using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AgentVitals;

/// <summary>Один ответ модели из лога агента: сколько токенов и какой моделью.</summary>
internal sealed record UsageRecord(
	string Id,
	DateTime Utc,
	string Model,
	long Input,
	long Output,
	long CacheWrite,
	long CacheWrite1h,
	long CacheRead)
{
	public long Total => Input + Output + CacheWrite + CacheRead;
}

/// <summary>Расход за местный день. Cost — null, если цен нет вовсе; Unpriced — часть моделей без цены.</summary>
internal sealed record UsageDay(
	DateOnly Date,
	long Input,
	long Output,
	long CacheWrite,
	long CacheRead,
	decimal? Cost,
	bool Unpriced,
	TimeSpan Working)
{
	public long Total => Input + Output + CacheWrite + CacheRead;
}

/// <summary>Локальные логи агента: из них — токены, стоимость и время работы по дням.</summary>
internal interface IUsageLog
{
	/// <summary>Дочитать логи (и при необходимости цены) и вернуть 30 дней, от старого к сегодняшнему.</summary>
	/// <param name="http">Общий HTTP-клиент опроса — для страницы цен.</param>
	/// <param name="now">Текущее время, UTC.</param>
	/// <param name="cancellation">Отмена.</param>
	/// <returns>30 дней, пустые — тоже.</returns>
	Task<IReadOnlyList<UsageDay>> ReadAsync(HttpClient http, DateTime now, CancellationToken cancellation);
}
