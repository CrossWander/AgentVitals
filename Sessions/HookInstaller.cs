using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentVitals;

/// <summary>Подключены ли hooks AgentVitals к Claude Code.</summary>
public enum HookLink
{
	Off,
	On,
	Moved,
}

/// <summary>
/// Hooks AgentVitals в ~/.claude/settings.json: добавить, убрать, узнать состояние. Чужие ключи и
/// чужие hooks не трогаются; свои записи узнаются по exe и аргументу «hook».
/// </summary>
internal static class HookInstaller
{
	// Событие, matcher и фоновый ли запуск. SessionEnd синхронный: фоновый процесс при выходе
	// Claude Code может не успеть, а бюджет события — полторы секунды.
	private static readonly (string Event, string? Matcher, bool Async)[] _hooks =
	[
		("SessionStart", null, true),
		("UserPromptSubmit", null, true),
		("PreToolUse", null, true),
		("PostToolUse", null, true),
		("PostToolUseFailure", null, true),
		("PermissionRequest", null, true),
		("PermissionDenied", null, true),
		("Notification", "permission_prompt", true),
		("Stop", null, true),
		("StopFailure", null, true),
		("SessionEnd", null, false),
	];

	// Файл открывает и правит человек: с отступами и кириллицей, а не \uXXXX.
	private static readonly JsonSerializerOptions _write = new()
	{
		WriteIndented = true,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
	};

	private static readonly JsonDocumentOptions _read = new()
	{
		CommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
	};

	public static string SettingsPath { get; } = Path.Combine(Paths.Home, ".claude", "settings.json");

	/// <summary>Подключены ли hooks этой программы; ошибка чтения settings.json — «нет» с текстом ошибки.</summary>
	public static HookLink Link(out string? error)
	{
		error = null;

		try
		{
			return State(SettingsPath, Environment.ProcessPath ?? "");
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
		{
			error = exception.Message;

			return HookLink.Off;
		}
	}

	public static HookLink State(string settings, string exe)
	{
		if (!File.Exists(settings))
		{
			return HookLink.Off;
		}

		var commands = Ours(Load(settings))
			.Select(hook => (string?)hook["command"])
			.ToList();

		return commands.Count == 0 ? HookLink.Off
			: commands.Count == _hooks.Length && commands.All(command => string.Equals(command, exe, StringComparison.OrdinalIgnoreCase)) ? HookLink.On
			: HookLink.Moved;
	}

	/// <summary>Подключить заново с текущим путём к exe: свои старые записи убираются.</summary>
	public static void Install(string settings, string exe) =>
		Change(settings, root =>
		{
			Strip(root);
			Add(root, exe);
		});

	public static void Uninstall(string settings)
	{
		if (File.Exists(settings))
		{
			Change(settings, Strip);
		}
	}

	internal static JsonObject Load(string settings) =>
		JsonNode.Parse(SharedFile.ReadAllText(settings), documentOptions: _read) as JsonObject
			?? throw new JsonException("settings.json is not a JSON object");

	internal static void Add(JsonObject root, string exe)
	{
		var hooks = root["hooks"] as JsonObject ?? (JsonObject)(root["hooks"] = new JsonObject())!;

		foreach (var (name, matcher, isAsync) in _hooks)
		{
			// Exec-форма: Claude Code запускает exe сам, без shell, — пробелы и кириллица в пути не мешают.
			var hook = new JsonObject
			{
				["type"] = "command",
				["command"] = exe,
				["args"] = new JsonArray("hook"),
			};

			if (isAsync)
			{
				hook["async"] = true;
			}

			var group = new JsonObject();

			if (matcher is not null)
			{
				group["matcher"] = matcher;
			}

			group["hooks"] = new JsonArray(hook);

			var groups = hooks[name] as JsonArray ?? (JsonArray)(hooks[name] = new JsonArray())!;
			groups.Add(group);
		}
	}

	/// <summary>Убрать свои записи и только те контейнеры, что опустели из-за этого.</summary>
	/// <remarks>
	/// Пустой контейнер, в который мы писали, был ли он до нас — уже не узнать; для Claude Code
	/// пустой и отсутствующий раздел значат одно и то же, поэтому такой удаляется.
	/// </remarks>
	internal static void Strip(JsonObject root)
	{
		if (root["hooks"] is not JsonObject hooks)
		{
			return;
		}

		var emptiedAny = false;

		foreach (var (name, node) in hooks.ToList())
		{
			if (node is not JsonArray groups)
			{
				continue;
			}

			var emptied = false;

			foreach (var group in groups.OfType<JsonObject>().ToList())
			{
				if (group["hooks"] is not JsonArray list)
				{
					continue;
				}

				var ours = list.OfType<JsonObject>().Where(IsOurs).ToList();
				ours.ForEach(hook => list.Remove(hook));

				if (ours.Count > 0 && list.Count == 0)
				{
					groups.Remove(group);
					emptied = true;
				}
			}

			if (emptied && groups.Count == 0)
			{
				hooks.Remove(name);
				emptiedAny = true;
			}
		}

		if (emptiedAny && hooks.Count == 0)
		{
			root.Remove("hooks");
		}
	}

	private static void Change(string settings, Action<JsonObject> change)
	{
		// Разбор первым: испорченный файл бросит здесь, до бэкапа и до записи.
		var root = File.Exists(settings) ? Load(settings) : new JsonObject();

		// Копия перед каждой записью: перезапись теряет комментарии, а человек мог дописать их после прошлой.
		if (File.Exists(settings))
		{
			File.Copy(settings, settings + ".agentvitals.bak", overwrite: true);
		}

		change(root);

		Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
		var temp = settings + ".agentvitals.tmp";
		File.WriteAllText(temp, root.ToJsonString(_write));
		File.Move(temp, settings, overwrite: true);
	}

	private static IEnumerable<JsonObject> Ours(JsonObject root) =>
		(root["hooks"] as JsonObject)?
			.Select(pair => pair.Value)
			.OfType<JsonArray>()
			.SelectMany(groups => groups.OfType<JsonObject>())
			.SelectMany(group => (group["hooks"] as JsonArray)?.OfType<JsonObject>() ?? [])
			.Where(IsOurs)
		?? [];

	// Путь к программе мог смениться, имя и аргумент — нет.
	private static bool IsOurs(JsonObject hook) =>
		hook["args"] is JsonArray { Count: 1 } args
		&& (string?)args[0] == "hook"
		&& Path.GetFileNameWithoutExtension((string?)hook["command"] ?? "").Equals("AgentVitals", StringComparison.OrdinalIgnoreCase);
}
