using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace AgentVitals.Tests;

public class HookInstallerTests
{
	private const string _exe = @"C:\Program Files\Мои программы\AgentVitals.exe";

	private const string _foreign = """
		{
		  // свои настройки пользователя
		  "model": "opus",
		  "hooks": {
		    "PreToolUse": [ { "matcher": "Bash", "hooks": [ { "type": "command", "command": "lint.sh" } ] } ]
		  },
		}
		""";

	private static HookTarget Claude(string file) => ClaudeHooks.Target with { File = file };

	private static HookTarget Codex(string file) => CodexHooks.Target with { File = file };

	[Theory]
	[InlineData("codex", "Codex")]
	[InlineData(null, "Claude")] // так Claude Code звал hook до Codex — подключённые раньше hooks работают как были
	[InlineData("", "Claude")]
	public void HookArgumentFindsItsAgent(string? key, string agent) => Assert.Equal(agent, AgentCatalog.Hook(key).Agent);

	[Fact]
	public void ProvidersCarryTheirHooks() // сессии — часть контракта агента, как расход и статус
	{
		Assert.Equal(["Claude", "Codex"], AgentCatalog.Hooks.Select(target => target.Agent));
		Assert.All(AgentCatalog.All(), agent => Assert.True(agent.Hooks is null || agent.Hooks.Agent == agent.Name));
	}

	[Fact]
	public void CodexGetsAQuotedCommandLine() // Codex запускает строку через cmd.exe /C — путь в кавычках
	{
		using var temp = new TempDirectory();
		var hooks = temp.File("hooks.json");

		HookInstaller.Install(Codex(hooks), _exe);

		var root = HookInstaller.Load(hooks);
		var stop = root["hooks"]!["Stop"]![0]!["hooks"]![0]!;
		Assert.Equal($"\"{_exe}\" hook codex", (string?)stop["command"]);
		Assert.True((bool?)stop["async"]);

		var interrupt = root["hooks"]!["Interrupt"]![0]!["hooks"]![0]!;
		Assert.Null(interrupt["async"]);
		Assert.Equal(3, (int?)interrupt["timeout"]);

		Assert.Null(root["hooks"]!["SubagentStop"]); // у субагента session_id родителя
		Assert.Equal(HookLink.On, HookInstaller.State(Codex(hooks), _exe)); // путь с пробелами и кириллицей узнаётся
	}

	[Fact]
	public void CodexKeepsForeignHooksAndNoticesTheMove()
	{
		using var temp = new TempDirectory();
		var hooks = temp.File("hooks.json");
		File.WriteAllText(hooks, """{ "description": "мои", "hooks": { "Stop": [ { "hooks": [ { "type": "command", "command": "python3 notify.py" } ] } ] } }""");
		var original = HookInstaller.Load(hooks);

		HookInstaller.Install(Codex(hooks), _exe);
		Assert.Equal(HookLink.Moved, HookInstaller.State(Codex(hooks), @"D:\elsewhere\AgentVitals.exe"));

		HookInstaller.Uninstall(Codex(hooks));
		Assert.True(JsonNode.DeepEquals(original, HookInstaller.Load(hooks)));
	}

	[Fact]
	public void ClaudeAndCodexEntriesAreToldApart() // одна программа, две формы записи
	{
		var claude = new JsonObject { ["type"] = "command", ["command"] = _exe, ["args"] = new JsonArray("hook") };
		var codex = new JsonObject { ["type"] = "command", ["command"] = $"\"{_exe}\" hook codex" };

		Assert.Null(CodexHooks.Target.Exe(claude));
		Assert.Null(ClaudeHooks.Target.Exe(codex));
		Assert.Equal(_exe, CodexHooks.Target.Exe(codex));
	}

	[Fact]
	public void InstallKeepsForeignSettingsAndHooks()
	{
		using var temp = new TempDirectory();
		var settings = temp.File("settings.json");
		File.WriteAllText(settings, _foreign);

		HookInstaller.Install(Claude(settings), _exe);

		var root = HookInstaller.Load(settings);
		Assert.Equal("opus", (string?)root["model"]);

		var preToolUse = root["hooks"]!["PreToolUse"]!.AsArray();
		Assert.Equal(2, preToolUse.Count);
		Assert.Equal("lint.sh", (string?)preToolUse[0]!["hooks"]![0]!["command"]);
		Assert.Equal(HookLink.On, HookInstaller.State(Claude(settings), _exe));
		Assert.True(File.Exists(settings + ".agentvitals.bak"));
	}

