using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Media;

namespace AgentVitals;

/// <summary>Карточка окна квоты (или доп. расхода) в том виде, в каком её рисуют окна.</summary>
internal sealed record CardView(
	string Title,
	Geometry? Icon,
	IBrush IconBrush,
	string Badge,
	IBrush BadgeBrush,
	string Value,
	string Unit,
	string Note,
	double Fill,
	IBrush Meter,
	string Resets,
	bool Limiting)
{
	public string Display => Value + Unit;
}

internal sealed record ProviderView(string Name, string Info, string Updated, IReadOnlyList<CardView> Cards, string Error, string? Dashboard, UsageView? Usage)
{
	/// <summary>Для карточки по наведению — только окна, которые блокируют всю работу.</summary>
	public IReadOnlyList<CardView> Glance => [.. Cards.Where(card => card.Limiting)];

	public bool HasError => Error.Length > 0;
}

internal sealed record SessionView(string Folder, string Branch, string State, IBrush Dot)
{
	/// <summary>Подсказка над строкой: длинные имя папки и ветка в строке обрезаны троеточием.</summary>
	public string Tip => Branch.Length > 0 ? $"{Folder}\n{Branch}" : Folder;
}

/// <summary>Что откладывать по высоте столбиков графика.</summary>
internal enum ChartMode
{
	Cost,
	Tokens,
	Cache,
}

/// <summary>Столбик дня: нижняя и верхняя части в пикселях, прозрачность и строка для наведения.</summary>
internal sealed record BarView(DateOnly Date, double Low, double High, double Opacity, string Line);

/// <summary>Блок статистики вкладки: «Сегодня» и дни для графика.</summary>
internal sealed record UsageView(
	string Cost,
	string CostNote,
	string CostYesterday,
	string Tokens,
	string TokensNote,
	string TokensYesterday,
	string Working,
	string WorkingYesterday,
	IReadOnlyList<UsageDay> Days);

/// <summary>Всё, что показывают окна, одним снимком.</summary>
internal sealed record Vitals(
	string HealthText,
	IBrush HealthBrush,
	IReadOnlyList<ProviderView> Providers,
	string StatusText,
	IBrush StatusBrush,
	IReadOnlyList<SessionView> Sessions,
	string SessionsHint,
	string LiveText,
	IBrush LiveBrush)
{
	public bool NoAgents => Providers.Count == 0;
}

/// <summary>Собирает представления из данных опроса и сессий. Кисти — из палитры текущей темы.</summary>
internal static class Views
{
	public static Vitals Build(Poller poller, IReadOnlyList<LiveSession> sessions, DateTime now)
	{
		var available = poller.Available.ToList();
		var health = poller.Health;

		// Шапка говорит о самом узком окне: исчерпано оно — «исчерпано», как на его карточке.
		var tightest = available
			.Where(state => state.Health != Health.Unknown)
			.Select(state => Quota.Bottleneck(state.Snapshot!.Windows)?.Used ?? 0)
			.DefaultIfEmpty(0)
			.Max();

		var live = SessionWatcher.Overall(sessions);
		var focus = sessions.FirstOrDefault(session => session.State == live);

		var hint = Hooks(out _) != HookLink.On ? Localization.Get("langHooksHint")
			: sessions.Count == 0 ? Localization.Get("langNoSessions")
			: "";

		return new(
			health == Health.Unknown ? Text.HealthName(health) : Text.Badge(tightest),
			Brush(Tone(health)),
			[.. available.Select(state => Provider(state, now))],
			Text.Status(poller.Status),
			Brush(poller.Status is { } status ? Tone(status.Health) : "Muted"),
			[.. sessions.Select(session => new SessionView(session.Folder, session.Branch ?? "", Text.Session(session), Brush(Dot(session.State))))],
			hint,
			focus is null ? "" : Text.Live(focus),
			Brush(Dot(live)));
	}

