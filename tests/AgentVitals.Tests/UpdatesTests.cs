using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace AgentVitals.Tests;

public class UpdatesTests
{
	private static readonly Version _current = new(0, 2, 0, 0); // как у сборки: четыре числа

	private const string _url = "https://github.com/CrossWander/AgentVitals/releases/download/v0.3.0/AgentVitals.exe";

	private static readonly string _exe = $$"""{"name":"AgentVitals.exe","browser_download_url":"{{_url}}","digest":"sha256:ab12"}""";

	private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

	[Fact]
	public void NewerReleaseCarriesItsExeAndHash() =>
		Assert.Equal(
			new Release(UpdateState.Newer, new Version(0, 3, 0), _url, "sha256:ab12"),
			Updates.Read(Json($$"""{"tag_name":"v0.3.0","assets":[{"name":"AgentVitals.zip"},{{_exe}}]}"""), _current));

	[Theory]
	[InlineData("v0.2.0")] // тег из трёх чисел против 0.2.0.0 сборки — та же версия
	[InlineData("0.2.0")]
	[InlineData("v0.1.9")]
	public void SameOrOlderReleaseIsLatest(string tag) =>
		Assert.Equal(UpdateState.Latest, Updates.Read(Json($$"""{"tag_name":"{{tag}}","assets":[{{_exe}}]}"""), _current).State);

	[Fact]
	public void ReleaseWithoutTheExeHasNothingToInstall()
	{
		var release = Updates.Read(Json("""{"tag_name":"v0.3.0","assets":[{"name":"AgentVitals.zip"}]}"""), _current);

		Assert.Equal((UpdateState.Newer, null, null), (release.State, release.Download, release.Digest));
	}

	[Theory]
	[InlineData("""{"tag_name":"latest"}""")]
	[InlineData("""{"tag_name":3}""")]
	[InlineData("""{"assets":[]}""")]
	[InlineData("""[]""")]
	public void UnreadableReleaseFails(string json) => Assert.Equal(UpdateState.Failed, Updates.Read(Json(json), _current).State);

	[Fact]
	public void FileMatchesTheHashGitHubGave()
	{
		var bytes = "AgentVitals"u8.ToArray();
		var digest = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

		Assert.True(Updates.Matches(new MemoryStream(bytes), digest));
		Assert.False(Updates.Matches(new MemoryStream([.. bytes, 0]), digest));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("md5:abc")]
	public void NoHashNoInstall(string? digest) => Assert.False(Updates.Matches(new MemoryStream([1, 2, 3]), digest));

	[Fact]
	public void ScriptStartsTheProgramEvenWhenTheSwapFails() // хук держал exe все 40 попыток — поднимется старая версия
	{
		var lines = Updates.Script(@"C:\Tools\AgentVitals.exe", @"C:\Tools\AgentVitals.exe.new").Split("\r\n");

		var swap = Array.FindIndex(lines, line => line.Contains("move /y \"%fresh%\" \"%target%\""));
		var giveUp = Array.FindIndex(lines, line => line.Trim() == "del \"%fresh%\" >nul 2>&1");
		var start = Array.FindIndex(lines, line => line.Trim() == "start \"\" \"%target%\"");

		Assert.True(swap >= 0 && swap < giveUp && giveUp < start);
		Assert.Single(lines, line => line.Trim() == "start \"\" \"%target%\"");
		Assert.DoesNotContain(lines, line => line.Contains("goto") && !line.Contains("goto start"));
	}

	[Fact]
	public void ScriptKeepsPathsQuotedInUtf8() // кириллица и пробелы в пути к программе
	{
		var script = Updates.Script(@"C:\Users\Павел\Мои программы\AgentVitals.exe", @"C:\Users\Павел\Мои программы\AgentVitals.exe.new");

		Assert.Contains("chcp 65001 >nul", script);
		Assert.Contains(@"set ""target=C:\Users\Павел\Мои программы\AgentVitals.exe""", script);
		Assert.Contains(@"set ""fresh=C:\Users\Павел\Мои программы\AgentVitals.exe.new""", script);
	}

	[Fact]
	public void ScriptHasWindowsLineEndings() // с одними LF cmd путает метки и goto
	{
		var script = Updates.Script(@"C:\a.exe", @"C:\a.exe.new");

		Assert.DoesNotContain("\n", script.Replace("\r\n", ""));
	}

	[Theory]
	[InlineData(false, null, false)]
	[InlineData(true, null, true)]
	[InlineData(true, -2.0, false)]
	[InlineData(true, -25.0, true)]
	public void ChecksOnceADay(bool enabled, double? hoursAgo, bool due)
	{
		var now = new DateTime(2026, 10, 14, 19, 0, 0, DateTimeKind.Utc);

		Assert.Equal(due, Updates.Due(enabled, hoursAgo is { } hours ? now.AddHours(hours) : null, now));
	}

	[Theory]
	[InlineData(null, false)] // первый запуск — не обновление
	[InlineData("0.2.0", true)]
	[InlineData("0.3.0", false)]
	[InlineData("garbage", false)]
	public void KnowsItWasJustUpdated(string? last, bool upgraded) => Assert.Equal(upgraded, Updates.Upgraded(last, new Version(0, 3, 0)));
}
