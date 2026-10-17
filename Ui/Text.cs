using System;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;

namespace AgentVitals;

/// <summary>Подписи на языке интерфейса: из ключа, данных и текущего времени.</summary>
internal static class Text
{
	/// <summary>Заголовок окна. У квоты функции — перевод её имени, а неизвестное имя — как есть.</summary>
	public static string Title(QuotaWindow window) =>
		window.Kind == WindowKind.MonthFeature
			? Localization.Get($"langFeature_{window.Label}") is var name && name != $"langFeature_{window.Label}" ? name : window.Label ?? ""
			: Localization.Format($"langWindow{window.Kind}", window.Label);

	/// <summary>«сброс через 1 ч 12 мин», дальше суток — «сброс пт 10:00», а дальше недели — «сброс 1 нояб.».</summary>
	public static string Until(DateTimeOffset? resets, DateTime now)
	{
		if (resets is not { } at)
		{
			return "";
		}

		var left = at.UtcDateTime - now;

		return left <= TimeSpan.Zero ? Localization.Get("langResetDone")
			: left > TimeSpan.FromDays(1) ? Localization.Format("langResetsAt", at.LocalDateTime.ToString(ResetPattern(left), Localization.Culture))
			: Localization.Format("langResetsIn", Span(left));
	}

	/// <summary>Как назвать далёкий сброс: в пределах недели — днём недели, а дальше — датой: у месячного окна
	/// «вс 02:00» читалось бы как ближайшее воскресенье.</summary>
	internal static string ResetPattern(TimeSpan left) => left > TimeSpan.FromDays(7) ? "d MMM" : "ddd HH:mm";

	/// <param name="least">Меньше скольких минут не показывать: сброс через полминуты — «1 мин», а день без работы — «0 мин».</param>
	public static string Span(TimeSpan span, int least = 1) =>
		span.TotalHours >= 1
			? Localization.Format("langHoursMinutes", (int)span.TotalHours, span.Minutes)
			: Localization.Format("langMinutes", Math.Max(least, span.Minutes));

	/// <summary>«обновлено 10 ч 5 мин назад», а старше суток — «обновлено пт 18:40»: ответ после выходных помнится тоже.</summary>
	public static string Ago(DateTime? at, DateTime now) =>
		at is not { } time ? ""
		: now - time < TimeSpan.FromMinutes(1) ? Localization.Get("langUpdatedNow")
		: now - time > TimeSpan.FromDays(1) ? Localization.Format("langUpdatedAt", time.ToLocalTime().ToString("ddd HH:mm", Localization.Culture))
		: Localization.Format("langUpdatedAgo", Span(now - time));

	public static string Error(Exception error) => error switch
	{
		TokenExpiredException expired => Localization.Format(expired.Text, expired.Cli),
		RateLimitedException limited => Localization.Format("langErrorRateLimited", limited.Until.ToLocalTime().ToString("HH:mm", Localization.Culture)),
		HttpRequestException or TaskCanceledException => Localization.Get("langErrorNetwork"),
		_ => Localization.Get("langErrorParse"),
	};

	/// <summary>Плашка окна: по проценту, а исчерпанное — «исчерпано», а не «критично».</summary>
	public static string Badge(double used, Thresholds levels) =>
		Math.Round(used) >= 100 ? Localization.Get("langHealthExhausted") : HealthName(Quota.Rate(used, levels));

	/// <summary>Тост о квоте: заголовок — насколько серьёзно, текст — что, сколько осталось, на сколько хватит и когда сброс.</summary>
	public static (string Title, string Message) QuotaToast(QuotaAlert alert, DateTime now)
	{
		if (alert.Level == QuotaLevel.Reset)
		{
			return (Localization.Get("langAlertReset"), Localization.Format("langAlertResetText", alert.Provider, Title(alert.Window)));
		}

		var left = Math.Max(0, 100 - Math.Round(alert.Window.Used));
		var message = Localization.Format("langAlertLeft", alert.Provider, Title(alert.Window), left);

		if (Quota.Runout(alert.Window, now) is { } runout)
		{
			message += ", " + Runout(runout, now);
		}

		if (Until(alert.Window.ResetsAt, now) is { Length: > 0 } resets)
		{
			message += ", " + resets;
		}

		return (Localization.Get(alert.Level switch
		{
			QuotaLevel.Pace => "langAlertPace",
			QuotaLevel.Critical => "langAlertCritical",
			QuotaLevel.Important or QuotaLevel.Reset => "langAlertImportant",
		}), message);
	}

	/// <summary>«хватит до 13:40», а не сегодня — «хватит до чт 13:40».</summary>
	public static string Runout(DateTime at, DateTime now)
	{
		var local = at.ToLocalTime();

		return Localization.Format("langPaceRunout", local.ToString(local.Date == now.ToLocalTime().Date ? "HH:mm" : "ddd HH:mm", Localization.Culture));
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

	/// <summary>Тост о сессии: заголовок — кто и что, текст — где и сколько ход занял.</summary>
	public static (string Title, string Message) SessionToast(SessionAlert alert) => alert.Waiting
		? (Localization.Format("langToastWaiting", alert.Session.Agent), alert.Session.Folder)
		: (Localization.Format("langToastDone", alert.Session.Agent), Localization.Format("langToastDoneText", alert.Session.Folder, Span(alert.Session.TurnDuration ?? TimeSpan.Zero)));

	public static string Live(Session session) => session.State switch
	{
		SessionState.Idle => "",
		SessionState.Working => session.Tool is { } tool
			? Localization.Format("langLiveTool", session.Agent, tool, session.Folder)
			: Localization.Format("langLiveWorking", session.Agent, session.Folder),
		SessionState.Waiting => Localization.Format("langLiveWaiting", session.Agent, session.Folder),
	};

	/// <param name="name">Чья страница: если она не ответила, имени в статусе нет.</param>
	public static string Status(ServiceStatus? status, string name) =>
		status is null ? Localization.Format("langStatusUnknown", name)
		: status.Health == Health.Good ? Localization.Format("langStatusOk", status.Name)
		: status.Incident ?? status.Description;

	public static string StatusToast(ServiceStatus status) =>
		status.Health == Health.Good
			? Localization.Format("langToastStatusOk", status.Name)
			: Localization.Format("langToastStatus", status.Name, status.Incident ?? status.Description);

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

	/// <summary>Строка дня для графика: «7 окт · $12.34 · 152,3 млн · 3 ч 12 мин».</summary>
	public static string DayLine(UsageDay day) =>
		string.Join(" · ", day.Date.ToString("d MMM", Localization.Culture), Cost(day.Cost), Tokens(day.Total), Span(day.WorkTime, least: 0));
}
