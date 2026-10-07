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
	public void MapsEvents(string hookEvent, string? notification, SessionState expected) =>
		Assert.Equal(expected, HookWriter.Map(hookEvent, notification));

	[Theory]
	[InlineData("Notification", "idle_prompt")]
	[InlineData("PreCompact", null)]
	[InlineData(null, null)]
	public void IgnoresOtherEvents(string? hookEvent, string? notification) =>
		Assert.Null(HookWriter.Map(hookEvent, notification));

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

			Assert.Equal(round * 100 + 7, HookWriter.Read(temp.File("s1.json"))!.At);
		}
	}

	private static JsonNode Event(string name) =>
		JsonNode.Parse($$"""{ "session_id": "s1", "hook_event_name": "{{name}}", "tool_name": "Bash", "cwd": "C:\\x" }""")!;

	[Fact]
	public void SessionEndRemovesFile()
	{
		using var temp = new TempDirectory();
		HookWriter.Apply(JsonNode.Parse("""{ "session_id": "s1", "hook_event_name": "SessionStart", "cwd": "C:\\x" }"""), temp.Path, at: 1);
		HookWriter.Apply(JsonNode.Parse("""{ "session_id": "s1", "hook_event_name": "SessionEnd" }"""), temp.Path, at: 2);

		Assert.False(File.Exists(temp.File("s1.json")));
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
