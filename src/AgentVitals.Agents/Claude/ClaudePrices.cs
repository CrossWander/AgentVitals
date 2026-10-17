using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AgentVitals.Core.Usage;

namespace AgentVitals.Agents.Claude;

/// <summary>Страница цен Anthropic: из неё цены моделей Claude.</summary>
internal static class ClaudePrices
{
	public const string Url = "https://platform.claude.com/docs/en/about-claude/pricing.md";

	private static readonly Regex _link = new(@"\s*\(\[[^\]]*\]\([^)]*\)\)");
	private static readonly Regex _footnote = new("<sup>.*?</sup>");

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

		var header = PriceList.Cells(table[0]);
		int Column(string name) => header.FindIndex(cell => cell.Contains(name, StringComparison.OrdinalIgnoreCase));
		int[] columns = [Column("Model"), Column("Base input"), Column("5m cache"), Column("1h cache"), Column("Cache hits"), Column("Output")];

		if (columns.Any(column => column < 0))
		{
			return new(found);
		}

		// Первая строка после заголовка — разделитель «| --- |».
		foreach (var row in table.Skip(2).Select(PriceList.Cells).Where(cells => cells.Count == header.Count))
		{
			var money = columns[1..].Select(column => PriceList.Money(row[column])).ToList();

			if (money.All(value => value is not null))
			{
				found[Id(row[columns[0]])] = new(money[0]!.Value, money[1]!.Value, money[2]!.Value, money[3]!.Value, money[4]!.Value);
			}
		}

		return new(found);
	}

	/// <summary>«Claude Opus 5.5 ([ссылка](…))» → «claude-opus-5-5».</summary>
	private static string Id(string name) =>
		_footnote.Replace(_link.Replace(name, ""), "").Trim().ToLowerInvariant().Replace(' ', '-').Replace('.', '-');
}
