using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AgentVitals;

/// <summary>Живые сессии из папки состояний, с поправкой на то, что hook мог не дожить до конца сессии.</summary>
internal static class SessionWatcher
{
	private const string _branchPrefix = "ref: refs/heads/";

	// Ход, который «работает» дольше четверти часа, оборван: Stop не пришёл. Вопрос о разрешении
	// человек может оставить на обед. Простаивающая сессия старше часа — закрытый терминал.
	private static readonly TimeSpan _workingStale = TimeSpan.FromMinutes(15);
	private static readonly TimeSpan _waitingStale = TimeSpan.FromHours(2);
	private static readonly TimeSpan _idleForgotten = TimeSpan.FromHours(1);

	public static IReadOnlyList<LiveSession> Read(string directory, DateTime now)
	{
		if (!Directory.Exists(directory))
		{
			return [];
		}

		List<LiveSession> sessions = [];

		foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
		{
			if (HookWriter.Read(file) is not { } session)
			{
				continue;
			}

			var age = now - session.Time;
			var state = Effective(session.State, age);

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
				Branch(session.Cwd)));
		}

		return sessions;
	}

	internal static SessionState Effective(SessionState state, TimeSpan age) => state switch
	{
		SessionState.Working when age > _workingStale => SessionState.Idle,
		SessionState.Waiting when age > _waitingStale => SessionState.Idle,
		_ => state,
	};

	public static SessionState Overall(IEnumerable<LiveSession> sessions) =>
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
