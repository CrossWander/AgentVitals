using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;

namespace AgentVitals;

/// <summary>Режим hook: событие Claude Code из stdin превращается в файл состояния сессии.</summary>
internal static class HookWriter
{
	private static readonly long _sameMoment = TimeSpan.FromMilliseconds(50).Ticks;

	internal static readonly JsonSerializerOptions Json = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		Converters =
		{
			new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
		},
	};

	/// <summary>Прочитать событие и записать состояние; не бросает никогда.</summary>
	public static void Run(Stream input, string directory, long at, int? pid = null)
	{
		try
		{
			using var reader = new StreamReader(input, Encoding.UTF8);
			Apply(JsonNode.Parse(reader.ReadToEnd()), directory, at, pid);
		}
		catch (Exception)
		{
			// Сбой hook не должен доходить до Claude Code: ненулевой код или вывод он покажет
			// посреди работы, а сессия без статуса в трее — не повод человека отвлекать.
		}
	}

	internal static void Apply(JsonNode? payload, string directory, long at, int? pid = null)
	{
		if (payload is null
			|| (string?)payload["session_id"] is not { Length: > 0 } id
			|| id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
			|| id.Contains("..", StringComparison.Ordinal))
		{
			return;
		}

		var file = Path.Combine(directory, id + ".json");
		var cwd = (string?)payload["cwd"] ?? "";
		var hookEvent = (string?)payload["hook_event_name"];

		var state = hookEvent == "SessionEnd" ? SessionState.Idle : Map(hookEvent, (string?)payload["notification_type"]);

		if (state is null)
		{
			return;
		}

		// PreToolUse и PermissionRequest одного вызова инструмента запускаются почти разом: чтение,
		// сравнение и запись — под мьютексом сессии, иначе последним пишет тот, кто медленнее прогрелся.
		using var mutex = new Mutex(false, $"AgentVitals.hook.{id}");
		var owned = Acquire(mutex);

		try
		{
			var recorded = Read(file);

			// SessionEnd не удаляет файл, а оставляет отметку: async-событие, запущенное перед выходом
			// и финишировавшее после, иначе воскресило бы сессию. Отметку через минуту убирает трей.
			var session = hookEvent == "SessionEnd"
				? new Session(SessionState.Idle, null, cwd, at, Ended: true)
				: new Session(
					state.Value,
					state == SessionState.Working ? (string?)payload["tool_name"] : null,
					cwd,
					at,
					Transcript: (string?)payload["transcript_path"],
					Turn: Turn(hookEvent, state.Value, recorded, at),
					Pid: pid);

			if (Stale(recorded, session))
			{
				return;
			}

			Directory.CreateDirectory(directory);
			var temp = $"{file}.{Environment.ProcessId}.tmp";

			try
			{
				File.WriteAllText(temp, JsonSerializer.Serialize(session, Json));
				Replace(temp, file);
			}
			finally
			{
				// После удачной подмены файла уже нет; после неудачной — не оставлять мусор в папке сессий.
				File.Delete(temp);
			}
		}
		finally
		{
			if (owned)
			{
				mutex.ReleaseMutex();
			}
		}
	}

	/// <summary>Событие опоздало: записано более новое, или «ждёт» из того же мгновения.</summary>
	/// <remarks>
	/// Async-hooks финишируют не по порядку. Время старта процессов совпадает с точностью до тика таймера,
	/// а из «ждёт» в «работает» без ответа человека не попасть — значит, это одно мгновение и «ждёт» главнее.
	/// </remarks>
	private static bool Stale(Session? recorded, Session next) =>
		recorded is not null
		&& (recorded.Utc > next.Utc
			|| (!next.Ended
				&& recorded.State == SessionState.Waiting
				&& next.State != SessionState.Waiting
				&& next.Utc - recorded.Utc < _sameMoment));

	// Пока трей читает файл (раз в секунду, доли миллисекунды), Windows не даёт подменить его переименованием.
	private static void Replace(string temp, string file)
	{
		for (var attempt = 1; ; attempt++)
		{
			try
			{
				File.Move(temp, file, overwrite: true);

				return;
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException && attempt < 10)
			{
				Thread.Sleep(20);
			}
		}
	}

	private static bool Acquire(Mutex mutex)
	{
		try
		{
			// Зависший сосед не должен держать Claude Code: через две секунды пишем без очереди.
			return mutex.WaitOne(TimeSpan.FromSeconds(2));
		}
		catch (AbandonedMutexException)
		{
			// Прежний hook умер, держа мьютекс: теперь он наш.
			return true;
		}
	}

	/// <summary>Начало хода — запрос человека. События хода его наследуют, Stop тоже: по нему трей узнаёт длительность.</summary>
	private static long? Turn(string? hookEvent, SessionState state, Session? recorded, long at) =>
		hookEvent == "UserPromptSubmit" ? at
		: hookEvent == "SessionStart" ? null
		: state == SessionState.Idle ? recorded?.Turn

		// Ход без запроса или сессия, начатая до подключения hooks, — отсчёт с первого события.
		: recorded is { State: not SessionState.Idle, Turn: { } turn } ? turn
		: at;

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
			return File.Exists(file) ? JsonSerializer.Deserialize<Session>(SharedFile.ReadAllText(file), Json) : null;
		}
		catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
		{
			return null;
		}
	}
}
