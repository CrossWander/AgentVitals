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

/// <summary>Запись о сессии, которую hook кладёт в файл. At — тики момента, когда hook запустился.</summary>
internal sealed record Session(SessionState State, string? Tool, string Cwd, long At)
{
	// Только для чтения в коде: в файле время хранится одним полем at.
	[JsonIgnore]
	public DateTime Time => new(At);
}

/// <summary>Сессия для показа: состояние с поправкой на давность, папка и ветка.</summary>
internal sealed record LiveSession(string Id, SessionState State, string? Tool, string Folder, string? Branch);
