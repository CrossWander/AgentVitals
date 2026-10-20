using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace AgentVitals.Tests;

public class SessionWatcherTests
{
	private static readonly DateTime _now = new(2026, 10, 7, 12, 0, 0);

	private static void Write(TempDirectory temp, string id, SessionState state, DateTime at, string cwd = @"C:\nowhere", string? tool = null) =>
		File.WriteAllText(temp.File(id + ".json"), JsonSerializer.Serialize(new SessionRecord(state, tool, cwd, at.Ticks), HookWriter.Json));

	[Fact]
	public void WaitingSessionTellsItsTool()
	{
		using var temp = new TempDirectory();
		Write(temp, "s", SessionState.Waiting, _now.AddMinutes(-1), tool: "Bash");

		Assert.Equal("Bash", SessionWatcher.Read(temp.Path, _now, []).Single().Tool);
	}

	[Fact]
	public void IdleSessionTellsSinceWhen() // «простой · 12 мин»: с последнего события
	{
		using var temp = new TempDirectory();
		Write(temp, "s", SessionState.Idle, _now.AddMinutes(-12));

		Assert.Equal(_now.AddMinutes(-12), SessionWatcher.Read(temp.Path, _now, []).Single().IdleSince);
	}

	[Theory]
	[InlineData(SessionState.Working, 14, SessionState.Working)]
	[InlineData(SessionState.Working, 16, SessionState.Idle)]
	[InlineData(SessionState.Waiting, 60, SessionState.Waiting)]
	[InlineData(SessionState.Waiting, 121, SessionState.Idle)]
	[InlineData(SessionState.Idle, 500, SessionState.Idle)]
	public void StaleStatesFallIdle(SessionState state, int minutes, SessionState expected) =>
		Assert.Equal(expected, SessionWatcher.Effective(state, TimeSpan.FromMinutes(minutes)));

	[Fact]
	public void ForgetsLongIdleSessions()
	{
		using var temp = new TempDirectory();
		Write(temp, "old", SessionState.Idle, _now.AddHours(-2));
		Write(temp, "live", SessionState.Working, _now.AddMinutes(-1));

		var session = Assert.Single(SessionWatcher.Read(temp.Path, _now, AgentCatalog.Hooks));

		Assert.Equal("live", session.Id);
		Assert.Equal(SessionState.Working, session.State);
		Assert.False(File.Exists(temp.File("old.json")));
	}

	[Fact]
	public void ForgetsSessionOfClosedClaude() // терминал закрыли крестиком: SessionEnd не пришёл, а час ждать незачем
	{
		using var temp = new TempDirectory();
		var now = DateTime.UtcNow;
		File.WriteAllText(temp.File("gone.json"), JsonSerializer.Serialize(new SessionRecord(SessionState.Working, null, @"C:\x", now.Ticks, Pid: Exited()), HookWriter.Json));
		File.WriteAllText(temp.File("live.json"), JsonSerializer.Serialize(new SessionRecord(SessionState.Working, null, @"C:\x", now.Ticks, Pid: Environment.ProcessId), HookWriter.Json));

		Assert.Equal("live", Assert.Single(SessionWatcher.Read(temp.Path, now, AgentCatalog.Hooks)).Id);
		Assert.False(File.Exists(temp.File("gone.json")));
	}

	[Fact]
	public void SessionKnowsItsClaudeProcess() // по нему щелчок по тосту находит окно, где идёт сессия
	{
		using var temp = new TempDirectory();
		var now = DateTime.UtcNow;
		File.WriteAllText(temp.File("live.json"), JsonSerializer.Serialize(new SessionRecord(SessionState.Waiting, null, @"C:\x", now.Ticks, Pid: Environment.ProcessId), HookWriter.Json));

		var session = Assert.Single(SessionWatcher.Read(temp.Path, now, AgentCatalog.Hooks));
		Assert.Equal((Environment.ProcessId, @"C:\x"), (session.Pid, session.Cwd));
	}

	private static int Exited()
	{
		using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c exit") { CreateNoWindow = true })!;
		process.WaitForExit();

		return process.Id;
	}

	[Fact]
	public void ForgetsFilesWithLocalTime() // прежний формат: поле at в местном времени, в UTC+3 оно «из будущего»
	{
		using var temp = new TempDirectory();
		File.WriteAllText(temp.File("legacy.json"), $$"""{ "state": "working", "cwd": "C:\\x", "at": {{_now.AddHours(3).Ticks}} }""");

		Assert.Empty(SessionWatcher.Read(temp.Path, _now, AgentCatalog.Hooks));
		Assert.False(File.Exists(temp.File("legacy.json")));
	}

