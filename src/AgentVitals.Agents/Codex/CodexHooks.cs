using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using AgentVitals.Core.Sessions;

namespace AgentVitals.Agents.Codex;

/// <summary>Hooks Codex: в какой файл, на какие события и какой командой.</summary>
internal static class CodexHooks
{
	private const string _args = "hook codex";

	// Codex запускает строку через cmd.exe /C — путь в кавычках. SubagentStop не подключён: у субагента session_id
	// родителя, и его конец сбросил бы главную сессию посреди хода. Interrupt и SessionEnd у Codex синхронные,
	// по умолчанию с секундой на всё — три с запасом на прогрев exe.
	public static HookTarget Target { get; } = new(
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
		exe => new JsonObject { ["type"] = "command", ["command"] = $"\"{exe}\" {_args}" },
		hook => (string?)hook["command"] is { } command
			&& command.EndsWith(" " + _args, StringComparison.Ordinal)
			&& command[..^(_args.Length + 1)].Trim().Trim('"') is var exe
			&& HookTarget.IsAgentVitals(exe)
				? exe
				: null,
		"codex",

		// codex.exe при любой установке: из npm node лишь запускает его.
		new HashSet<string>(["codex.exe"], StringComparer.OrdinalIgnoreCase),

		// Прерывание Codex сообщает сам — событием Interrupt.
		Transcript: false);
}
