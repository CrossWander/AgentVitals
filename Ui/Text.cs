using System;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;

namespace AgentVitals;

/// <summary>Подписи на языке интерфейса: из ключа, данных и текущего времени.</summary>
internal static class Text
{
	public static string Title(QuotaWindow window) => Localization.Format($"langWindow{window.Kind}", window.Label);

	/// <summary>«сброс через 1 ч 12 мин», а дальше суток — «сброс пт 10:00».</summary>
	public static string Until(DateTimeOffset? resets, DateTime now)
	{
		if (resets is not { } at)
		{
			return "";
		}

		var left = at.UtcDateTime - now;

		return left <= TimeSpan.Zero ? Localization.Get("langResetDone")
			: left > TimeSpan.FromDays(1) ? Localization.Format("langResetsAt", at.LocalDateTime.ToString("ddd HH:mm", Localization.Culture))
			: Localization.Format("langResetsIn", Span(left));
	}

	public static string Span(TimeSpan span) =>
		span.TotalHours >= 1
			? Localization.Format("langHoursMinutes", (int)span.TotalHours, span.Minutes)
			: Localization.Format("langMinutes", Math.Max(1, span.Minutes));

	public static string Ago(DateTime? at, DateTime now) =>
		at is not { } time ? ""
		: now - time < TimeSpan.FromMinutes(1) ? Localization.Get("langUpdatedNow")
		: Localization.Format("langUpdatedAgo", (int)(now - time).TotalMinutes);

	public static string Error(Exception error) => error switch
	{
		TokenExpiredException expired => Localization.Format("langErrorExpired", expired.Cli),
		RateLimitedException limited => Localization.Format("langErrorRateLimited", limited.Until.ToLocalTime().ToString("HH:mm", Localization.Culture)),
		HttpRequestException or TaskCanceledException => Localization.Get("langErrorNetwork"),
		_ => Localization.Get("langErrorParse"),
	};

	/// <summary>Плашка окна: по проценту, а исчерпанное — «исчерпано», а не «критично».</summary>
	public static string Badge(double used, Thresholds levels) =>
		Math.Round(used) >= 100 ? Localization.Get("langHealthExhausted") : HealthName(Quota.Rate(used, levels));

	/// <summary>Тост о пороге: заголовок — насколько серьёзно, текст — что, сколько осталось и когда сброс.</summary>
	public static (string Title, string Message) QuotaToast(QuotaAlert alert, DateTime now)
	{
		var left = Math.Max(0, 100 - Math.Round(alert.Window.Used));
		var message = Localization.Format("langAlertLeft", alert.Provider, Title(alert.Window), left);

		if (Until(alert.Window.ResetsAt, now) is { Length: > 0 } resets)
		{
			message += ", " + resets;
		}

		return (Localization.Get(alert.Critical ? "langAlertCritical" : "langAlertImportant"), message);
	}

	public static string HealthName(Health health) => Localization.Get(health switch
	{
		Health.Unknown => "langHealthUnknown",
		Health.Good => "langHealthGood",
		Health.Warning => "langHealthWarning",
		Health.Critical => "langHealthCritical",
	});

	/// <summary>Состояние сессии, а у идущего хода — и сколько он длится: «работает · 3 мин». Первую минуту таймера нет.</summary>
	public static string Session(Session session, DateTime now)
	{
		var state = session.State switch
		{
			SessionState.Idle => Localization.Get("langSessionIdle"),
			SessionState.Working => session.Tool is { } tool ? Localization.Format("langSessionWorkingTool", tool) : Localization.Get("langSessionWorking"),
			SessionState.Waiting => Localization.Get("langSessionWaiting"),
		};

		return session.TurnStarted is { } since && now - since >= TimeSpan.FromMinutes(1) ? $"{state} · {Span(now - since)}" : state;
	}

	/// <summary>Тост о сессии: заголовок — что случилось, текст — где и сколько ход занял.</summary>
	public static (string Title, string Message) SessionToast(SessionAlert alert) => alert.Waiting
		? (Localization.Get("langToastWaiting"), alert.Session.Folder)
		: (Localization.Get("langToastDone"), Localization.Format("langToastDoneText", alert.Session.Folder, Span(alert.Session.TurnDuration ?? TimeSpan.Zero)));

	public static string Live(Session session) => session.State switch
	{
		SessionState.Idle => "",
		SessionState.Working => session.Tool is { } tool
			? Localization.Format("langLiveTool", tool, session.Folder)
			: Localization.Format("langLiveWorking", session.Folder),
		SessionState.Waiting => Localization.Format("langLiveWaiting", session.Folder),
	};

	public static string Status(ServiceStatus? status) =>
		status is null ? Localization.Get("langStatusUnknown")
		: status.Health == Health.Good ? Localization.Get("langStatusOk")
		: status.Incident ?? status.Description;

	public static string StatusToast(ServiceStatus status) =>
		status.Health == Health.Good
			? Localization.Get("langToastStatusOk")
			: Localization.Format("langToastStatus", status.Incident ?? status.Description);

	public static string Money(decimal amount, string currency)
	{
		var number = amount.ToString("0.00", CultureInfo.InvariantCulture);

		return currency == "USD" ? "$" + number : $"{number} {currency}";
	}

	/// <summary>Стоимость или «—», если цен нет.</summary>
	public static string Cost(decimal? cost) => cost is { } amount ? Money(amount, "USD") : "—";

	/// <summary>Токены коротко: «152,3 млн», «12,4 тыс».</summary>
	public static string Tokens(long count)
	{
		var (key, value) = Scale(count);

		return Localization.Format(key, value.ToString("0.#", Localization.Culture));
	}

	internal static (string Key, double Value) Scale(long count) =>
		count >= 1_000_000_000 ? ("langBillions", count / 1e9)
		: count >= 1_000_000 ? ("langMillions", count / 1e6)
		: count >= 1_000 ? ("langThousands", count / 1e3)
		: ("langUnits", count);

	/// <summary>Время работы: «3 ч 12 мин», меньше часа — минуты; ноль — честные «0 мин», а не «1 мин», как у Span.</summary>
	public static string Working(TimeSpan span) =>
		span.TotalHours >= 1
			? Localization.Format("langHoursMinutes", (int)span.TotalHours, span.Minutes)
			: Localization.Format("langMinutes", span.Minutes);

	/// <summary>Строка дня для графика: «7 окт · $12.34 · 152,3 млн · 3 ч 12 мин».</summary>
	public static string DayLine(UsageDay day) =>
		string.Join(" · ", day.Date.ToString("d MMM", Localization.Culture), Cost(day.Cost), Tokens(day.Total), Working(day.WorkTime));
}
