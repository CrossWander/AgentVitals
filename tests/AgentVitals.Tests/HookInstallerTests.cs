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

	[Fact]
	public void InstallKeepsForeignSettingsAndHooks()
	{
		using var temp = new TempDirectory();
		var settings = temp.File("settings.json");
		File.WriteAllText(settings, _foreign);

		HookInstaller.Install(settings, _exe);

		var root = HookInstaller.Load(settings);
		Assert.Equal("opus", (string?)root["model"]);

		var preToolUse = root["hooks"]!["PreToolUse"]!.AsArray();
		Assert.Equal(2, preToolUse.Count);
		Assert.Equal("lint.sh", (string?)preToolUse[0]!["hooks"]![0]!["command"]);
		Assert.Equal(HookLink.On, HookInstaller.State(settings, _exe));
		Assert.True(File.Exists(settings + ".agentvitals.bak"));
	}

	[Fact]
	public void WritesExecFormWithAsync()
	{
		using var temp = new TempDirectory();
		var settings = temp.File("settings.json");

		HookInstaller.Install(settings, _exe);

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

		HookInstaller.Install(settings, _exe);

		Assert.Contains("Мои программы", File.ReadAllText(settings));
	}

	[Fact]
	public void InstallTwiceDoesNotDuplicate()
	{
		using var temp = new TempDirectory();
		var settings = temp.File("settings.json");
		File.WriteAllText(settings, _foreign);

		HookInstaller.Install(settings, _exe);
		var once = File.ReadAllText(settings);
		HookInstaller.Install(settings, _exe);

		Assert.Equal(once, File.ReadAllText(settings));
	}

	[Fact]
	public void UninstallRestoresOriginalStructure()
	{
		using var temp = new TempDirectory();
		var settings = temp.File("settings.json");
		File.WriteAllText(settings, _foreign);
		var original = HookInstaller.Load(settings);

		HookInstaller.Install(settings, _exe);
		HookInstaller.Uninstall(settings);

		Assert.True(JsonNode.DeepEquals(original, HookInstaller.Load(settings)));
		Assert.Equal(HookLink.Off, HookInstaller.State(settings, _exe));
	}

	[Fact]
	public void MovedExeIsReported()
	{
		using var temp = new TempDirectory();
		var settings = temp.File("settings.json");

		HookInstaller.Install(settings, _exe);

		Assert.Equal(HookLink.Moved, HookInstaller.State(settings, @"D:\elsewhere\AgentVitals.exe"));
	}

	[Fact]
	public void BrokenFileIsLeftAlone()
	{
		using var temp = new TempDirectory();
		var settings = temp.File("settings.json");
		File.WriteAllText(settings, "{ broken");

		Assert.ThrowsAny<JsonException>(() => HookInstaller.Install(settings, _exe));
		Assert.Equal("{ broken", File.ReadAllText(settings));
	}

	[Fact]
	public void MissingFileIsOffAndUninstallIsQuiet()
	{
		using var temp = new TempDirectory();
		var settings = temp.File("settings.json");

		Assert.Equal(HookLink.Off, HookInstaller.State(settings, _exe));
		HookInstaller.Uninstall(settings);
		Assert.False(File.Exists(settings));
	}
}
