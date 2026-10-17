using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgentVitals.Platform;

namespace AgentVitals.Core.Sessions;

/// <summary>Живые сессии из папки состояний, с поправкой на то, что hook мог не дожить до конца сессии.</summary>
public static class SessionWatcher
{
	private const string _branchPrefix = "ref: refs/heads/";

	// Ход, который «работает» дольше четверти часа, оборван: Stop не пришёл. Вопрос о разрешении
	// человек может оставить на обед. Простаивающая сессия старше часа — закрытый терминал.
	private static readonly TimeSpan _workingStale = TimeSpan.FromMinutes(15);
	private static readonly TimeSpan _waitingStale = TimeSpan.FromHours(2);
	private static readonly TimeSpan _idleForgotten = TimeSpan.FromHours(1);

	// Отметка о закрытии нужна, пока могут финишировать async-hooks, запущенные перед выходом.
	private static readonly TimeSpan _endedForgotten = TimeSpan.FromMinutes(1);

	/// <param name="agents">Hooks агентов: кто как узнаёт прерывание по своему журналу сессии.</param>
	public static IReadOnlyList<Session> Read(string directory, DateTime now, IReadOnlyList<HookTarget> agents)
	{
		if (!Directory.Exists(directory))
		{
			return [];
		}

		List<Session> sessions = [];

		foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
		{
			if (HookWriter.Read(file) is not { } session)
			{
				continue;
			}

			var age = now - session.Time;

			if (session.Ended)
			{
				if (age > _endedForgotten)
				{
					Forget(file);
				}

				continue;
			}

			// Терминал закрыли крестиком или Claude Code упал: SessionEnd не придёт, а ждать час незачем.
			if (session.Pid is { } pid && !AgentProcess.Alive(pid, session.Time))
			{
				Forget(file);

				continue;
			}

			var state = Effective(session.State, age);
			var agent = session.Agent ?? "Claude";

			if (state != SessionState.Idle
				&& session.Transcript is { Length: > 0 } transcript
				&& agents.FirstOrDefault(target => target.Agent == agent)?.Interrupted is { } interrupted
				&& interrupted(transcript, session.Time))
			{
				state = SessionState.Idle;
			}

			DateTime? turn = session.Turn is { } ticks ? new DateTime(ticks, DateTimeKind.Utc) : null;

			if (state == SessionState.Idle && age > _idleForgotten)
			{
				Forget(file);

				continue;
			}

			sessions.Add(new(
				Path.GetFileNameWithoutExtension(file),
				state,
				state == SessionState.Working ? session.Tool : null,
				Folder(session.Cwd),
				Branch(session.Cwd),
				state == SessionState.Idle ? null : turn,

				// Длительность — только у хода, закрытого Stop: прерванный Esc или брошенный её не знает.
				session.State == SessionState.Idle ? session.Time - turn : null,
				session.Pid,
				session.Cwd,
				agent,
				session.Prompt));
		}

		return sessions;
	}

	internal static SessionState Effective(SessionState state, TimeSpan age) => state switch
	{
		SessionState.Working when age > _workingStale => SessionState.Idle,
		SessionState.Waiting when age > _waitingStale => SessionState.Idle,
		_ => state,
	};

	public static SessionState Overall(IEnumerable<Session> sessions) =>
		sessions
			.Select(session => session.State)
			.DefaultIfEmpty(SessionState.Idle)
			.Max();

	/// <summary>Ветка git ближайшего репозитория над папкой; null — репозитория нет.</summary>
	internal static string? Branch(string cwd)
	{
		if (string.IsNullOrWhiteSpace(cwd))
		{
			return null;
		}

		try
		{
			// Сетевую папку не трогаем: недоступный сервер держит каждую проверку секунды,
			// а ветка читается раз в секунду в потоке окна.
			// ponytail: у сессий на сетевых дисках нет ветки; понадобится — читать в фоне с кэшем.
			if (cwd.StartsWith(@"\\", StringComparison.Ordinal)
				|| cwd.StartsWith("//", StringComparison.Ordinal)
				|| new DriveInfo(cwd).DriveType == DriveType.Network)
			{
				return null;
			}

			for (var directory = new DirectoryInfo(cwd); directory is not null; directory = directory.Parent)
			{
				var git = Path.Combine(directory.FullName, ".git");
				var head = Directory.Exists(git) ? Path.Combine(git, "HEAD")
					: File.Exists(git) ? Path.Combine(Worktree(git), "HEAD")
					: null;

				if (head is null)
				{
					continue;
				}

				var text = SharedFile.ReadAllText(head).Trim();

				return text.StartsWith(_branchPrefix, StringComparison.Ordinal)
					? text[_branchPrefix.Length..]
					: text[..Math.Min(7, text.Length)];
			}
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
		{
			// ветка — украшение строки; без неё сессия показывается так же
		}

		return null;
	}

	// В worktree .git — файл со строкой «gitdir: <путь>» к настоящей папке репозитория.
	private static string Worktree(string gitFile)
	{
		var path = SharedFile.ReadAllText(gitFile).Trim()["gitdir:".Length..].Trim();

		return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(gitFile)!, path));
	}

	private static string Folder(string cwd) =>
		Path.GetFileName(cwd.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : cwd;

	private static void Forget(string file)
	{
		try
		{
			File.Delete(file);
		}
		catch (IOException)
		{
			// hook как раз пишет этот файл — сессия ожила, удалять нечего
		}
	}
}
