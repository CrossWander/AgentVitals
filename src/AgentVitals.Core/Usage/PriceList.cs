using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace AgentVitals.Core.Usage;

/// <summary>Цена модели, $ за миллион токенов.</summary>
public sealed record Price(decimal Input, decimal CacheWrite, decimal CacheWrite1h, decimal CacheRead, decimal Output);

/// <summary>Цены моделей по имени. Страницу цен разбирает агент, чья она: ClaudePrices, OpenAiPrices.</summary>
public sealed class PriceList(IReadOnlyDictionary<string, Price> prices)
{
	private static readonly Regex _footnote = new("<sup>.*?</sup>");
	private static readonly Regex _money = new(@"\$([0-9]+(?:\.[0-9]+)?)");
	private static readonly Regex _dated = new(@"^(.+)-\d{8}$");

	public int Count => prices.Count;

	/// <summary>Цена модели из лога: точное имя или имя с датой выпуска. Модели нет — null, а не цена похожей.</summary>
	public Price? Find(string model) =>
		prices.TryGetValue(model, out var price) ? price
		: _dated.Match(model) is { Success: true } dated && prices.TryGetValue(dated.Groups[1].Value, out price) ? price
		: null;

	/// <summary>Стоимость ответа: запись в кэш на час дороже, чем на 5 минут.</summary>
	public static decimal Cost(UsageRecord record, Price price) =>
		(record.Input * price.Input
			+ (record.CacheWrite - record.CacheWrite1h) * price.CacheWrite
			+ record.CacheWrite1h * price.CacheWrite1h
			+ record.CacheRead * price.CacheRead
			+ record.Output * price.Output) / 1_000_000m;

	/// <summary>Сколько сэкономил кэш: прочитанное из него стоило бы как обычный вход.</summary>
	public static decimal Saved(UsageRecord record, Price price) => record.CacheRead * (price.Input - price.CacheRead) / 1_000_000m;

	/// <summary>Сумма в долларах из ячейки таблицы цен, без сносок; нет — null.</summary>
	public static decimal? Money(string? cell) =>
		cell is not null && _money.Match(_footnote.Replace(cell, "")) is { Success: true } match
			? decimal.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
			: null;

	/// <summary>Ячейки строки таблицы markdown.</summary>
	public static List<string> Cells(string row) => [.. row.Trim('|').Split('|').Select(cell => cell.Trim())];
}
