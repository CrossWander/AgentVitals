using System;
using System.Collections.Generic;
using System.Linq;
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
/// <remarks>Saved — сколько сэкономил кэш, Models — стоимость по семействам моделей; без цен оба null.</remarks>
internal sealed record UsageDay(
	DateOnly Date,
	long Input,
	long Output,
	long CacheWrite,
	long CacheRead,
	decimal? Cost,
	bool Unpriced,
	TimeSpan Working,
	decimal? Saved = null,
	ModelCosts? Models = null)
{
	public long Total => Input + Output + CacheWrite + CacheRead;
}

/// <summary>Семейство модели — слой столбика на графике «по моделям».</summary>
public enum ModelFamily
{
	Opus,
	Sonnet,
	Haiku,
	Fable,
	Other,
}

/// <summary>Стоимость дня по семействам моделей. Запись, а не словарь: дни сравниваются по значению.</summary>
internal sealed record ModelCosts(decimal Opus, decimal Sonnet, decimal Haiku, decimal Fable, decimal Other)
{
	public static readonly ModelCosts None = new(0, 0, 0, 0, 0);

	/// <summary>Слои в порядке ModelFamily.</summary>
	public IReadOnlyList<decimal> All => [Opus, Sonnet, Haiku, Fable, Other];

	/// <summary>Семейство по имени модели: «claude-opus-5-5[1m]» — Opus, «fable» — Fable.</summary>
	public static ModelFamily Family(string model) =>
		Enum.GetValues<ModelFamily>().FirstOrDefault(
			family => family != ModelFamily.Other && model.Contains(family.ToString(), StringComparison.OrdinalIgnoreCase),
			ModelFamily.Other);

	public ModelCosts Add(ModelFamily family, decimal cost) => family switch
	{
		ModelFamily.Opus => this with { Opus = Opus + cost },
		ModelFamily.Sonnet => this with { Sonnet = Sonnet + cost },
		ModelFamily.Haiku => this with { Haiku = Haiku + cost },
		ModelFamily.Fable => this with { Fable = Fable + cost },
		_ => this with { Other = Other + cost },
	};
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
