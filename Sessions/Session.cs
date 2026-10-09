using System;
using System.Collections.Generic;
using System.Linq;
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
/// <remarks>Turn — тики UTC начала хода; после Stop остаётся, чтобы трей узнал, сколько ход занял.</remarks>
/// <remarks>Pid — процесс Claude Code: умер он — сессия кончилась, даже если SessionEnd не пришёл.</remarks>
internal sealed record SessionRecord(SessionState State, string? Tool, string Cwd, long Utc, bool Ended = false, string? Transcript = null, long? Turn = null, int? Pid = null)
{
	// Только для чтения в коде: в файле время хранится одним полем utc.
	[JsonIgnore]
	public DateTime Time => new(Utc, DateTimeKind.Utc);
}

/// <summary>Сессия для показа: состояние с поправкой на давность, папка и ветка.</summary>
/// <remarks>TurnStarted — начало идущего хода; TurnDuration — сколько занял закончившийся (null — прерван или неизвестно).</remarks>
internal sealed record Session(string Id, SessionState State, string? Tool, string Folder, string? Branch, DateTime? TurnStarted = null, TimeSpan? TurnDuration = null);

/// <summary>О чём сказать тостом: ход закончился или сессия ждёт разрешения.</summary>
internal sealed record SessionAlert(Session Session, bool Waiting);

/// <summary>Перемены в сессиях, о которых стоит сказать, пока человек в другом окне.</summary>
internal static class SessionAlarm
{
	/// <param name="before">Прошлое чтение; сессии, которых в нём нет, молчат — неизвестно, что было до них.</param>
	/// <param name="shortest">Ход короче этого не стоит тоста: на коротком человек и так смотрит в терминал.</param>
	public static IReadOnlyList<SessionAlert> Check(IReadOnlyList<Session> before, IReadOnlyList<Session> after, TimeSpan shortest)
	{
		var was = before.ToDictionary(session => session.Id, session => session.State);

		return
		[
			.. after
				.Where(session => was.TryGetValue(session.Id, out var state) && state != session.State)
				.Where(session => session.State == SessionState.Waiting || (session.State == SessionState.Idle && session.TurnDuration >= shortest))
				.Select(session => new SessionAlert(session, session.State == SessionState.Waiting)),
		];
	}
}
