using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
	bool Limiting,
	double? Pace = null,
	string PaceWord = "",
	IBrush? PaceBrush = null,
	string? PaceTip = null)
{
	/// <summary>Метка ровного темпа на полоске: где был бы расход, если тратить равномерно.</summary>
	public bool HasPace => Pace is not null;

	/// <summary>Подпись у числа в окне: темп, если он известен, иначе «использовано».</summary>
	public string Caption => PaceWord.Length > 0 ? PaceWord : Note;

	// Пустая кисть сделала бы подпись невидимой: у доплаты темпа нет.
	public IBrush CaptionBrush => PaceBrush ?? Views.Brush("TextDim");

	/// <summary>Нижняя строка в карточке наведения: там шире, темп встаёт после сброса.</summary>
	public string Footer => PaceWord.Length > 0 && Resets.Length > 0 ? $"{Resets} · {PaceWord}" : Resets;

	public string Display => Value + Unit;
}

internal sealed record ProviderView(string Name, string Info, string Updated, IReadOnlyList<CardView> Cards, string Error, string? Dashboard, UsageView? Usage)
{
	/// <summary>Для карточки по наведению — только окна, которые блокируют всю работу.</summary>
	public IReadOnlyList<CardView> LimitingCards => [.. Cards.Where(card => card.Limiting)];

	public bool HasError => Error.Length > 0;
}

/// <param name="Session">Сама сессия: по щелчку на строке поднимается окно, где она идёт.</param>
internal sealed record SessionView(string Folder, string Branch, string State, IBrush Dot, Session Session)
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
	Models,
}

/// <summary>Слой столбика: высота в точках и цвет.</summary>
internal sealed record BarPart(double Height, IBrush Brush);

/// <summary>Пункт легенды графика.</summary>
internal sealed record LegendItem(IBrush Brush, string Text);

/// <summary>Столбик дня: слои снизу вверх.</summary>
internal sealed record BarView(DateOnly Date, IReadOnlyList<BarPart> Parts, double Opacity, string Line)
{
	/// <summary>Слои сверху вниз — в таком порядке их кладёт StackPanel.</summary>
	public IReadOnlyList<BarPart> Stack => [.. Parts.Reverse()];
}

/// <summary>График в выбранной мере: столбики, шкала сверху вниз, даты под столбиками, легенда и сумма за 30 дней.</summary>
internal sealed record ChartView(
	IReadOnlyList<BarView> Bars,
	IReadOnlyList<string> AxisY,
	IReadOnlyList<(int Index, string Text)> AxisX,
	IReadOnlyList<LegendItem> Legend,
	string Total);

