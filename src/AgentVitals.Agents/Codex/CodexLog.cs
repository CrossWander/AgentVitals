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
using AgentVitals.Core.Usage;
using AgentVitals.Platform;

namespace AgentVitals.Agents.Codex;

/// <summary>
/// Расход Codex по его журналам в ~/.codex/sessions и archived_sessions. Из строк берутся только числа ответов
/// модели и имя модели хода — переписка не хранится. Цены — со страницы OpenAI (PriceSource), копия в папке данных.
/// </summary>
internal sealed class CodexLog(string home, string pricesFile) : IUsageLog
{
	// Расход и модель хода — в записях с turn_id; реплики и вывод инструментов без него разбирать незачем.
	private static readonly byte[] _marker = Encoding.UTF8.GetBytes("\"turn_id\"");

	// 30 дней графика и запас на часовой пояс.
	private static readonly TimeSpan _keep = TimeSpan.FromDays(31);

	private static readonly EnumerationOptions _everywhere = new()
	{
		RecurseSubdirectories = true,
		IgnoreInaccessible = true,
	};

	private readonly LogTail _tail = new();
	private readonly PriceSource _prices = new(OpenAiPrices.Url, pricesFile, OpenAiPrices.Parse);

	// Ответ — по response_id: запись на ответ одна, а повторное чтение файла ничего не удваивает.
	private readonly Dictionary<string, UsageRecord> _records = [];

	// Модель хода из turn_context: запись о расходе её не называет и бывает прочитана в следующий раз.
	// ponytail: ходы не забываются, пока программа работает, — десятки байт на ход; чистить, если это станет заметно.
	private readonly Dictionary<string, string> _models = [];

	private string? _lastError;

	public CodexLog()
		: this(CodexProvider.Home, Path.Combine(Paths.Data, "prices-openai.md"))
	{
	}

	internal IReadOnlyCollection<UsageRecord> Records => _records.Values;

	/// <inheritdoc/>
	public async Task<IReadOnlyList<UsageDay>> ReadAsync(HttpClient http, DateTime now, CancellationToken cancellation)
	{
		var prices = await _prices.GetAsync(http, now, cancellation);
		await Task.Run(() => Scan(now), cancellation);

		var zone = TimeZoneInfo.Local;

		return UsageDays.Build(_records.Values, prices, DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, zone)), zone);
	}

	/// <summary>Дочитать изменившиеся журналы за 31 день и выбросить записи старше.</summary>
	internal void Scan(DateTime now)
	{
		var since = now - _keep;
		List<string> files =
		[
			.. new[] { "sessions", "archived_sessions" }
				.Select(folder => Path.Combine(home, folder))
				.Where(Directory.Exists)
				.SelectMany(folder => Directory.EnumerateFiles(folder, "*.jsonl", _everywhere))
				.Where(file => File.GetLastWriteTimeUtc(file) >= since),
		];

		_tail.Keep(files);

		foreach (var file in files)
		{
			try
			{
				foreach (var line in _tail.ReadNew(file, _marker))
				{
					Read(line);
				}
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				// Файл занят или его как раз перенесли в архив — попробуем на следующем тике.
				Report(exception.GetType().Name, $"{Path.GetFileName(file)}: {exception.Message}");
			}
		}

		foreach (var old in _records.Where(pair => pair.Value.Utc < since).Select(pair => pair.Key).ToList())
		{
			_records.Remove(old);
		}
	}

	/// <summary>Строка журнала: модель хода запоминается, ответ модели становится записью.</summary>
	private void Read(string line)
	{
		try
		{
			var entry = JsonNode.Parse(line);
			var payload = entry?["payload"];

			if ((string?)entry?["type"] == "turn_context")
			{
				if ((string?)payload?["turn_id"] is { } turn && (string?)payload["model"] is { Length: > 0 } model)
				{
					_models[turn] = model;
				}
			}
			else if (Parse(entry, _models) is { } record)
			{
				_records[record.Id] = record;
			}
		}
		catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
		{
			// оборванная или чужая строка — пропускаем
		}
	}

	/// <summary>Ответ модели из записи token_usage_record; не она, без response_id, времени или токенов — null.</summary>
	internal static UsageRecord? Parse(JsonNode? entry, IReadOnlyDictionary<string, string> models)
	{
		var payload = entry?["payload"];
		var usage = payload?["usage"];

		if ((string?)entry?["type"] != "token_usage_record"
			|| (string?)payload?["response_id"] is not { Length: > 0 } id
			|| usage is null
			|| !DateTime.TryParse((string?)entry?["timestamp"], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var utc))
		{
			return null;
		}

		// Прочитанное из кэша и записанное в кэш входят в input_tokens, рассуждения — в output_tokens.
		var cached = Count(usage["cached_input_tokens"]);
		var written = Count(usage["cache_write_input_tokens"]);
		var record = new UsageRecord(
			id,
			utc,
			models.GetValueOrDefault((string?)payload?["turn_id"] ?? "", ""),
			Math.Max(0, Count(usage["input_tokens"]) - cached - written),
			Count(usage["output_tokens"]),
			written,
			0,
			cached);

		return record.Total == 0 ? null : record;
	}

	private static long Count(JsonNode? node) => node is JsonValue value && value.TryGetValue<long>(out var count) ? count : 0;

	/// <summary>В журнал — при смене вида беды, а не каждую минуту одно и то же.</summary>
	private void Report(string kind, string message)
	{
		if (_lastError != kind)
		{
			Journal.Add($"codex usage: {message}");
			_lastError = kind;
		}
	}
}
