using System;
using System.IO;
using System.Threading;
using Xunit;

namespace AgentVitals.Tests;

public class FileNoticeTests
{
	private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

	[Fact]
	public void RewrittenFileIsNoticed() // Claude Code обновил вход после сна — не ждать минутного тика
	{
		using var temp = new TempDirectory();
		var file = temp.File(".credentials.json");
		File.WriteAllText(file, "{}");
		using var changed = new ManualResetEventSlim();
		using var notice = new FileNotice(file, changed.Set);

		File.WriteAllText(file, """{"claudeAiOauth":{}}""");

		Assert.True(changed.Wait(_wait));
	}

	[Fact]
	public void ReplacedFileIsNoticed() // запись через временный файл и переименование
	{
		using var temp = new TempDirectory();
		var file = temp.File(".credentials.json");
		File.WriteAllText(file, "{}");
		using var changed = new ManualResetEventSlim();
		using var notice = new FileNotice(file, changed.Set);

		File.WriteAllText(file + ".tmp", """{"claudeAiOauth":{}}""");
		File.Move(file + ".tmp", file, overwrite: true);

		Assert.True(changed.Wait(_wait));
	}

	[Fact]
	public void OtherFilesAreIgnored()
	{
		using var temp = new TempDirectory();
		var file = temp.File(".credentials.json");
		File.WriteAllText(file, "{}");
		using var changed = new ManualResetEventSlim();
		using var notice = new FileNotice(file, changed.Set);

		File.WriteAllText(temp.File("settings.json"), "{}");

		Assert.False(changed.Wait(TimeSpan.FromSeconds(2)));
	}

	[Fact]
	public void MissingFolderIsNoError() // агент не установлен
	{
		using var temp = new TempDirectory();

		using var notice = new FileNotice(Path.Combine(temp.Path, "missing", ".credentials.json"), () => { });
	}
}
