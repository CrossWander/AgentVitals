using System;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace AgentVitals.Tests;

public class HookWriterTests
{
	[Theory]
	[InlineData("SessionStart", null, SessionState.Idle)]
	[InlineData("UserPromptSubmit", null, SessionState.Working)]
	[InlineData("PreToolUse", null, SessionState.Working)]
	[InlineData("PostToolUse", null, SessionState.Working)]
	[InlineData("PostToolUseFailure", null, SessionState.Working)]
	[InlineData("PermissionDenied", null, SessionState.Working)]
	[InlineData("PermissionRequest", null, SessionState.Waiting)]
	[InlineData("Notification", "permission_prompt", SessionState.Waiting)]
	[InlineData("Stop", null, SessionState.Idle)]
	[InlineData("StopFailure", null, SessionState.Idle)]
	[InlineData("Interrupt", null, SessionState.Idle)] // Esc в Codex
	public void MapsEvents(string hookEvent, string? notification, SessionState expected) =>
		Assert.Equal(expected, HookWriter.Map(hookEvent, notification));

	[Theory]
	[InlineData("Notification", "idle_prompt")]
	[InlineData("PreCompact", null)]
	[InlineData(null, null)]
	public void IgnoresOtherEvents(string? hookEvent, string? notification) =>
		Assert.Null(HookWriter.Map(hookEvent, notification));

	private static void Codex(TempDirectory temp, string hookEvent, DateTime at) =>
		HookWriter.Apply(new JsonObject { ["session_id"] = "c1", ["hook_event_name"] = hookEvent, ["cwd"] = @"C:\x" }, temp.Path, at.Ticks, agent: "Codex");

	[Fact]
	public void InterruptEndsTheTurnWithoutADuration() // Esc в Codex — не «закончил»
	{
		using var temp = new TempDirectory();
		var now = DateTime.UtcNow;
		Codex(temp, "UserPromptSubmit", now.AddMinutes(-10));
		Codex(temp, "Interrupt", now.AddSeconds(-1));

		var session = Assert.Single(SessionWatcher.Read(temp.Path, now));
		Assert.Equal(SessionState.Idle, session.State);
		Assert.Null(session.TurnDuration);
		Assert.Equal("Codex", session.Agent);
	}

	private static void Prompt(TempDirectory temp, string text, DateTime at) =>
		HookWriter.Apply(new JsonObject { ["session_id"] = "s1", ["hook_event_name"] = "UserPromptSubmit", ["cwd"] = @"C:\x", ["prompt"] = text }, temp.Path, at.Ticks);

	[Fact]
	public void SessionKeepsItsFirstPrompt() // по нему различают сессии в одной папке; «да» и «продолжай» потом — не название
	{
		using var temp = new TempDirectory();
		var now = DateTime.UtcNow;
		Prompt(temp, "  Почини\n\nтесты   в pdu  ", now.AddMinutes(-3));
		HookWriter.Apply(new JsonObject { ["session_id"] = "s1", ["hook_event_name"] = "Stop", ["cwd"] = @"C:\x" }, temp.Path, now.AddMinutes(-2).Ticks);
		Prompt(temp, "да", now.AddMinutes(-1));

		Assert.Equal("Почини тесты в pdu", Assert.Single(SessionWatcher.Read(temp.Path, now)).Prompt);
	}

	[Fact]
	public void LongPromptIsCut() // вставленный лог в тысячу строк не нужен ни строке, ни подсказке
	{
		using var temp = new TempDirectory();
		Prompt(temp, new string('a', 5000), DateTime.UtcNow);

		var prompt = HookWriter.Read(temp.File("s1.json"))!.Prompt!;
		Assert.Equal((120, '…'), (prompt.Length, prompt[^1]));
	}

	[Fact]
	public void CodexKeepsNoTranscript() // прерывание Codex сообщает сам — журнал читать незачем
	{
		using var temp = new TempDirectory();
		var codex = CodexHooks.Target;
		HookWriter.Apply(new JsonObject { ["session_id"] = "c1", ["hook_event_name"] = "PreToolUse", ["tool_name"] = "Bash", ["cwd"] = @"C:\x", ["transcript_path"] = @"C:\x\rollout.jsonl" }, temp.Path, 100, pid: 7, agent: codex.Agent, transcript: codex.Transcript);

		var record = HookWriter.Read(temp.File("c1.json"))!;
		Assert.Equal(("Codex", null, 7), (record.Agent, record.Transcript, record.Pid));
	}

