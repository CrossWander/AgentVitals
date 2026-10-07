using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace AgentVitals.Tests;

public class SessionWatcherTests
{
	private static readonly DateTime _now = new(2026, 10, 7, 12, 0, 0);

	private static void Write(TempDirectory temp, string id, SessionState state, DateTime at, string cwd = @"C:\nowhere") =>
		File.WriteAllText(temp.File(id + ".json"), JsonSerializer.Serialize(new Session(state, null, cwd, at.Ticks), HookWriter.Json));

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

		var session = Assert.Single(SessionWatcher.Read(temp.Path, _now));

		Assert.Equal("live", session.Id);
		Assert.Equal(SessionState.Working, session.State);
		Assert.False(File.Exists(temp.File("old.json")));
	}

	[Fact]
	public void WaitingOutranksWorking()
	{
		LiveSession[] sessions =
		[
			new("a", SessionState.Idle, null, "a", null),
			new("b", SessionState.Waiting, null, "b", null),
			new("c", SessionState.Working, "Edit", "c", null),
		];

		Assert.Equal(SessionState.Waiting, SessionWatcher.Overall(sessions));
		Assert.Equal(SessionState.Idle, SessionWatcher.Overall([]));
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
}
