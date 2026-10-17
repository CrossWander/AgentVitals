using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using AgentVitals.Core.Sessions;
using AgentVitals.Platform;

namespace AgentVitals.Agents.Claude;

/// <summary>Hooks Claude Code: в какой файл, на какие события и какой командой.</summary>
internal static class ClaudeHooks
{
	// SessionEnd синхронный: фоновый процесс при выходе Claude Code может не успеть, а бюджет события — полторы секунды.
	// Exec-форма: Claude Code запускает exe сам, без shell, — пробелы и кириллица в пути не мешают.
	public static HookTarget Target { get; } = new(
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
		hook => hook["args"] is JsonArray { Count: 1 } args && (string?)args[0] == "hook" && HookTarget.IsAgentVitals((string?)hook["command"])
			? (string?)hook["command"]
			: null,
		"",

		// node.exe — Claude Code, поставленный через npm; родной установщик даёт claude.exe.
		new HashSet<string>(["claude.exe", "node.exe"], StringComparer.OrdinalIgnoreCase),

		// О прерывании клавишей Esc hooks Claude Code молчат — оно видно только в журнале сессии.
		Interrupted: ClaudeTranscript.Interrupted);
}