	[Fact]
	public void WritesExecFormWithAsync()
	{
		using var temp = new TempDirectory();
		var settings = temp.File("settings.json");

		HookInstaller.Install(Claude(settings), _exe);

		var root = HookInstaller.Load(settings);
		var stop = root["hooks"]!["Stop"]![0]!["hooks"]![0]!;
		Assert.Equal("command", (string?)stop["type"]);
		Assert.Equal(_exe, (string?)stop["command"]);
		Assert.Equal(["hook"], stop["args"]!.AsArray().Select(argument => (string?)argument));
		Assert.True((bool?)stop["async"]);

		var end = root["hooks"]!["SessionEnd"]![0]!["hooks"]![0]!;
		Assert.Null(end["async"]);

		Assert.Equal("permission_prompt", (string?)root["hooks"]!["Notification"]![0]!["matcher"]);
	}

	[Fact]
	public void KeepsPathWithSpacesAndCyrillic()
	{
		using var temp = new TempDirectory();
		var settings = temp.File("settings.json");

		HookInstaller.Install(Claude(settings), _exe);

		Assert.Contains("Мои программы", File.ReadAllText(settings));
	}

	[Fact]
	public void InstallTwiceDoesNotDuplicate()
	{
		using var temp = new TempDirectory();
		var settings = temp.File("settings.json");
		File.WriteAllText(settings, _foreign);

		HookInstaller.Install(Claude(settings), _exe);
		var once = File.ReadAllText(settings);
		HookInstaller.Install(Claude(settings), _exe);

		Assert.Equal(once, File.ReadAllText(settings));
	}

	[Fact]
	public void UninstallRestoresOriginalStructure()
	{
		using var temp = new TempDirectory();
		var settings = temp.File("settings.json");
		File.WriteAllText(settings, _foreign);
		var original = HookInstaller.Load(settings);

		HookInstaller.Install(Claude(settings), _exe);
		HookInstaller.Uninstall(Claude(settings));

		Assert.True(JsonNode.DeepEquals(original, HookInstaller.Load(settings)));
		Assert.Equal(HookLink.Off, HookInstaller.State(Claude(settings), _exe));
	}

	[Fact]
	public void UninstallKeepsContainersThatWereEmptyBefore()
	{
		using var temp = new TempDirectory();
		var settings = temp.File("settings.json");
		File.WriteAllText(settings, """{ "hooks": { "PreCompact": [] } }"""); // в PreCompact мы не пишем
		var original = HookInstaller.Load(settings);

		HookInstaller.Install(Claude(settings), _exe);
		HookInstaller.Uninstall(Claude(settings));

		Assert.True(JsonNode.DeepEquals(original, HookInstaller.Load(settings)));
	}

	[Fact]
	public void UninstallKeepsEmptyHooksObject()
	{
		using var temp = new TempDirectory();
		var settings = temp.File("settings.json");
		File.WriteAllText(settings, """{ "model": "opus", "hooks": {} }""");

		HookInstaller.Uninstall(Claude(settings));

		Assert.NotNull(HookInstaller.Load(settings)["hooks"]);
	}

	[Fact]
	public void BackupHoldsTheFileBeforeTheLatestChange() // комментарии при перезаписи теряются — их хранит копия
	{
		using var temp = new TempDirectory();
		var settings = temp.File("settings.json");
		File.WriteAllText(settings, _foreign);
		HookInstaller.Install(Claude(settings), _exe);

		var edited = File.ReadAllText(settings).Replace("\"opus\"", "\"sonnet\" /* поменял руками */");
		File.WriteAllText(settings, edited);
		HookInstaller.Uninstall(Claude(settings));

		Assert.Equal(edited, File.ReadAllText(settings + ".agentvitals.bak"));
	}

	[Fact]
	public void MovedExeIsReported()
	{
		using var temp = new TempDirectory();
		var settings = temp.File("settings.json");

		HookInstaller.Install(Claude(settings), _exe);

		Assert.Equal(HookLink.Moved, HookInstaller.State(Claude(settings), @"D:\elsewhere\AgentVitals.exe"));
	}

	[Fact]
	public void BrokenFileIsLeftAlone()
	{
		using var temp = new TempDirectory();
		var settings = temp.File("settings.json");
		File.WriteAllText(settings, "{ broken");

		Assert.ThrowsAny<JsonException>(() => HookInstaller.Install(Claude(settings), _exe));
		Assert.Equal("{ broken", File.ReadAllText(settings));
	}

	[Fact]
	public void MissingFileIsOffAndUninstallIsQuiet()
	{
		using var temp = new TempDirectory();
		var settings = temp.File("settings.json");

		Assert.Equal(HookLink.Off, HookInstaller.State(Claude(settings), _exe));
		HookInstaller.Uninstall(Claude(settings));
		Assert.False(File.Exists(settings));
	}
}