	[Fact]
	public void ClaudeKeepsItsTranscript() // о прерывании Esc hooks Claude Code молчат — его видно только в журнале
	{
		using var temp = new TempDirectory();
		var claude = ClaudeHooks.Target;
		HookWriter.Apply(new JsonObject { ["session_id"] = "c1", ["hook_event_name"] = "PreToolUse", ["cwd"] = @"C:\x", ["transcript_path"] = @"C:\x\c1.jsonl" }, temp.Path, 100, agent: claude.Agent, transcript: claude.Transcript);

		Assert.Equal(@"C:\x\c1.jsonl", HookWriter.Read(temp.File("c1.json"))!.Transcript);
	}

	[Fact]
	public void FileWithoutAgentIsClaude() // записан до появления Codex
	{
		using var temp = new TempDirectory();
		var now = DateTime.UtcNow;
		File.WriteAllText(temp.File("s1.json"), $$"""{ "state": "working", "cwd": "C:\\x", "utc": {{now.AddSeconds(-1).Ticks}} }""");

		Assert.Equal("Claude", Assert.Single(SessionWatcher.Read(temp.Path, now)).Agent);
	}

	[Fact]
	public void KeepsCyrillicCwd()
	{
		using var temp = new TempDirectory();
		var payload = """{ "session_id": "s1", "hook_event_name": "PreToolUse", "tool_name": "Edit", "cwd": "C:\\Проекты\\мой репо" }""";

		HookWriter.Run(new MemoryStream(Encoding.UTF8.GetBytes(payload)), temp.Path, at: 100);

		var session = HookWriter.Read(temp.File("s1.json"))!;
		Assert.Equal(SessionState.Working, session.State);
		Assert.Equal("Edit", session.Tool);
		Assert.Equal(@"C:\Проекты\мой репо", session.Cwd);
	}

	[Fact]
	public void RecordsClaudeProcess() // по нему трей узнаёт, что терминал закрыли без SessionEnd
	{
		using var temp = new TempDirectory();

		HookWriter.Apply(new JsonObject { ["session_id"] = "s1", ["hook_event_name"] = "PreToolUse", ["cwd"] = @"C:\x" }, temp.Path, 100, pid: 42);

		Assert.Equal(42, HookWriter.Read(temp.File("s1.json"))!.Pid);
	}

	// Секунды, а не тики: события ближе 50 мс к «ждёт» считаются одним мгновением и отбрасываются.
	private static void Hook(TempDirectory temp, string hookEvent, long second) =>
		HookWriter.Apply(new JsonObject { ["session_id"] = "s1", ["hook_event_name"] = hookEvent, ["cwd"] = @"C:\x" }, temp.Path, second * TimeSpan.TicksPerSecond);

	[Fact]
	public void TurnStartsAtThePromptAndOutlivesItsTools() // по нему таймер хода, а после Stop — сколько ход занял
	{
		using var temp = new TempDirectory();

		Hook(temp, "UserPromptSubmit", 100);
		Hook(temp, "PreToolUse", 200);
		Hook(temp, "PermissionRequest", 300);
		Hook(temp, "Stop", 400);

		Assert.Equal(100 * TimeSpan.TicksPerSecond, HookWriter.Read(temp.File("s1.json"))!.Turn);

		Hook(temp, "SessionStart", 500); // /clear — новый разговор, прежнего хода нет
		Assert.Null(HookWriter.Read(temp.File("s1.json"))!.Turn);
	}

	[Fact]
	public void TurnWithoutPromptStartsAtItsFirstEvent() // сессия старше hooks или ход без запроса
	{
		using var temp = new TempDirectory();

		Hook(temp, "Stop", 100);
		Hook(temp, "PreToolUse", 200);
		Hook(temp, "PostToolUse", 300);

		Assert.Equal(200 * TimeSpan.TicksPerSecond, HookWriter.Read(temp.File("s1.json"))!.Turn);
	}

