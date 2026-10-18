using System;
using System.Collections.Generic;
using System.Linq;

namespace AgentVitals.Core.Quotas;

/// <summary>Запомненный аккаунт агента снова доступен: можно переключиться на него.</summary>
public sealed record AccountBack(string Agent, string Key);

/// <summary>
/// Тост «снова доступен» — о запомненных аккаунтах, когда сбросилось последнее израсходованное ограничивающее окно.
/// Только о том, что случилось, пока программа работает: сброшенное до запуска видно по плашке аккаунта.
/// Об аккаунте в работе говорят тосты квоты.
/// </summary>
public sealed class AccountAlarm
{
	// «Агент/ключ» → как выглядел запомненный аккаунт на прошлой проверке.
	private readonly Dictionary<string, Availability> _seen = [];

	/// <param name="agents">Агенты с вкладкой: аккаунт в работе и все аккаунты.</param>
	/// <param name="now">На этот момент: прошедший сброс — уже не занят.</param>
	/// <returns>Первый аккаунт, который только что освободился: два разом — редкость, второй видно по плашке.</returns>
	public AccountBack? Check(IEnumerable<(string Agent, string? InUse, IReadOnlyList<Account> Accounts)> agents, DateTime now)
	{
		AccountBack? back = null;

		foreach (var (agent, inUse, accounts) in agents)
		{
			foreach (var account in accounts)
			{
				var key = $"{agent}/{account.Key}";

				if (account.Key == inUse)
				{
					_seen.Remove(key);

					continue;
				}

				var state = account.Availability(now).State;

				if (_seen.TryGetValue(key, out var was) && was == Availability.Busy && state != Availability.Busy)
				{
					back ??= new(agent, account.Key);
				}

				_seen[key] = state;
			}
		}

		return back;
	}
}
