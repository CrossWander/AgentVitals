using System;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;

namespace AgentVitals;

/// <summary>Подписи на языке интерфейса: из ключа, данных и текущего времени.</summary>
internal static class Text
{
	public static string Title(QuotaWindow window) => Localization.Format(window.Key, window.Label);

	/// <summary>«сброс через 1 ч 12 мин», а дальше суток — «сброс пт 10:00».</summary>
	public static string Until(DateTimeOffset? resets, DateTime now)
	{
		if (resets is not { } at)
		{
			return "";
		}

		var left = at.LocalDateTime - now;

		return left <= TimeSpan.Zero ? Localization.Get("langResetsSoon")
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
		RateLimitedException limited => Localization.Format("langErrorRateLimited", limited.Until.ToString("HH:mm", Localization.Culture)),
		HttpRequestException or TaskCanceledException => Localization.Get("langErrorNetwork"),
		_ => Localization.Get("langErrorParse"),
	};

	public static string HealthName(Health health) => Localization.Get(health switch
	{
		Health.Unknown => "langHealthUnknown",
		Health.Good => "langHealthGood",
		Health.Warning => "langHealthWarning",
		Health.Critical => "langHealthCritical",
		_ => throw new NotImplementedException("Health switch error"),
	});

	public static string Session(LiveSession session) => session.State switch
	{
		SessionState.Idle => Localization.Get("langSessionIdle"),
		SessionState.Working => session.Tool is { } tool ? Localization.Format("langSessionWorkingTool", tool) : Localization.Get("langSessionWorking"),
		SessionState.Waiting => Localization.Get("langSessionWaiting"),
		_ => throw new NotImplementedException("SessionState switch error"),
	};

	public static string Live(LiveSession session) => session.State switch
	{
		SessionState.Idle => "",
		SessionState.Working => session.Tool is { } tool
			? Localization.Format("langLiveTool", tool, session.Folder)
			: Localization.Format("langLiveWorking", session.Folder),
		SessionState.Waiting => Localization.Format("langLiveWaiting", session.Folder),
		_ => throw new NotImplementedException("SessionState switch error"),
	};

	public static string Status(ServiceStatus? status) =>
		status is null ? Localization.Get("langStatusUnknown")
		: status.Indicator == "none" ? Localization.Get("langStatusOk")
		: status.Incident ?? status.Description;

	public static string StatusToast(ServiceStatus status) =>
		status.Indicator == "none"
			? Localization.Get("langToastStatusOk")
			: Localization.Format("langToastStatus", status.Incident ?? status.Description);

	public static string QuotaToast(string provider, QuotaWindow window) =>
		Localization.Format("langToastQuota", provider, Title(window), Math.Round(window.Used));

	public static string Money(decimal amount, string currency)
	{
		var number = amount.ToString("0.00", CultureInfo.InvariantCulture);

		return currency == "USD" ? "$" + number : $"{number} {currency}";
	}
}
