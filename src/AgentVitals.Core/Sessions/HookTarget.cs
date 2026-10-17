using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;

namespace AgentVitals.Core.Sessions;

/// <summary>Событие агента, на которое подключается hook: matcher, фоновый ли запуск, таймаут в секундах.</summary>
public sealed record HookEvent(string Name, string? Matcher = null, bool Async = true, int? Timeout = null);

/// <summary>
/// Куда и в каком виде подключаются hooks одного агента. Agent — имя агента: им помечаются сессии;
/// Title — имя программы в подписях. Make строит запись hook для exe; Exe узнаёт свою запись и отдаёт путь из неё.
/// Key — слово после «hook» в команде; Processes — имена exe агента, предка процесса hook;
/// Interrupted — как по журналу сессии узнать прерывание, о котором hooks молчат; null — журнал не нужен.
/// </summary>
public sealed record HookTarget(
	string Agent,
	string Title,
	string File,
	IReadOnlyList<HookEvent> Events,
	Func<string, JsonObject> Make,
	Func<JsonObject, string?> Exe,
	string Key,
	IReadOnlySet<string> Processes,
	Func<string, DateTime, bool>? Interrupted)
{
	/// <summary>Запоминать журнал сессии: по нему агент узнаёт прерывание.</summary>
	public bool Transcript => Interrupted is not null;

	/// <summary>Путь в записи hook — к этой программе: путь мог смениться, имя — нет.</summary>
	public static bool IsAgentVitals(string? path) =>
		Path.GetFileNameWithoutExtension(path ?? "").Equals("AgentVitals", StringComparison.OrdinalIgnoreCase);
}
