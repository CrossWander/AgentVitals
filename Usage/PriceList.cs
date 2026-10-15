using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace AgentVitals;

/// <summary>Цена модели, $ за миллион токенов.</summary>
internal sealed record Price(decimal Input, decimal CacheWrite, decimal CacheWrite1h, decimal CacheRead, decimal Output);

/// <summary>Цены моделей по таблицам со страниц цен: Anthropic (<c>Parse</c>) и OpenAI (<c>ParseOpenAi</c>).</summary>
internal sealed class PriceList(IReadOnlyDictionary<string, Price> prices)
{
	public const string Url = "https://platform.claude.com/docs/en/about-claude/pricing.md";

	public const string OpenAiUrl = "https://developers.openai.com/api/docs/pricing.md";

	private static readonly Regex _note = new(@"\s*\(.*\)\s*$");

	private static readonly Regex _link = new(@"\s*\(\[[^\]]*\]\([^)]*\)\)");
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

	/// <summary>Первая таблица после «## Model pricing». Колонки ищутся по названию; не разобралось — пусто.</summary>
	public static PriceList Parse(string page)
	{
		Dictionary<string, Price> found = [];
		var lines = page.Split('\n').Select(line => line.Trim()).ToList();
		var start = lines.FindIndex(line => line.StartsWith("## Model pricing", StringComparison.OrdinalIgnoreCase));

		if (start < 0)
		{
			return new(found);
		}

		var table = lines.Skip(start + 1).SkipWhile(line => !line.StartsWith('|')).TakeWhile(line => line.StartsWith('|')).ToList();

		if (table.Count < 3)
		{
			return new(found);
		}

		var header = Cells(table[0]);
		int Column(string name) => header.FindIndex(cell => cell.Contains(name, StringComparison.OrdinalIgnoreCase));
		int[] columns = [Column("Model"), Column("Base input"), Column("5m cache"), Column("1h cache"), Column("Cache hits"), Column("Output")];

		if (columns.Any(column => column < 0))
		{
			return new(found);
		}

		// Первая строка после заголовка — разделитель «| --- |».
		foreach (var row in table.Skip(2).Select(Cells).Where(cells => cells.Count == header.Count))
		{
			var money = columns[1..].Select(column => Money(row[column])).ToList();

			if (money.All(value => value is not null))
			{
				found[Id(row[columns[0]])] = new(money[0]!.Value, money[1]!.Value, money[2]!.Value, money[3]!.Value, money[4]!.Value);
			}
		}

		return new(found);
	}

	/// <summary>
	/// Цены OpenAI: таблицы под подписью «Standard» (не Batch, Flex, Fast…). Колонки ищутся по названию; у флагманов —
	/// «короткий контекст»: окно Codex (258K) меньше порога длинного (272K). Нет цены кэша — он стоит как обычный вход;
	/// нет цены выхода — это не модель для Codex (эмбеддинги, модерация). Модель есть в нескольких таблицах — первая цена.
	/// </summary>
	public static PriceList ParseOpenAi(string page)
	{
		Dictionary<string, Price> found = [];
		string? label = null;
		var standard = false;
		List<string>? header = null;

		foreach (var line in page.Split('\n').Select(line => line.Trim()))
		{
			if (line.StartsWith('#'))
			{
				standard = label == "Standard";
				header = null;
			}
			else if (line.StartsWith('|'))
			{
				if (!standard)
				{
					continue;
				}

				var cells = Cells(line);

				if (header is null)
				{
					header = cells;
				}
				else if (cells.Any(cell => cell.Trim('-', ':', ' ').Length > 0) && OpenAiRow(header, cells) is { } row)
				{
					found.TryAdd(row.Model, row.Price);
				}
			}
			else if (line.Length > 0)
			{
				label = line;
			}
		}

		return new(found);
	}

	private static (string Model, Price Price)? OpenAiRow(List<string> header, List<string> cells)
	{
		string? Cell(params string[] names) =>
			header.FindIndex(cell => names.Any(name => cell.Equals(name, StringComparison.OrdinalIgnoreCase))) is var column && column >= 0 && column < cells.Count
				? cells[column]
				: null;

		var model = Cell("Model");
		var input = Money(Cell("Short context input", "Input"));
		var output = Money(Cell("Short context output", "Output"));

		if (string.IsNullOrEmpty(model) || input is not { } plain || output is not { } spoken)
		{
			return null;
		}

		var cached = Money(Cell("Short context cached input", "Cached input")) ?? plain;
		var written = Money(Cell("Short context cache writes", "Cache writes")) ?? plain;

		return (_note.Replace(model, "").ToLowerInvariant(), new Price(plain, written, written, cached, spoken));
	}

	/// <summary>«Claude Opus 5.5 ([ссылка](…))» → «claude-opus-5-5».</summary>
	private static string Id(string name) =>
		_footnote.Replace(_link.Replace(name, ""), "").Trim().ToLowerInvariant().Replace(' ', '-').Replace('.', '-');

	private static decimal? Money(string? cell) =>
		cell is not null && _money.Match(_footnote.Replace(cell, "")) is { Success: true } match
			? decimal.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
			: null;

	private static List<string> Cells(string row) => [.. row.Trim('|').Split('|').Select(cell => cell.Trim())];
}
