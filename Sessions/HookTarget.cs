using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace AgentVitals;

/// <summary>Событие агента, на которое подключается hook: matcher, фоновый ли запуск, таймаут в секундах.</summary>
internal sealed record HookEvent(string Name, string? Matcher = null, bool Async = true, int? Timeout = null);

/// <summary>
/// Куда и в каком виде подключаются hooks одного агента. Agent — имя провайдера: им помечаются сессии;
/// Title — имя программы в подписях. Make строит запись hook для exe; Exe узнаёт свою запись и отдаёт путь из неё.
/// Key — слово после «hook» в команде; Processes — имена exe агента, предка процесса hook;
/// Transcript — hooks молчат о прерывании, и его ищут в журнале сессии.
/// </summary>
internal sealed record HookTarget(
	string Agent,
	string Title,
	string File,
	IReadOnlyList<HookEvent> Events,
	Func<string, JsonObject> Make,
	Func<JsonObject, string?> Exe,
	string Key,
	IReadOnlySet<string> Processes,
	bool Transcript)
{
	private const string _codexArgs = "hook codex";

	// SessionEnd синхронный: фоновый процесс при выходе Claude Code может не успеть, а бюджет события — полторы секунды.
	// Exec-форма: Claude Code запускает exe сам, без shell, — пробелы и кириллица в пути не мешают.
	public static HookTarget Claude { get; } = new(
		"Claude",
		"Claude Code",
		Path.Combine(Paths.Home, ".claude", "settings.json"),
		[
			new("SessionStart"),
			new("UserPromptSubmit"),
			new("PreToolUse"),
			new("PostToolUse"),
			new("PostToolUseFailure"),
			new("PermissionRequest"),
			new("PermissionDenied"),
			new("Notification", "permission_prompt"),
			new("Stop"),
			new("StopFailure"),
			new("SessionEnd", Async: false),
		],
		exe => new JsonObject { ["type"] = "command", ["command"] = exe, ["args"] = new JsonArray("hook") },
		hook => hook["args"] is JsonArray { Count: 1 } args && (string?)args[0] == "hook" && IsAgentVitals((string?)hook["command"])
			? (string?)hook["command"]
			: null,
		"",

		// node.exe — Claude Code, поставленный через npm; родной установщик даёт claude.exe.
		new HashSet<string>(["claude.exe", "node.exe"], StringComparer.OrdinalIgnoreCase),

		// О прерывании клавишей Esc hooks Claude Code молчат.
		Transcript: true);

	// Codex запускает строку через cmd.exe /C — путь в кавычках. SubagentStop не подключён: у субагента session_id
	// родителя, и его конец сбросил бы главную сессию посреди хода. Interrupt и SessionEnd у Codex синхронные,
	// по умолчанию с секундой на всё — три с запасом на прогрев exe.
	public static HookTarget Codex { get; } = new(
		"Codex",
		"Codex",
		Path.Combine(CodexProvider.Home, "hooks.json"),
		[
			new("SessionStart"),
			new("UserPromptSubmit"),
			new("PreToolUse"),
			new("PostToolUse"),
			new("PermissionRequest"),
			new("Stop"),
			new("Interrupt", Async: false, Timeout: 3),
			new("SessionEnd", Async: false, Timeout: 3),
		],
		exe => new JsonObject { ["type"] = "command", ["command"] = $"\"{exe}\" {_codexArgs}" },
		hook => (string?)hook["command"] is { } command
			&& command.EndsWith(" " + _codexArgs, StringComparison.Ordinal)
			&& command[..^(_codexArgs.Length + 1)].Trim().Trim('"') is var exe
			&& IsAgentVitals(exe)
				? exe
				: null,
		"codex",

		// codex.exe при любой установке: из npm node лишь запускает его.
		new HashSet<string>(["codex.exe"], StringComparer.OrdinalIgnoreCase),

		// Прерывание Codex сообщает сам — событием Interrupt.
		Transcript: false);

	/// <summary>Агенты, к которым подключаются hooks; у Gemini их нет.</summary>
	public static IReadOnlyList<HookTarget> All { get; } = [Claude, Codex];

	/// <summary>Чей hook по слову после «hook»: без слова и с незнакомым — Claude, так его звали до Codex.</summary>
	public static HookTarget For(string? key) => All.FirstOrDefault(target => target.Key == key) ?? All.First(target => target.Key.Length == 0);

	// Путь к программе мог смениться, имя — нет.
	private static bool IsAgentVitals(string? path) =>
		Path.GetFileNameWithoutExtension(path ?? "").Equals("AgentVitals", StringComparison.OrdinalIgnoreCase);
}
