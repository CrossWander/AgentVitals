using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AgentVitals;

/// <summary>Окно квоты: сколько израсходовано и когда сброс. Key — ключ строки названия, Label — подстановка в неё.</summary>
/// <remarks>Limiting — окно блокирует всю работу (сессия, неделя), а не одну модель. Used может быть больше 100.</remarks>
internal sealed record QuotaWindow(string Key, string? Label, double Used, DateTimeOffset? ResetsAt, bool Limiting);

/// <summary>Расход сверх тарифа в деньгах; Limit null — потолка нет.</summary>
internal sealed record Spend(decimal Used, decimal? Limit, string Currency);

/// <summary>Квоты одного провайдера на момент запроса.</summary>
internal sealed record Snapshot(string Plan, string? Account, IReadOnlyList<QuotaWindow> Windows, Spend? Extra);

/// <summary>Состояние квот; порядок значений — от лучшего к худшему, поэтому худшее — это Max.</summary>
public enum Health
{
	Unknown,
	Good,
	Warning,
	Critical,
}

/// <summary>Вход истёк: обновить его может только сам CLI агента.</summary>
internal sealed class TokenExpiredException(string cli) : Exception($"{cli}: sign-in expired")
{
	public string Cli { get; } = cli;
}

/// <summary>Сервер просит подождать до указанного момента.</summary>
internal sealed class RateLimitedException(DateTime until) : Exception($"rate limited until {until.ToLocalTime():HH:mm}")
{
	public DateTime Until { get; } = until;
}

/// <summary>Окно дошло до порога: осталась четверть (важно) или десятая часть (критично).</summary>
internal sealed record QuotaAlert(QuotaWindow Window, bool Critical);

/// <summary>
/// Тосты по сессии и неделе: «важно» при 75%, «критично» при 90%, каждый раз за цикл окна.
/// Ниже 75% — после сброса — пороги взводятся снова.
/// </summary>
internal sealed class QuotaAlarm
{
	private const double _important = 75;
	private const double _critical = 90;

	// Окно → уже сказали «критично» (true) или только «важно» (false).
	private readonly Dictionary<string, bool> _fired = [];

	/// <summary>Окна провайдера, которые только что дошли до следующего порога. Уже высокое при запуске — тоже:
	/// запуск утром или после обновления — как раз тот момент, когда об этом важно знать.</summary>
	/// <param name="provider">Имя агента: у каждого свои окна.</param>
	/// <param name="windows">Окна из свежего снимка.</param>
	/// <returns>По тревоге на окно, только о самом высоком из пройденных порогов.</returns>
	public IReadOnlyList<QuotaAlert> Check(string provider, IReadOnlyList<QuotaWindow> windows)
	{
		var alerts = new List<QuotaAlert>();

		foreach (var window in windows.Where(window => window.Limiting))
		{
			var key = $"{provider}/{window.Key}/{window.Label}";

			if (window.Used < _important)
			{
				_fired.Remove(key);

				continue;
			}

			var critical = window.Used >= _critical;

			// Уже сказали то же или больше.
			if (_fired.TryGetValue(key, out var said) && (said || !critical))
			{
				continue;
			}

			_fired[key] = critical;
			alerts.Add(new(window, critical));
		}

		return alerts;
	}
}

internal static class Quota
{
	/// <summary>Окно, которое ограничивает сильнее всех: среди блокирующих всё, а если таких нет — среди всех.</summary>
	public static QuotaWindow? Bottleneck(IReadOnlyList<QuotaWindow> windows) =>
		(windows.Any(window => window.Limiting) ? windows.Where(window => window.Limiting) : windows)
			.MaxBy(window => window.Used);

	public static Health Rate(double used) => used >= 80 ? Health.Critical : used >= 50 ? Health.Warning : Health.Good;

	public static Health Rate(IReadOnlyList<QuotaWindow> windows) =>
		Bottleneck(windows) is { } window ? Rate(window.Used) : Health.Unknown;

	/// <summary>Худшее из состояний: серое уступает любому известному.</summary>
	public static Health Worst(IEnumerable<Health> healths) => healths.DefaultIfEmpty(Health.Unknown).Max();

	/// <summary>Тариф для показа: «claude_max» → «Max», «team» → «Team».</summary>
	public static string PlanName(string? subscription) =>
		string.IsNullOrEmpty(subscription)
			? ""
			: CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
				subscription.Replace("claude_", "", StringComparison.OrdinalIgnoreCase).Replace('_', ' '));
}
