using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AgentVitals.Core.Usage;

namespace AgentVitals.Agents.Codex;

/// <summary>Страница цен OpenAI: из неё цены моделей Codex.</summary>
internal static class OpenAiPrices
{
	public const string Url = "https://developers.openai.com/api/docs/pricing.md";

	private static readonly Regex _note = new(@"\s*\(.*\)\s*$");

	/// <summary>
	/// Цены OpenAI: таблицы под подписью «Standard» (не Batch, Flex, Fast…). Колонки ищутся по названию; у флагманов —
	/// «короткий контекст»: окно Codex (258K) меньше порога длинного (272K). Нет цены кэша — он стоит как обычный вход;
	/// нет цены выхода — это не модель для Codex (эмбеддинги, модерация). Модель есть в нескольких таблицах — первая цена.
	/// </summary>
	public static PriceList Parse(string page)
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

				var cells = PriceList.Cells(line);

				if (header is null)
				{
					header = cells;
				}
				else if (cells.Any(cell => cell.Trim('-', ':', ' ').Length > 0) && Row(header, cells) is { } row)
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

	private static (string Model, Price Price)? Row(List<string> header, List<string> cells)
	{
		string? Cell(params string[] names) =>
			header.FindIndex(cell => names.Any(name => cell.Equals(name, StringComparison.OrdinalIgnoreCase))) is var column && column >= 0 && column < cells.Count
				? cells[column]
				: null;

		var model = Cell("Model");
		var input = PriceList.Money(Cell("Short context input", "Input"));
		var output = PriceList.Money(Cell("Short context output", "Output"));

		if (string.IsNullOrEmpty(model) || input is not { } plain || output is not { } spoken)
		{
			return null;
		}

		var cached = PriceList.Money(Cell("Short context cached input", "Cached input")) ?? plain;
		var written = PriceList.Money(Cell("Short context cache writes", "Cache writes")) ?? plain;

		return (_note.Replace(model, "").ToLowerInvariant(), new Price(plain, written, written, cached, spoken));
	}
}
