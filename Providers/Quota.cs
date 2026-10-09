using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AgentVitals;

/// <summary>Вид окна квоты: от него длина окна, название и значок.</summary>
public enum WindowKind
{
	Session,
	Week,

	/// <summary>Недельный лимит одной модели (Label — её имя).</summary>
	WeekModel,

	/// <summary>Лимит модели неизвестной длины (Label — её имя).</summary>
	Model,
}

/// <summary>Окно квоты: сколько израсходовано и когда сброс. Label — имя модели у модельных окон.</summary>
/// <remarks>Limiting — окно блокирует всю работу (сессия, неделя), а не одну модель. Used может быть больше 100.</remarks>
internal sealed record QuotaWindow(WindowKind Kind, string? Label, double Used, DateTimeOffset? ResetsAt, bool Limiting)
{
	/// <summary>Длина окна; null — неизвестна.</summary>
	public TimeSpan? Length => Kind switch
	{
		WindowKind.Session => TimeSpan.FromHours(5),
		WindowKind.Week or WindowKind.WeekModel => TimeSpan.FromDays(7),
		WindowKind.Model => null,
	};

	/// <summary>Окно на этот момент. Сброс прошёл, а свежего снимка нет — после сна вход истёк или нет сети: окно точно обнулилось.</summary>
	/// <remarks>ponytail: расход с других устройств после сброса не виден, пока не придёт новый снимок.</remarks>
	public QuotaWindow At(DateTime now) => ResetsAt?.UtcDateTime <= now ? this with { Used = 0 } : this;
}

/// <summary>Расход сверх тарифа в деньгах; Limit null — потолка нет.</summary>
internal sealed record ExtraUsage(decimal Used, decimal? Limit, string Currency);

/// <summary>Квоты одного провайдера на момент запроса.</summary>
internal sealed record Snapshot(string Plan, string? Account, IReadOnlyList<QuotaWindow> Windows, ExtraUsage? Extra);

/// <summary>Темп расхода против ровного: с запасом, ровно, с опережением.</summary>
internal enum Pace
{
	Spare,
	Even,
	Ahead,
}

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

/// <summary>Пороги «важно» и «критично», %: с них и тосты, и жёлтый с красным — что красное, о том и тост.</summary>
internal sealed record Thresholds(int Important, int Critical);

/// <summary>Окно агента дошло до порога «важно» или «критично».</summary>
internal sealed record QuotaAlert(string Provider, QuotaWindow Window, bool Critical);

/// <summary>
/// Тосты по сессии и неделе: «важно» и «критично» (по умолчанию 75% и 90%, меняются в настройках), каждый раз
/// за цикл окна. Ниже порога «важно» — после сброса — пороги взводятся снова.
/// </summary>
internal sealed class QuotaAlarm
{
	private const int _step = 5;
	private const int _lowest = 50;
	private const int _highest = 95;

	// Окно → уже сказали «критично» (true) или только «важно» (false).
	private readonly Dictionary<string, bool> _fired = [];

	/// <summary>Сдвинуть порог на шаг: от 50 до 95, «важно» всегда хотя бы на шаг ниже «критично».</summary>
	public static Thresholds Shift(Thresholds levels, bool moveCritical, int delta) => moveCritical
		? levels with { Critical = Math.Clamp(levels.Critical + delta, Math.Max(_lowest, levels.Important + _step), _highest) }
		: levels with { Important = Math.Clamp(levels.Important + delta, _lowest, Math.Min(_highest, levels.Critical - _step)) };

	/// <summary>Окна провайдера, которые только что дошли до следующего порога. Уже высокое при запуске — тоже:
	/// запуск утром или после обновления — как раз тот момент, когда об этом важно знать.</summary>
	/// <param name="provider">Имя агента: у каждого свои окна.</param>
	/// <param name="windows">Окна из свежего снимка.</param>
	/// <param name="levels">Пороги из настроек.</param>
	/// <returns>По тревоге на окно, только о самом высоком из пройденных порогов.</returns>
	public IReadOnlyList<QuotaAlert> Check(string provider, IReadOnlyList<QuotaWindow> windows, Thresholds levels)
	{
		var alerts = new List<QuotaAlert>();

		foreach (var window in windows.Where(window => window.Limiting))
		{
			var key = $"{provider}/{window.Kind}/{window.Label}";

			if (window.Used < levels.Important)
			{
				_fired.Remove(key);

				continue;
			}

			var high = window.Used >= levels.Critical;

			// Уже сказали то же или больше.
			if (_fired.TryGetValue(key, out var said) && (said || !high))
			{
				continue;
			}

			_fired[key] = high;
			alerts.Add(new(provider, window, high));
		}

		return alerts;
	}

	/// <summary>Самая серьёзная из новых тревог всех агентов: выше порога у двух разом бывает разве что при
	/// запуске — тост один, о главном. Пороги взводятся у всех, а не только у сказанного.</summary>
	public QuotaAlert? Check(IEnumerable<(string Provider, IReadOnlyList<QuotaWindow> Windows)> agents, Thresholds levels) =>
		agents
			.SelectMany(agent => Check(agent.Provider, agent.Windows, levels))
			.MaxBy(alert => (alert.Critical, alert.Window.Used));
}

internal static class Quota
{
	/// <summary>Окно, которое ограничивает сильнее всех: среди блокирующих всё, а если таких нет — среди всех.</summary>
	public static QuotaWindow? Bottleneck(IReadOnlyList<QuotaWindow> windows) =>
		(windows.Any(window => window.Limiting) ? windows.Where(window => window.Limiting) : windows)
			.MaxBy(window => window.Used);

	public static Health Rate(double used, Thresholds levels) =>
		used >= levels.Critical ? Health.Critical : used >= levels.Important ? Health.Warning : Health.Good;

	/// <summary>Сколько процентов было бы потрачено к этому моменту при ровном темпе; null — длина окна неизвестна
	/// или сброс уже прошёл.</summary>
	public static double? Expected(QuotaWindow window, DateTime now)
	{
		if (window.Length is not { } span || window.ResetsAt is not { } resets)
		{
			return null;
		}

		var left = resets.UtcDateTime - now;

		return left <= TimeSpan.Zero || left > span ? null : (1 - (left / span)) * 100;
	}

	/// <summary>Темп расхода: дальше пяти пунктов от ровного — уже не ровно.</summary>
	public static Pace Pace(double used, double expected) =>
		used > expected + 5 ? AgentVitals.Pace.Ahead : used < expected - 5 ? AgentVitals.Pace.Spare : AgentVitals.Pace.Even;

	/// <summary>Тариф для показа: «claude_max» → «Max», «team» → «Team».</summary>
	public static string PlanName(string? subscription) =>
		string.IsNullOrEmpty(subscription)
			? ""
			: CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
				subscription.Replace("claude_", "", StringComparison.OrdinalIgnoreCase).Replace('_', ' '));
}
