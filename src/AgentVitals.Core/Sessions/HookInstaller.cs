using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgentVitals.Platform;

namespace AgentVitals.Core.Sessions;

/// <summary>Подключены ли hooks AgentVitals к агенту.</summary>
public enum HookLink
{
	Off,
	On,
	Moved,
}

/// <summary>
/// Hooks AgentVitals в настройках агента (HookTarget): добавить, убрать, узнать состояние. Чужие ключи и
/// чужие hooks не трогаются; свои записи узнаёт цель.
/// </summary>
public static class HookInstaller
{
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

	/// <summary>Подключены ли hooks этой программы к агенту; ошибка чтения файла — «нет» с текстом ошибки.</summary>
	public static HookLink Link(HookTarget target, out string? error)
	{
		error = null;

		try
		{
			return State(target, Environment.ProcessPath ?? "");
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
		{
			error = exception.Message;

			return HookLink.Off;
		}
	}

	public static HookLink State(HookTarget target, string exe)
	{
		if (!File.Exists(target.File))
		{
			return HookLink.Off;
		}

		var commands = Ours(target, Load(target.File)).Select(target.Exe).ToList();

		return commands.Count == 0 ? HookLink.Off
			: commands.Count == target.Events.Count && commands.All(command => string.Equals(command, exe, StringComparison.OrdinalIgnoreCase)) ? HookLink.On
			: HookLink.Moved;
	}

	/// <summary>Подключить заново с текущим путём к exe: свои старые записи убираются.</summary>
	public static void Install(HookTarget target, string exe) =>
		Change(target.File, root =>
		{
			Strip(target, root);
			Add(target, root, exe);
		});

	public static void Uninstall(HookTarget target)
	{
		if (File.Exists(target.File))
		{
			Change(target.File, root => Strip(target, root));
		}
	}

	internal static JsonObject Load(string file) =>
		JsonNode.Parse(SharedFile.ReadAllText(file), documentOptions: _read) as JsonObject
			?? throw new JsonException($"{Path.GetFileName(file)} is not a JSON object");

	internal static void Add(HookTarget target, JsonObject root, string exe)
	{
		var hooks = root["hooks"] as JsonObject ?? (JsonObject)(root["hooks"] = new JsonObject())!;

		foreach (var hookEvent in target.Events)
		{
			var hook = target.Make(exe);

			if (hookEvent.Async)
			{
				hook["async"] = true;
			}

			if (hookEvent.Timeout is { } timeout)
			{
				hook["timeout"] = timeout;
			}

			var group = new JsonObject();

			if (hookEvent.Matcher is not null)
			{
				group["matcher"] = hookEvent.Matcher;
			}

			group["hooks"] = new JsonArray(hook);

			var groups = hooks[hookEvent.Name] as JsonArray ?? (JsonArray)(hooks[hookEvent.Name] = new JsonArray())!;
			groups.Add(group);
		}
	}

	/// <summary>Убрать свои записи и только те контейнеры, что опустели из-за этого.</summary>
	/// <remarks>
	/// Пустой контейнер, в который мы писали, был ли он до нас — уже не узнать; для агента
	/// пустой и отсутствующий раздел значат одно и то же, поэтому такой удаляется.
	/// </remarks>
	internal static void Strip(HookTarget target, JsonObject root)
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

				var ours = list.OfType<JsonObject>().Where(hook => target.Exe(hook) is not null).ToList();
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

	private static void Change(string file, Action<JsonObject> change)
	{
		// Разбор первым: испорченный файл бросит здесь, до бэкапа и до записи.
		var root = File.Exists(file) ? Load(file) : new JsonObject();

		// Копия перед каждой записью: перезапись теряет комментарии, а человек мог дописать их после прошлой.
		if (File.Exists(file))
		{
			File.Copy(file, file + ".agentvitals.bak", overwrite: true);
		}

		change(root);

		Directory.CreateDirectory(Path.GetDirectoryName(file)!);
		var temp = file + ".agentvitals.tmp";
		File.WriteAllText(temp, root.ToJsonString(_write));
		File.Move(temp, file, overwrite: true);
	}

	private static IEnumerable<JsonObject> Ours(HookTarget target, JsonObject root) =>
		(root["hooks"] as JsonObject)?
			.Select(pair => pair.Value)
			.OfType<JsonArray>()
			.SelectMany(groups => groups.OfType<JsonObject>())
			.SelectMany(group => (group["hooks"] as JsonArray)?.OfType<JsonObject>() ?? [])
			.Where(hook => target.Exe(hook) is not null)
		?? [];
}
