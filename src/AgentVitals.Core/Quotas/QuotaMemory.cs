using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentVitals.Platform;

namespace AgentVitals.Core.Quotas;

/// <summary>Последний ответ агента и до какого времени сервер велел ждать — то, что переживает перезапуск.</summary>
public sealed record RememberedQuota(Snapshot? Snapshot, DateTime? Updated, DateTime? RetryAt);

/// <summary>
/// quota.json рядом с настройками: последний ответ каждого агента. После запуска окно сразу показывает его,
/// а сервер спрашивают не раньше, чем пора, — перезапуск не тратит лимит запросов.
/// </summary>
public static class QuotaMemory
{
	public static string Location { get; } = Path.Combine(Paths.Data, "quota.json");

	private static readonly JsonSerializerOptions _format = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true,
		Converters =
		{
			new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
		},
	};

	public static IReadOnlyDictionary<string, RememberedQuota> Load(string file)
	{
		try
		{
			if (File.Exists(file))
			{
				return JsonSerializer.Deserialize<Dictionary<string, RememberedQuota>>(File.ReadAllText(file), _format) ?? [];
			}
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
		{
			// Без памяти программа работает как прежде: окно пусто до первого ответа.
			Journal.Add($"quota.json: {exception.Message}");
		}

		return new Dictionary<string, RememberedQuota>();
	}

	/// <summary>Записать через временный файл: оборванная запись не испортит прежний ответ.</summary>
	public static void Save(string file, IReadOnlyDictionary<string, RememberedQuota> memory)
	{
		var temp = file + ".tmp";

		try
		{
			File.WriteAllText(temp, JsonSerializer.Serialize(memory, _format));
			File.Move(temp, file, overwrite: true);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			Journal.Add($"quota.json: {exception.Message}");
		}
	}
}
