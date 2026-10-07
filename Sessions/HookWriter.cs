using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AgentVitals;

/// <summary>Режим hook: событие Claude Code из stdin превращается в файл состояния сессии.</summary>
internal static class HookWriter
{
	internal static readonly JsonSerializerOptions Json = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		Converters =
		{
			new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
		},
	};

	/// <summary>Прочитать событие и записать состояние; не бросает никогда.</summary>
	public static void Run(Stream input, string directory, long at)
	{
		try
		{
			using var reader = new StreamReader(input, Encoding.UTF8);
			Apply(JsonNode.Parse(reader.ReadToEnd()), directory, at);
		}
		catch (Exception)
		{
			// Сбой hook не должен доходить до Claude Code: ненулевой код или вывод он покажет
			// посреди работы, а сессия без статуса в трее — не повод человека отвлекать.
		}
	}

	internal static void Apply(JsonNode? payload, string directory, long at)
	{
		if (payload is null
			|| (string?)payload["session_id"] is not { Length: > 0 } id
			|| id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
			|| id.Contains("..", StringComparison.Ordinal))
		{
			return;
		}

		var file = Path.Combine(directory, id + ".json");
		var hookEvent = (string?)payload["hook_event_name"];

		if (hookEvent == "SessionEnd")
		{
			File.Delete(file);

			return;
		}

		if (Map(hookEvent, (string?)payload["notification_type"]) is not { } state)
		{
			return;
		}

		// Async-hooks финишируют не по порядку: событие старше записанного уже устарело.
		// ponytail: между чтением и записью окно в доли миллисекунды; два hook одной сессии в него на практике не попадают.
		if (Read(file)?.At > at)
		{
			return;
		}

		var tool = state == SessionState.Working ? (string?)payload["tool_name"] : null;
		var session = new Session(state, tool, (string?)payload["cwd"] ?? "", at);

		Directory.CreateDirectory(directory);
		var temp = $"{file}.{Environment.ProcessId}.tmp";
		File.WriteAllText(temp, JsonSerializer.Serialize(session, Json));
		File.Move(temp, file, overwrite: true);
	}

	internal static SessionState? Map(string? hookEvent, string? notificationType) => hookEvent switch
	{
		"SessionStart" or "Stop" or "StopFailure" => SessionState.Idle,
		"UserPromptSubmit" or "PreToolUse" or "PostToolUse" or "PostToolUseFailure" or "PermissionDenied" => SessionState.Working,
		"PermissionRequest" => SessionState.Waiting,
		"Notification" when notificationType == "permission_prompt" => SessionState.Waiting,
		_ => null,
	};

	internal static Session? Read(string file)
	{
		try
		{
			return File.Exists(file) ? JsonSerializer.Deserialize<Session>(File.ReadAllText(file), Json) : null;
		}
		catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
		{
			return null;
		}
	}
}
