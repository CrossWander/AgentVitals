using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgentVitals.Platform;

namespace AgentVitals.Agents.Claude;

/// <summary>Журнал сессии Claude Code: в нём видно прерывание, о котором hooks молчат.</summary>
internal static class ClaudeTranscript
{
	private const string _interrupted = "[Request interrupted by user";
	private const int _tail = 8192;

	/// <summary>
	/// Ход прерван клавишей Esc после последнего события hook. Stop в этом случае не приходит, зато в журнал
	/// сессии последней репликой ложится отметка прерывания; «…for tool use» — Esc на вопросе о разрешении.
	/// </summary>
	internal static bool Interrupted(string? transcript, DateTime since)
	{
		if (string.IsNullOrEmpty(transcript))
		{
			return false;
		}

		try
		{
			// Хватает хвоста: отметка короткая и стоит в самом конце, за ней — только служебные записи.
			using var stream = SharedFile.Open(transcript);
			var start = Math.Max(0, stream.Length - _tail);
			var buffer = new byte[stream.Length - start];
			stream.Position = start;
			stream.ReadExactly(buffer);
			var lines = Encoding.UTF8.GetString(buffer).Split('\n');

			// Первая строка хвоста обрезана, если читали не с начала файла.
			for (var index = lines.Length - 1; index >= (start > 0 ? 1 : 0); index--)
			{
				var entry = Parse(lines[index]) as JsonObject;
				var type = (string?)entry?["type"];

				if (entry is null || type is not ("user" or "assistant"))
				{
					continue;
				}

				return type == "user"
					&& entry["message"]?["content"] is JsonArray content
					&& content.OfType<JsonObject>().Any(item => ((string?)item["text"])?.StartsWith(_interrupted, StringComparison.Ordinal) == true)
					&& DateTimeOffset.TryParse((string?)entry["timestamp"], CultureInfo.InvariantCulture, out var at)
					&& at.UtcDateTime >= since;
			}
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
		{
			// нет журнала или он не того вида — остаётся то, что сказали hooks
		}

		return false;
	}

	private static JsonNode? Parse(string line)
	{
		try
		{
			return JsonNode.Parse(line);
		}
		catch (JsonException)
		{
			return null;
		}
	}
}
