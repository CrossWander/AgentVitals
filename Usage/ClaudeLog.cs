using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace AgentVitals;

/// <summary>
/// Расход Claude Code по его логам в ~/.claude/projects, с субагентами. Из строк берутся только числа
/// ответов модели — переписка не хранится. Цены — со страницы Anthropic (PriceSource), копия в папке данных.
/// </summary>
internal sealed class ClaudeLog(string projects, string pricesFile) : IUsageLog
{
	// Строки без этого слова — вложения и реплики человека: их незачем разбирать как JSON.
	private static readonly byte[] _marker = Encoding.UTF8.GetBytes("\"assistant\"");

	// 30 дней графика и запас на часовой пояс.
	private static readonly TimeSpan _keep = TimeSpan.FromDays(31);

	private static readonly EnumerationOptions _everywhere = new()
	{
		RecurseSubdirectories = true,
		IgnoreInaccessible = true,
	};

	private readonly LogTail _tail = new();
	private readonly PriceSource _prices = new(PriceList.Url, pricesFile, PriceList.Parse);

	// Пока идёт ответ, Claude Code пишет его несколько раз с одним id: последняя копия — полная.
	// По этой же причине повторное чтение файла ничего не удваивает.
	private readonly Dictionary<string, UsageRecord> _records = [];

	private string? _lastError;

	public ClaudeLog()
		: this(Path.Combine(Paths.Home, ".claude", "projects"), Path.Combine(Paths.Data, "prices.md"))
	{
	}

	internal IReadOnlyCollection<UsageRecord> Records => _records.Values;

	/// <inheritdoc/>
	public async Task<IReadOnlyList<UsageDay>> ReadAsync(HttpClient http, DateTime now, CancellationToken cancellation)
	{
		var prices = await _prices.GetAsync(http, now, cancellation);

		// ponytail: после запуска логи за месяц читаются заново (здесь ~390 МБ, секунды); кэш прошедших
		// дней на диске — если первое чтение станет заметно долгим.
		await Task.Run(() => Scan(now), cancellation);

		var zone = TimeZoneInfo.Local;

		return UsageDays.Build(_records.Values, prices, DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, zone)), zone);
	}

	/// <summary>Дочитать изменившиеся логи за 31 день и выбросить записи старше.</summary>
	internal void Scan(DateTime now)
	{
		var since = now - _keep;
		List<string> files = Directory.Exists(projects)
			? [.. Directory.EnumerateFiles(projects, "*.jsonl", _everywhere).Where(file => File.GetLastWriteTimeUtc(file) >= since)]
			: [];

		_tail.Keep(files);

		foreach (var file in files)
		{
			try
			{
				foreach (var line in _tail.ReadNew(file, _marker))
				{
					if (Parse(line) is { } record)
					{
						_records[record.Id] = record;
					}
				}
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				// Файл занят или пропал — попробуем на следующем тике.
				Report(exception.GetType().Name, $"{Path.GetFileName(file)}: {exception.Message}");
			}
		}

		foreach (var old in _records.Where(pair => pair.Value.Utc < since).Select(pair => pair.Key).ToList())
		{
			_records.Remove(old);
		}
	}

	/// <summary>Ответ модели из строки лога; не ответ, без id, времени или токенов — null.</summary>
	internal static UsageRecord? Parse(string line)
	{
		try
		{
			var entry = JsonNode.Parse(line);
			var message = entry?["message"];
			var usage = message?["usage"];

			if ((string?)entry?["type"] != "assistant"
				|| (string?)message?["id"] is not { Length: > 0 } id
				|| usage is null
				|| !DateTime.TryParse((string?)entry?["timestamp"], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var utc))
			{
				return null;
			}

			var record = new UsageRecord(
				id,
				utc,
				(string?)message?["model"] ?? "",
				Count(usage["input_tokens"]),
				Count(usage["output_tokens"]),
				Count(usage["cache_creation_input_tokens"]),
				Count(usage["cache_creation"]?["ephemeral_1h_input_tokens"]),
				Count(usage["cache_read_input_tokens"]));

			// <synthetic> — служебные строки Claude Code: токенов в них нет.
			return record.Total == 0 ? null : record;
		}
		catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
		{
			return null;
		}
	}

	private static long Count(JsonNode? node) => node is JsonValue value && value.TryGetValue<long>(out var count) ? count : 0;

	/// <summary>В журнал — при смене вида беды, а не каждую минуту одно и то же.</summary>
	private void Report(string kind, string message)
	{
		if (_lastError != kind)
		{
			Journal.Add($"usage: {message}");
			_lastError = kind;
		}
	}
}