	private static string Entry(string type, DateTime at, string text) => new JsonObject
	{
		["type"] = type,
		["timestamp"] = at.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
		["message"] = new JsonObject { ["role"] = type, ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }) },
	}.ToJsonString();

	private static SessionState AfterEsc(SessionState state, DateTime hook)
	{
		using var sessions = new TempDirectory();
		using var logs = new TempDirectory();
		var transcript = logs.File("session.jsonl");

		File.WriteAllLines(transcript,
		[
			Entry("user", _now.AddMinutes(-3), "fix it"),
			Entry("assistant", _now.AddMinutes(-2), "on it"),
			Entry("user", _now.AddMinutes(-1), "[Request interrupted by user for tool use]"),
			new JsonObject { ["type"] = "last-prompt" }.ToJsonString(), // служебные записи после прерывания не мешают
		]);

		File.WriteAllText(sessions.File("s.json"), JsonSerializer.Serialize(new SessionRecord(state, null, @"C:\nowhere", hook.Ticks, Transcript: transcript), HookWriter.Json));

		return Assert.Single(SessionWatcher.Read(sessions.Path, _now, AgentCatalog.Hooks)).State;
	}

	[Theory]
	[InlineData(SessionState.Working)]
	[InlineData(SessionState.Waiting)] // Esc на вопросе о разрешении
	public void EscEndsTheTurn(SessionState state) => // Stop после Esc не приходит: иначе «работает» висело бы четверть часа
		Assert.Equal(SessionState.Idle, AfterEsc(state, _now.AddMinutes(-2)));

	[Fact]
	public void NewPromptAfterEscWorks() => // hook нового хода позже прерывания, а сам запрос в транскрипт ещё не дописан
		Assert.Equal(SessionState.Working, AfterEsc(SessionState.Working, _now.AddSeconds(-10)));

	[Fact]
	public void WorkingSessionKnowsWhenItsTurnStarted() // для таймера «работает · 3 мин»
	{
		using var temp = new TempDirectory();
		var turn = _now.AddMinutes(-3);
		File.WriteAllText(temp.File("s.json"), JsonSerializer.Serialize(new SessionRecord(SessionState.Working, null, @"C:\x", _now.AddMinutes(-1).Ticks, Turn: turn.Ticks), HookWriter.Json));

		var session = Assert.Single(SessionWatcher.Read(temp.Path, _now, AgentCatalog.Hooks));

		Assert.Equal(turn, session.TurnStarted);
		Assert.Null(session.TurnDuration);
	}

	[Fact]
	public void FinishedTurnTellsHowLongItTook()
	{
		using var temp = new TempDirectory();
		File.WriteAllText(temp.File("s.json"), JsonSerializer.Serialize(new SessionRecord(SessionState.Idle, null, @"C:\x", _now.AddMinutes(-1).Ticks, Turn: _now.AddMinutes(-13).Ticks), HookWriter.Json));

		var session = Assert.Single(SessionWatcher.Read(temp.Path, _now, AgentCatalog.Hooks));

		Assert.Equal(TimeSpan.FromMinutes(12), session.TurnDuration);
		Assert.Null(session.TurnStarted);
	}

	[Fact]
	public void WaitingOutranksWorking()
	{
		Session[] sessions =
		[
			new("a", SessionState.Idle, null, "a", null),
			new("b", SessionState.Waiting, null, "b", null),
			new("c", SessionState.Working, "Edit", "c", null),
		];

		Assert.Equal(SessionState.Waiting, SessionWatcher.Overall(sessions));
		Assert.Equal(SessionState.Idle, SessionWatcher.Overall([]));
	}

	[Fact]
	public void ForgetsEndedSessionsAfterAMinute() // отметке хватает минуты, чтобы опоздавшие hooks её увидели
	{
		using var temp = new TempDirectory();
		File.WriteAllText(temp.File("fresh.json"), JsonSerializer.Serialize(new SessionRecord(SessionState.Idle, null, "C:\\x", _now.AddSeconds(-30).Ticks, Ended: true), HookWriter.Json));
		File.WriteAllText(temp.File("old.json"), JsonSerializer.Serialize(new SessionRecord(SessionState.Idle, null, "C:\\x", _now.AddMinutes(-2).Ticks, Ended: true), HookWriter.Json));

		Assert.Empty(SessionWatcher.Read(temp.Path, _now, AgentCatalog.Hooks));
		Assert.True(File.Exists(temp.File("fresh.json")));
		Assert.False(File.Exists(temp.File("old.json")));
	}

	[Fact]
	public void ReadsBranchFromParentRepository()
	{
		using var temp = new TempDirectory();
		Directory.CreateDirectory(temp.File(".git"));
		File.WriteAllText(Path.Combine(temp.File(".git"), "HEAD"), "ref: refs/heads/feature/x\n");
		var nested = Directory.CreateDirectory(temp.File("src")).FullName;

		Assert.Equal("feature/x", SessionWatcher.Branch(nested));
	}

	[Fact]
	public void ReadsBranchOfWorktree()
	{
		using var temp = new TempDirectory();
		var gitDirectory = Directory.CreateDirectory(temp.File("real-git")).FullName;
		File.WriteAllText(Path.Combine(gitDirectory, "HEAD"), "ref: refs/heads/wt\n");
		var tree = Directory.CreateDirectory(temp.File("tree")).FullName;
		File.WriteAllText(Path.Combine(tree, ".git"), $"gitdir: {gitDirectory}\n");

		Assert.Equal("wt", SessionWatcher.Branch(tree));
	}

	[Fact]
	public void DetachedHeadShowsShortHash()
	{
		using var temp = new TempDirectory();
		Directory.CreateDirectory(temp.File(".git"));
		File.WriteAllText(Path.Combine(temp.File(".git"), "HEAD"), "0123456789abcdef\n");

		Assert.Equal("0123456", SessionWatcher.Branch(temp.Path));
	}

	[Fact]
	public void NoBranchForEmptyCwd() => Assert.Null(SessionWatcher.Branch(""));

	[Fact]
	public void SkipsNetworkFolders() // недоступный сервер подвесил бы трей: ветку читаем раз в секунду в потоке окна
	{
		// Имя каждый раз новое: Windows запоминает, что сервера нет, и повторная проверка уже быстрая.
		var cwd = $@"\\av{Guid.NewGuid().ToString("N")[..12]}\share\repo";
		var watch = System.Diagnostics.Stopwatch.StartNew();

		Assert.Null(SessionWatcher.Branch(cwd));
		Assert.True(watch.ElapsedMilliseconds < 300, $"took {watch.ElapsedMilliseconds} ms");
	}
}
