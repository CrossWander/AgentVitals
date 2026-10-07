using System.IO;
using System.Text;
using System.Text.Json.Nodes;
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
