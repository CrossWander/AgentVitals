using System;
using System.Collections.Generic;
using System.Linq;

namespace AgentVitals.Core.Quotas;

/// <summary>Пороги «важно» и «критично», %: с них и тосты, и жёлтый с красным — что красное, о том и тост.</summary>
public sealed record Thresholds(int Important, int Critical);

/// <summary>О чём тост квоты, по возрастанию важности: окно сбросилось, при таком темпе не хватит до сброса, пороги.</summary>
public enum QuotaLevel
{
	Reset,
	Pace,
	Important,
	Critical,
}

/// <summary>Окно агента дошло до порога, обгоняет темп или сбросилось.</summary>
public sealed record QuotaAlert(string Agent, QuotaWindow Window, QuotaLevel Level)
{
	public bool Critical => Level == QuotaLevel.Critical;
}

/// <summary>
/// Тосты по сессии и неделе: «важно» и «критично» (по умолчанию 75% и 90%, меняются в настройках), каждый раз
/// за цикл окна. Ниже порога «важно» — после сброса — пороги взводятся снова. Ещё до «важно» — один тост, если
/// при таком темпе окна не хватит до сброса; после «критично» — тост, когда окно сбросилось.
/// </summary>
public sealed class QuotaAlarm
{
	private const int _step = 5;
	private const int _lowest = 50;
	private const int _highest = 95;

	// Окно → уже сказали «критично» (true) или только «важно» (false).
	private readonly Dictionary<string, bool> _fired = [];

	// Окна, о темпе которых в этом цикле уже сказали.
	private readonly HashSet<string> _paced = [];

	/// <summary>Сдвинуть порог на шаг: от 50 до 95, «важно» всегда хотя бы на шаг ниже «критично».</summary>
	public static Thresholds Shift(Thresholds levels, bool moveCritical, int delta) => moveCritical
		? levels with { Critical = Math.Clamp(levels.Critical + delta, Math.Max(_lowest, levels.Important + _step), _highest) }
		: levels with { Important = Math.Clamp(levels.Important + delta, _lowest, Math.Min(_highest, levels.Critical - _step)) };

	/// <summary>Окна агента, которые только что дошли до следующего порога. Уже высокое при запуске — тоже:
	/// запуск утром или после обновления — как раз тот момент, когда об этом важно знать.</summary>
	/// <param name="agent">Имя агента: у каждого свои окна.</param>
	/// <param name="windows">Окна из свежего снимка.</param>
	/// <param name="levels">Пороги из настроек.</param>
	/// <param name="now">От него темп: сколько окна уже прошло.</param>
	/// <returns>По тревоге на окно, только о самом высоком из пройденных порогов.</returns>
	public IReadOnlyList<QuotaAlert> Check(string agent, IReadOnlyList<QuotaWindow> windows, Thresholds levels, DateTime now)
	{
		var alerts = new List<QuotaAlert>();

		foreach (var window in windows.Where(window => window.Limiting))
		{
			var key = $"{agent}/{window.Kind}/{window.Label}";

			if (window.Used < levels.Important)
			{
				// Расход внутри окна только растёт: ниже 50% после «критично» опускает лишь сброс, а не поднятый порог.
				if (_fired.Remove(key, out var critical) && critical && window.Used < _lowest)
				{
					alerts.Add(new(agent, window, QuotaLevel.Reset));
				}

				// Темп — с половины окна: в начале он скачет, а 30% за первые полчаса — ещё не повод.
				if (window.Used < _lowest)
				{
					_paced.Remove(key);
				}
				else if (Quota.Runout(window, now) is not null && _paced.Add(key))
				{
					alerts.Add(new(agent, window, QuotaLevel.Pace));
				}

				continue;
			}

			var high = window.Used >= levels.Critical;

			// Уже сказали то же или больше.
			if (_fired.TryGetValue(key, out var said) && (said || !high))
			{
				continue;
			}

			_fired[key] = high;
			alerts.Add(new(agent, window, high ? QuotaLevel.Critical : QuotaLevel.Important));
		}

		return alerts;
	}

	/// <summary>Звук — только к «критично», когда он включён: осталось мало. Остальные тосты о квоте тихие.</summary>
	public static bool Knocks(QuotaAlert alert, bool enabled) => enabled && alert.Level == QuotaLevel.Critical;

	/// <summary>Самая серьёзная из новых тревог всех агентов: выше порога у двух разом бывает разве что при
	/// запуске — тост один, о главном. Пороги взводятся у всех, а не только у сказанного.</summary>
	public QuotaAlert? Check(IEnumerable<(string Agent, IReadOnlyList<QuotaWindow> Windows)> agents, Thresholds levels, DateTime now) =>
		agents
			.SelectMany(agent => Check(agent.Agent, agent.Windows, levels, now))
			.MaxBy(alert => (alert.Level, alert.Window.Used));
}
