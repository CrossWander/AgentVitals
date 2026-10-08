using System;
using System.Text.Json.Serialization;

namespace AgentVitals;

/// <summary>Что делает сессия Claude Code; порядок — по важности для значка, главное — Max.</summary>
public enum SessionState
{
	Idle,
	Working,
	Waiting,
}

/// <summary>Запись о сессии, которую hook кладёт в файл. Utc — тики UTC момента, когда hook запустился.</summary>
/// <remarks>Прежний формат хранил местное время в поле at: такие файлы читаются как очень старые и забываются.</remarks>
/// <remarks>Ended — отметка SessionEnd: файл не удаляется сразу, чтобы опоздавшие события сессию не воскресили.</remarks>
/// <remarks>Transcript — журнал сессии: по нему видно прерывание клавишей Esc, о котором hooks молчат.</remarks>
internal sealed record Session(SessionState State, string? Tool, string Cwd, long Utc, bool Ended = false, string? Transcript = null)
{
	// Только для чтения в коде: в файле время хранится одним полем utc.
	[JsonIgnore]
	public DateTime Time => new(Utc, DateTimeKind.Utc);
}

/// <summary>Сессия для показа: состояние с поправкой на давность, папка и ветка.</summary>
internal sealed record LiveSession(string Id, SessionState State, string? Tool, string Folder, string? Branch);