	[Fact]
	public void OlderEventDoesNotOverwriteNewer()
	{
		using var temp = new TempDirectory();
		HookWriter.Apply(JsonNode.Parse("""{ "session_id": "s1", "hook_event_name": "Stop", "cwd": "C:\\x" }"""), temp.Path, at: 200);
		HookWriter.Apply(JsonNode.Parse("""{ "session_id": "s1", "hook_event_name": "PreToolUse", "tool_name": "Bash", "cwd": "C:\\x" }"""), temp.Path, at: 100);

		Assert.Equal(SessionState.Idle, HookWriter.Read(temp.File("s1.json"))!.State);
	}

	[Fact]
	public void WaitingSurvivesWorkingFromSameMoment() // PreToolUse и PermissionRequest стартуют почти разом
	{
		using var temp = new TempDirectory();
		HookWriter.Apply(Event("PermissionRequest"), temp.Path, at: 1_000_000);
		HookWriter.Apply(Event("PreToolUse"), temp.Path, at: 1_000_000);
		HookWriter.Apply(Event("PreToolUse"), temp.Path, at: 1_000_000 + TimeSpan.TicksPerMillisecond * 20);

		Assert.Equal(SessionState.Waiting, HookWriter.Read(temp.File("s1.json"))!.State);
	}

	[Fact]
	public void WorkingReplacesWaitingOnceAnswered()
	{
		using var temp = new TempDirectory();
		HookWriter.Apply(Event("PermissionRequest"), temp.Path, at: 1_000_000);
		HookWriter.Apply(Event("PostToolUse"), temp.Path, at: 1_000_000 + TimeSpan.TicksPerSecond);

		Assert.Equal(SessionState.Working, HookWriter.Read(temp.File("s1.json"))!.State);
	}

	[Fact]
	public void SimultaneousHooksKeepNewest()
	{
		using var temp = new TempDirectory();

		for (var round = 1; round <= 20; round++)
		{
			Parallel.For(0, 8, index => HookWriter.Apply(Event("PreToolUse"), temp.Path, at: round * 100 + index));

			Assert.Equal(round * 100 + 7, HookWriter.Read(temp.File("s1.json"))!.Utc);
		}
	}

	private static JsonNode Event(string name) =>
		JsonNode.Parse($$"""{ "session_id": "s1", "hook_event_name": "{{name}}", "tool_name": "Bash", "cwd": "C:\\x" }""")!;

	[Fact]
	public void SessionEndHidesSession()
	{
		using var temp = new TempDirectory();
		var now = DateTime.UtcNow;
		HookWriter.Apply(Event("SessionStart"), temp.Path, at: now.AddSeconds(-2).Ticks);
		HookWriter.Apply(Event("SessionEnd"), temp.Path, at: now.AddSeconds(-1).Ticks);

		Assert.Empty(SessionWatcher.Read(temp.Path, now));
	}

	[Fact]
	public void EventFinishingAfterSessionEndDoesNotRevive() // async PostToolUse, запущенный перед выходом
	{
		using var temp = new TempDirectory();
		var now = DateTime.UtcNow;
		HookWriter.Apply(Event("SessionEnd"), temp.Path, at: now.AddSeconds(-1).Ticks);
		HookWriter.Apply(Event("PostToolUse"), temp.Path, at: now.AddSeconds(-2).Ticks);

		Assert.Empty(SessionWatcher.Read(temp.Path, now));
	}

	[Fact]
	public void ResumedSessionComesBack() // claude --resume продолжает ту же сессию
	{
		using var temp = new TempDirectory();
		var now = DateTime.UtcNow;
		HookWriter.Apply(Event("SessionEnd"), temp.Path, at: now.AddMinutes(-5).Ticks);
		HookWriter.Apply(Event("SessionStart"), temp.Path, at: now.AddSeconds(-1).Ticks);

		Assert.Single(SessionWatcher.Read(temp.Path, now));
	}

	[Theory]
	[InlineData("not json")]
	[InlineData("""{ "session_id": "..\\..\\evil", "hook_event_name": "Stop" }""")]
	[InlineData("""{ "hook_event_name": "Stop" }""")]
	[InlineData("")]
	public void IgnoresGarbage(string payload)
	{
		using var temp = new TempDirectory();

		HookWriter.Run(new MemoryStream(Encoding.UTF8.GetBytes(payload)), temp.Path, at: 1);

		Assert.Empty(Directory.GetFiles(temp.Path, "*", SearchOption.AllDirectories));
	}
}