/// <summary>Блок статистики вкладки: «Сегодня» и дни для графика.</summary>
internal sealed record UsageView(
	string Cost,
	string CostNote,
	string CostYesterday,
	string Tokens,
	string TokensNote,
	string TokensYesterday,
	string WorkTime,
	string WorkTimeYesterday,
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
	/// <param name="agents">Установленные агенты.</param>
	/// <param name="status">Статус Anthropic; null — страница не ответила.</param>
	/// <param name="hooks">Подключены ли hooks: без них список сессий пуст не потому, что их нет.</param>
	/// <param name="sessions">Живые сессии Claude Code.</param>
	/// <param name="now">Момент снимка: от него «обновлено N мин назад» и «сброс через».</param>
	/// <param name="levels">Пороги из настроек.</param>
	public static Vitals Build(IReadOnlyList<ProviderState> agents, ServiceStatus? status, HookLink hooks, IReadOnlyList<Session> sessions, DateTime now, Thresholds levels)
	{
		var health = Poller.Health(agents, now, levels);

		// Шапка говорит о самом узком окне: исчерпано оно — «исчерпано», как на его карточке.
		var tightest = Poller.Tightest(agents, now);

		var live = SessionWatcher.Overall(sessions);
		var focus = sessions.FirstOrDefault(session => session.State == live);

		var hint = hooks != HookLink.On ? Localization.Get("langHooksHint")
			: sessions.Count == 0 ? Localization.Get("langNoSessions")
			: "";

		return new(
			tightest is { } used ? Text.Badge(used, levels) : Text.HealthName(health),
			Brush(Level(health)),
			[.. agents.Select(state => Provider(state, now, levels))],
			Text.Status(status),
			Brush(Level(status?.Health ?? Health.Unknown)),
			[.. sessions.Select(session => new SessionView(session.Folder, session.Branch ?? "", Text.Session(session, now), Brush(Dot(session.State)), session))],
			hint,
			focus is null ? "" : Text.Live(focus),
			Brush(Dot(live)));
	}

	internal static ProviderView Provider(ProviderState state, DateTime now, Thresholds levels)
	{
		List<CardView> cards = [];

		if (state.Snapshot is { } snapshot)
		{
			cards.AddRange(state.Windows(now).Select(window => Card(window, now, levels)));

			if (snapshot.Extra is { } extra)
			{
				cards.Add(Extra(extra, levels));
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
			Text.Span(today.WorkTime, least: 0),
			Localization.Format("langYesterday", Text.Span(yesterday.WorkTime, least: 0)),
			days);
	}

	/// <summary>Всё, что рисует график, — за один подсчёт шкалы.</summary>
	internal static ChartView Chart(IReadOnlyList<UsageDay> days, ChartMode mode)
	{
		var ceiling = Ceiling(Top(days, mode));

		// Без дней «$0.00 за месяц» было бы неправдой: дней просто ещё нет.
		return new(Bars(days, mode, ceiling), AxisY(ceiling, mode), AxisX(days), Legend(mode), days.Count == 0 ? "" : Total(days, mode));
	}

	/// <summary>Столбики в выбранной мере: во всю высоту — верх шкалы, сегодняшний ярче.</summary>
	private static IReadOnlyList<BarView> Bars(IReadOnlyList<UsageDay> days, ChartMode mode, double ceiling)
	{
		var layers = Layers(mode);
		var scale = ceiling > 0 ? ChartHeight / ceiling : 0;

		return [.. days.Select((day, index) => new BarView(
			day.Date,
			[.. layers.Select(layer => new BarPart(layer.Value(day) * scale, Brush(layer.Brush)))],
			index == days.Count - 1 ? 1 : 0.6,
			Text.DayLine(day)))];
	}

	/// <summary>Самый большой день в выбранной мере.</summary>
	private static double Top(IReadOnlyList<UsageDay> days, ChartMode mode) =>
		days.Select(day => Layers(mode).Sum(layer => layer.Value(day))).DefaultIfEmpty(0).Max();

	/// <summary>Легенда под графиком: по пункту на слой.</summary>
	internal static IReadOnlyList<LegendItem> Legend(ChartMode mode) => [.. Layers(mode).Select(layer => new LegendItem(Brush(layer.Brush), layer.Name))];

	// Слои меры снизу вверх: цвет, подпись в легенде и значение дня. По ним — и столбики, и легенда, и шкала.
	private static IReadOnlyList<(string Brush, string Name, Func<UsageDay, double> Value)> Layers(ChartMode mode) => mode switch
	{
		ChartMode.Cost => [("Blue", Localization.Get("langCostNote"), day => (double)(day.Cost ?? 0))],
		ChartMode.Tokens => [("Blue", Localization.Get("langInput"), day => day.Input), ("Good", Localization.Get("langOutput"), day => day.Output)],
		ChartMode.Cache => [("Blue", Localization.Get("langCacheWrite"), day => day.CacheWrite), ("Good", Localization.Get("langCacheRead"), day => day.CacheRead)],
		ChartMode.Models => [.. Enum.GetValues<ModelFamily>().Select(family => (
			family switch
			{
				ModelFamily.Opus => "Blue",
				ModelFamily.Sonnet => "Good",
				ModelFamily.Haiku => "Warn",
				ModelFamily.Fable => "Bad",
				_ => "TextFaint",
			},
			family == ModelFamily.Other ? Localization.Get("langOther") : family.ToString(),
			(Func<UsageDay, double>)(day => (double)(day.Models?.All[(int)family] ?? 0))))],
	};

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
		string Label(double value) => mode is ChartMode.Cost or ChartMode.Models
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
		ChartMode.Cost or ChartMode.Models => Text.Cost(days.All(day => day.Cost is not null) ? days.Sum(day => day.Cost!.Value) : null),
		ChartMode.Tokens => Text.Tokens(days.Sum(day => day.Input + day.Output)),

		// Без цен экономию не посчитать — тогда только токены.
		ChartMode.Cache => Text.Tokens(days.Sum(day => day.CacheWrite + day.CacheRead))
			+ (days.Any(day => day.Saved is not null) ? " · " + Localization.Format("langCacheSaved", Text.Cost(days.Sum(day => day.Saved ?? 0))) : ""),
	};

	private static CardView Card(QuotaWindow window, DateTime now, Thresholds levels)
	{
		var health = Quota.Rate(window.Used, levels);
		var (icon, tint) = window.Kind switch
		{
			WindowKind.Session => ("IconBolt", "IconSession"),
			WindowKind.Week => ("IconCalendar", "IconWeek"),
			WindowKind.WeekModel or WindowKind.Model => ("IconSpark", "IconModel"),
		};

		var pace = Quota.Expected(window, now);
		Pace? word = pace is { } expected ? Quota.Pace(window.Used, expected) : null;

		return new(
			Text.Title(window).ToUpper(Localization.Culture),
			Shape(icon),
			Brush(tint),
			Text.Badge(window.Used, levels),
			Brush("Badge" + Level(health)),
			Math.Round(window.Used).ToString(CultureInfo.InvariantCulture),
			"%",
			Localization.Get("langUsed"),
			Math.Min(window.Used, 100),
			Brush("Meter" + Level(health)),
			Text.Until(window.ResetsAt, now),
			window.Limiting,
			pace,
			word is null ? "" : Localization.Get($"langPace{word}"),
			Brush(word switch
			{
				Pace.Ahead => "Warn",
				Pace.Spare => "Good",
				Pace.Even or null => "TextDim",
			}),
			pace is { } even ? Localization.Format("langPaceTip", Math.Round(even), Math.Round(window.Used)) : null);
	}

	private static CardView Extra(ExtraUsage extra, Thresholds levels)
	{
		var fill = extra.Limit is { } limit && limit > 0 ? (double)(extra.Used / limit * 100) : 0;

		return new(
			Localization.Get("langExtra").ToUpper(Localization.Culture),
			Shape("IconCoin"),
			Brush("IconExtra"),
			"",
			Brushes.Transparent,
			Text.Money(extra.Used, extra.Currency),
			"",
			extra.Limit is { } ceiling ? Localization.Format("langExtraOf", Text.Money(ceiling, extra.Currency)) : Localization.Get("langExtraSpent"),
			Math.Min(fill, 100),
			Brush("Meter" + Level(Quota.Rate(fill, levels))),
			"",
			false);
	}

	// Цвет состояния в палитре; с приставкой «Badge» и «Meter» — его вариант для бейджа и полоски.
	private static string Level(Health health) => health switch
	{
		Health.Unknown => "Muted",
		Health.Good => "Good",
		Health.Warning => "Warn",
		Health.Critical => "Bad",
	};

	private static string Dot(SessionState state) => state switch
	{
		SessionState.Idle => "Muted",
		SessionState.Working => "Good",
		SessionState.Waiting => "Blue",
	};

	internal static IBrush Brush(string key) => Find(key) as IBrush ?? Brushes.Gray;

	private static Geometry? Shape(string key) => Find(key) as Geometry;

	private static object? Find(string key) =>
		Application.Current is { } application && application.TryGetResource(key, application.ActualThemeVariant, out var value)
			? value
			: null;
}
