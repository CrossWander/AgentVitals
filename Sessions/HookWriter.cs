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

	// Для подписи сессии хватает начала запроса; вставленный лог целиком незачем носить в каждом событии.
	private const int _promptLength = 120;

	internal static readonly JsonSerializerOptions Json = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		Converters =
		{
			new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
		},
	};

	/// <summary>Прочитать событие и записать состояние; не бросает никогда.</summary>
	/// <param name="agent">Чей hook: «Claude» или «Codex».</param>
	public static void Run(Stream input, string directory, long at, int? pid = null, string agent = "Claude")
	{
		try
		{
			using var reader = new StreamReader(input, Encoding.UTF8);
			Apply(JsonNode.Parse(reader.ReadToEnd()), directory, at, pid, agent);
		}
		catch (Exception)
		{
			// Сбой hook не должен доходить до Claude Code: ненулевой код или вывод он покажет
			// посреди работы, а сессия без статуса в трее — не повод человека отвлекать.
		}
	}

	internal static void Apply(JsonNode? payload, string directory, long at, int? pid = null, string agent = "Claude")
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
				? new SessionRecord(SessionState.Idle, null, cwd, at, Ended: true, Agent: agent)
				: new SessionRecord(
					state.Value,
					state == SessionState.Working ? (string?)payload["tool_name"] : null,
					cwd,
					at,

					// Журнал нужен, чтобы заметить Esc, о котором hooks Claude Code молчат; у Codex для него есть Interrupt.
					Transcript: agent == "Claude" ? (string?)payload["transcript_path"] : null,
					Turn: Turn(hookEvent, state.Value, recorded, at),
					Pid: pid,
					Agent: agent,

					// Первый запрос, а не последний: «да» и «продолжай» сессию не называют.
					Prompt: recorded?.Prompt ?? (hookEvent == "UserPromptSubmit" ? Clip((string?)payload["prompt"]) : null));

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
	private static bool Stale(SessionRecord? recorded, SessionRecord next) =>
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

	/// <summary>Начало хода — запрос человека. События хода его наследуют, Stop тоже: по нему трей узнаёт длительность.
	/// Прерванный ход (Interrupt у Codex) длительности не имеет — как и Esc у Claude Code.</summary>
	private static long? Turn(string? hookEvent, SessionState state, SessionRecord? recorded, long at) =>
		hookEvent == "UserPromptSubmit" ? at
		: hookEvent is "SessionStart" or "Interrupt" ? null
		: state == SessionState.Idle ? recorded?.Turn

		// Ход без запроса или сессия, начатая до подключения hooks, — отсчёт с первого события.
		: recorded is { State: not SessionState.Idle, Turn: { } turn } ? turn
		: at;

	/// <summary>Запрос одной строкой, не длиннее 120 знаков; пустой — null.</summary>
	internal static string? Clip(string? prompt)
	{
		var text = string.Join(' ', (prompt ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

		return text.Length == 0 ? null : text.Length > _promptLength ? text[..(_promptLength - 1)] + "…" : text;
	}

	/// <summary>Событие Claude Code или Codex → состояние: имена у них общие, Interrupt — только у Codex.</summary>
	internal static SessionState? Map(string? hookEvent, string? notificationType) => hookEvent switch
	{
		"SessionStart" or "Stop" or "StopFailure" or "Interrupt" => SessionState.Idle,
		"UserPromptSubmit" or "PreToolUse" or "PostToolUse" or "PostToolUseFailure" or "PermissionDenied" => SessionState.Working,
		"PermissionRequest" => SessionState.Waiting,
		"Notification" when notificationType == "permission_prompt" => SessionState.Waiting,
		_ => null,
	};

	internal static SessionRecord? Read(string file)
	{
		try
		{
			return File.Exists(file) ? JsonSerializer.Deserialize<SessionRecord>(SharedFile.ReadAllText(file), Json) : null;
		}
		catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
		{
			return null;
		}
	}
}
