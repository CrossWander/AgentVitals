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

internal sealed record ProviderView(string Name, string Info, string Updated, IReadOnlyList<CardView> Cards, string Error, string? Dashboard)
{
	/// <summary>Для карточки по наведению — только окна, которые блокируют всю работу.</summary>
	public IReadOnlyList<CardView> Glance => [.. Cards.Where(card => card.Limiting)];

	public bool HasError => Error.Length > 0;
}

internal sealed record SessionView(string Folder, string Branch, string State, IBrush Dot);

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

		return new(state.Provider.Name, info, Text.Ago(state.Updated, now), cards, state.Error is { } error ? Text.Error(error) : "", state.Provider.Dashboard);
	}

	private static CardView Card(QuotaWindow window, DateTime now)
	{
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
