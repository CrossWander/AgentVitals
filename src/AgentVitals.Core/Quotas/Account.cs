using System;
using System.Collections.Generic;
using System.Linq;

namespace AgentVitals.Core.Quotas;

/// <summary>Можно ли работать запомненным аккаунтом — по его последним цифрам на этот момент.</summary>
public enum Availability
{
	/// <summary>Ни одно ограничивающее окно не было израсходовано.</summary>
	Free,

	/// <summary>Какое-то ограничивающее окно израсходовано и ещё не сбросилось.</summary>
	Busy,

	/// <summary>При последнем виде был занят, а с тех пор все израсходованные окна сбросились.</summary>
	Back,
}

/// <summary>Аккаунт агента и последнее, что о нём известно: снимок, когда он пришёл и до какого времени сервер велел ждать.</summary>
/// <param name="Key">Опознание аккаунта у агента; "" — агент не сообщает, кто вошёл.</param>
public sealed record Account(string Key, Snapshot? Snapshot, DateTime? Updated, DateTime? RetryAt)
{
	/// <summary>Окна снимка на этот момент: у прошедших сброс — 0%.</summary>
	public IReadOnlyList<QuotaWindow> Windows(DateTime now) => [.. Snapshot?.Windows.Select(window => window.At(now)) ?? []];

	/// <summary>Свободен, занят или снова доступен. Занят — до самого позднего сброса среди израсходованных
	/// ограничивающих окон: сессия и неделя по 100% — до сброса недели.</summary>
	/// <returns>Состояние и окно, которое сбросится последним (только у занятого); сброс неизвестен — позже всех.</returns>
	public (Availability State, QuotaWindow? Blocker) Availability(DateTime now)
	{
		// Как на карточке: 99,6% — уже «исчерпано».
		List<QuotaWindow> spent = [.. Snapshot?.Windows.Where(window => window.Limiting && Math.Round(window.Used) >= 100) ?? []];
		var blocking = spent.Where(window => window.ResetsAt is not { } at || at.UtcDateTime > now).ToList();

		return blocking.Count > 0 ? (Quotas.Availability.Busy, blocking.MaxBy(window => window.ResetsAt ?? DateTimeOffset.MaxValue))
			: spent.Count > 0 ? (Quotas.Availability.Back, null)
			: (Quotas.Availability.Free, null);
	}
}
