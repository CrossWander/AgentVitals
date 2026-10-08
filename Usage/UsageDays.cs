using System;
using System.Collections.Generic;
using System.Linq;

namespace AgentVitals;

/// <summary>Записи ответов → 30 местных дней: токены, стоимость, время работы.</summary>
internal static class UsageDays
{
	public const int Days = 30;

	// Дольше получаса без ответов модели — человек ушёл, а не ждёт.
	private static readonly TimeSpan _pause = TimeSpan.FromMinutes(30);

	/// <summary>Дни от старого к сегодняшнему, пустые — тоже.</summary>
	/// <param name="records">Ответы без повторов, время в UTC.</param>
	/// <param name="prices">Цены; null — стоимость неизвестна.</param>
	/// <param name="today">Сегодня по местному времени.</param>
	/// <param name="zone">Часовой пояс, в котором считаются дни.</param>
	public static IReadOnlyList<UsageDay> Build(IEnumerable<UsageRecord> records, PriceList? prices, DateOnly today, TimeZoneInfo zone)
	{
		var first = today.AddDays(1 - Days);
		var byDay = records
			.Select(record => (Day: DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(record.Utc, zone)), Record: record))
			.Where(item => item.Day >= first && item.Day <= today)
			.ToLookup(item => item.Day, item => item.Record);

		return [.. Enumerable.Range(0, Days).Select(offset => first.AddDays(offset)).Select(date => Day(date, [.. byDay[date]], prices))];
	}

	/// <summary>Сумма отрезков работы: перерыв дольше получаса начинает новый отрезок.</summary>
	internal static TimeSpan Working(IEnumerable<DateTime> times)
	{
		var sorted = times.Order().ToList();
		var total = TimeSpan.Zero;

		for (var index = 1; index < sorted.Count; index++)
		{
			var gap = sorted[index] - sorted[index - 1];

			if (gap <= _pause)
			{
				total += gap;
			}
		}

		return total;
	}

	private static UsageDay Day(DateOnly date, List<UsageRecord> records, PriceList? prices)
	{
		decimal? cost = prices is null ? null : 0m;
		decimal? saved = prices is null ? null : 0m;
		var models = prices is null ? null : ModelCosts.None;
		var unpriced = false;

		foreach (var record in records)
		{
			if (prices?.Find(record.Model) is { } price)
			{
				var each = PriceList.Cost(record, price);
				cost += each;
				saved += PriceList.Saved(record, price);
				models = models!.Add(ModelCosts.Family(record.Model), each);
			}
			else if (prices is not null)
			{
				unpriced = true;
			}
		}

		return new(
			date,
			records.Sum(record => record.Input),
			records.Sum(record => record.Output),
			records.Sum(record => record.CacheWrite),
			records.Sum(record => record.CacheRead),
			cost,
			unpriced,
			Working(records.Select(record => record.Utc)),
			saved,
			models);
	}
}
