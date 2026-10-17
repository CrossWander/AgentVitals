using System.Collections.Generic;
using System.Linq;
using AgentVitals.Agents.Claude;
using AgentVitals.Agents.Codex;
using AgentVitals.Agents.Copilot;
using AgentVitals.Agents.Cursor;
using AgentVitals.Agents.Gemini;
using AgentVitals.Core;
using AgentVitals.Core.Sessions;

namespace AgentVitals.Agents;

/// <summary>Все агенты программы, в порядке вкладок: больше никто в программе их по именам не знает.</summary>
public static class AgentCatalog
{
	/// <summary>Агенты заново: у каждого опроса — свои.</summary>
	public static IReadOnlyList<IAgent> All() =>
		[new ClaudeAgent(), new CodexAgent(), new GeminiAgent(), new CopilotAgent(), new CursorAgent()];

	/// <summary>Агенты, к которым подключаются hooks живых сессий.</summary>
	/// <remarks>Режим hook строит всех агентов на каждое событие: их конструкторы не читают файлов и не бросают.</remarks>
	public static IReadOnlyList<HookTarget> Hooks { get; } = [.. All().Select(agent => agent.Hooks).OfType<HookTarget>()];

	/// <summary>Чей hook по слову после «hook»: без слова и с незнакомым — Claude, так его звали до Codex.</summary>
	public static HookTarget Hook(string? key) =>
		Hooks.FirstOrDefault(target => target.Key == key) ?? Hooks.First(target => target.Key.Length == 0);
}