	/// <summary>Подключены ли hooks; ошибка чтения settings.json — «нет» с текстом ошибки.</summary>
	public static HookLink Hooks(out string? error)
	{
		error = null;

		try
		{
			return HookInstaller.State(HookInstaller.SettingsPath, Environment.ProcessPath ?? "");
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
		{
			error = exception.Message;

			return HookLink.Off;
		}
	}

	internal static ProviderView Provider(ProviderState state, DateTime now)
	{
		List<CardView> cards = [];

		if (state.Snapshot is { } snapshot)
		{
			cards.AddRange(snapshot.Windows.Select(window => Card(window, now)));

			if (snapshot.Extra is { } extra)
			{
				cards.Add(Extra(extra));
			}
		}

		var info = string.Join(" · ", new[] { state.Snapshot?.Plan, state.Snapshot?.Account }.Where(text => !string.IsNullOrEmpty(text)));

		return new(
			state.Provider.Name,
			info,
			Text.Ago(state.Updated, now),
			cards,
			state.Error is { } error ? Text.Error(error) : "",
			state.Provider.Dashboard,
			state.Provider.Usage is null ? null : Usage(state.Days));
	}

	public const double ChartHeight = 90;

	/// <summary>«Сегодня» — последний день, «вчера» — предпоследний.</summary>
	internal static UsageView Usage(IReadOnlyList<UsageDay>? days)
	{
		// Первое чтение логов идёт секунды: до него — «считаю…», а не нули.
		if (days is not { Count: >= 2 })
		{
			return new("…", Localization.Get("langCounting"), "", "…", "", "", "…", "", []);
		}

		var today = days[^1];
		var yesterday = days[^2];
		var costNote = today.Cost is null ? "langCostMissing" : today.Unpriced ? "langCostPartial" : "langCostNote";

		return new(
			Text.Cost(today.Cost),
			Localization.Get(costNote),
			Localization.Format("langYesterday", Text.Cost(yesterday.Cost)),
			Text.Tokens(today.Total),
			today.Total == 0 ? "" : Localization.Format("langFromCache", Math.Round(100.0 * today.CacheRead / today.Total)),
			Localization.Format("langYesterday", Text.Tokens(yesterday.Total)),
			Text.Working(today.Working),
			Localization.Format("langYesterday", Text.Working(yesterday.Working)),
			days);
	}

	/// <summary>Столбики в выбранной мере: во всю высоту — верх шкалы, сегодняшний ярче.</summary>
	internal static IReadOnlyList<BarView> Bars(IReadOnlyList<UsageDay> days, ChartMode mode)
	{
		var parts = days.Select(day => Parts(day, mode)).ToList();
		var ceiling = Ceiling(Top(days, mode));
		var scale = ceiling > 0 ? ChartHeight / ceiling : 0;

		return [.. days.Select((day, index) => new BarView(
			day.Date,
			parts[index].Low * scale,
			parts[index].High * scale,
			index == days.Count - 1 ? 1 : 0.6,
			Text.DayLine(day)))];
	}

	/// <summary>Самый большой день в выбранной мере.</summary>
	internal static double Top(IReadOnlyList<UsageDay> days, ChartMode mode) =>
		days.Select(day => Parts(day, mode)).Select(part => part.Low + part.High).DefaultIfEmpty(0).Max();

	/// <summary>Верх шкалы: две «круглые» ступени, как $0 / $80 / $160, не ниже самого большого дня.</summary>
	internal static double Ceiling(double top)
	{
		if (top <= 0)
		{
			return 0;
		}

		var half = top / 2;
		var power = Math.Pow(10, Math.Floor(Math.Log10(half)));

		return 2 * new[] { 1, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10 }.Select(step => step * power).First(step => step >= half);
	}

	/// <summary>Подписи оси Y сверху вниз: верх шкалы, середина, ноль.</summary>
	internal static IReadOnlyList<string> AxisY(double ceiling, ChartMode mode)
	{
		string Label(double value) => mode == ChartMode.Cost
			? "$" + value.ToString("0.##", CultureInfo.InvariantCulture)
			: value == 0 ? "0" : Text.Tokens((long)value);

		return ceiling > 0 ? [Label(ceiling), Label(ceiling / 2), Label(0)] : ["", "", Label(0)];
	}

	/// <summary>Подписи оси X: каждый седьмой день от первого — номер столбика и дата.</summary>
	internal static IReadOnlyList<(int Index, string Text)> AxisX(IReadOnlyList<UsageDay> days) =>
		[.. days.Select((day, index) => (Index: index, day.Date)).Where(item => item.Index % 7 == 0)
			.Select(item => (item.Index, item.Date.ToString("d MMM", Localization.Culture)))];

	/// <summary>Сумма за 30 дней в выбранной мере — в углу графика.</summary>
	internal static string Total(IReadOnlyList<UsageDay> days, ChartMode mode) => mode switch
	{
		ChartMode.Cost => Text.Cost(days.All(day => day.Cost is not null) ? days.Sum(day => day.Cost!.Value) : null),
		ChartMode.Tokens => Text.Tokens(days.Sum(day => day.Input + day.Output)),
		ChartMode.Cache => Text.Tokens(days.Sum(day => day.CacheWrite + day.CacheRead)),
		_ => throw new NotImplementedException("ChartMode switch error"),
	};

	private static (double Low, double High) Parts(UsageDay day, ChartMode mode) => mode switch
	{
		ChartMode.Cost => ((double)(day.Cost ?? 0), 0),
		ChartMode.Tokens => (day.Input, day.Output),
		ChartMode.Cache => (day.CacheWrite, day.CacheRead),
		_ => throw new NotImplementedException("ChartMode switch error"),
	};

	private static CardView Card(QuotaWindow window, DateTime now)
	{
		// Сброс прошёл, а свежих данных нет — после сна вход истёк или нет сети: окно точно обнулилось.
		// ponytail: расход с других устройств после сброса не виден, пока не придёт новый снимок.
		if (window.ResetsAt?.UtcDateTime <= now)
		{
			window = window with { Used = 0 };
		}

		var health = Quota.Rate(window.Used);
		var (icon, tint) = window.Key switch
		{
			"langWindowSession" => ("IconBolt", "IconSession"),
			"langWindowWeek" => ("IconCalendar", "IconWeek"),
			_ => ("IconSpark", "IconModel"),
		};

		return new(
			Text.Title(window).ToUpper(Localization.Culture),
			Shape(icon),
			Brush(tint),
			Text.Badge(window.Used),
			Brush(Badge(health)),
			Math.Round(window.Used).ToString(CultureInfo.InvariantCulture),
			"%",
			Localization.Get("langUsed"),
			Math.Min(window.Used, 100),
			Brush(Meter(health)),
			Text.Until(window.ResetsAt, now),
			window.Limiting);
	}

	private static CardView Extra(Spend spend)
	{
		var fill = spend.Limit is { } limit && limit > 0 ? (double)(spend.Used / limit * 100) : 0;

		return new(
			Localization.Get("langExtra").ToUpper(Localization.Culture),
			Shape("IconCoin"),
			Brush("IconExtra"),
			"",
			Brushes.Transparent,
			Text.Money(spend.Used, spend.Currency),
			"",
			spend.Limit is { } ceiling ? Localization.Format("langExtraOf", Text.Money(ceiling, spend.Currency)) : Localization.Get("langExtraSpent"),
			Math.Min(fill, 100),
			Brush(Meter(Quota.Rate(fill))),
			"",
			false);
	}

	private static string Tone(Health health) => health switch
	{
		Health.Unknown => "Muted",
		Health.Good => "Good",
		Health.Warning => "Warn",
		Health.Critical => "Bad",
		_ => throw new NotImplementedException("Health switch error"),
	};

	private static string Badge(Health health) => health switch
	{
		Health.Unknown => "BadgeNone",
		Health.Good => "BadgeGood",
		Health.Warning => "BadgeWarn",
		Health.Critical => "BadgeBad",
		_ => throw new NotImplementedException("Health switch error"),
	};

	private static string Meter(Health health) => health switch
	{
		Health.Unknown => "MeterNone",
		Health.Good => "MeterGood",
		Health.Warning => "MeterWarn",
		Health.Critical => "MeterBad",
		_ => throw new NotImplementedException("Health switch error"),
	};

	private static string Dot(SessionState state) => state switch
	{
		SessionState.Idle => "Muted",
		SessionState.Working => "Good",
		SessionState.Waiting => "Blue",
		_ => throw new NotImplementedException("SessionState switch error"),
	};

	internal static IBrush Brush(string key) => Find(key) as IBrush ?? Brushes.Gray;

	private static Geometry? Shape(string key) => Find(key) as Geometry;

	private static object? Find(string key) =>
		Application.Current is { } application && application.TryGetResource(key, application.ActualThemeVariant, out var value)
			? value
			: null;
}
