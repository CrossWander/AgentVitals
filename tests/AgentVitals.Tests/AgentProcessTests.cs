using System;
using System.Collections.Generic;
using System.Diagnostics;
using Xunit;

namespace AgentVitals.Tests;

public class AgentProcessTests
{
	[Theory]
	[InlineData("claude.exe")]
	[InlineData("CLAUDE.EXE")]
	[InlineData("node.exe")] // Claude Code, поставленный через npm
	public void FindsClaudeAboveTheShell(string name)
	{
		var processes = new Dictionary<int, (int Parent, string Name)>
		{
			[10] = (5, "AgentVitals.exe"),
			[5] = (3, "cmd.exe"),
			[3] = (1, name),
			[1] = (0, "explorer.exe"),
		};

		Assert.Equal(3, AgentProcess.Ancestor(processes, 10, HookTarget.Claude.Processes));
	}

	[Fact]
	public void NoClaudeNoPid()
	{
		var processes = new Dictionary<int, (int Parent, string Name)>
		{
			[10] = (5, "AgentVitals.exe"),
			[5] = (1, "cmd.exe"),
			[1] = (0, "explorer.exe"),
		};

		Assert.Null(AgentProcess.Ancestor(processes, 10, HookTarget.Claude.Processes));
	}

	[Fact]
	public void SurvivesALoopOfParents() // номер умершего родителя Windows отдаёт новому процессу, и цепочка замыкается
	{
		var processes = new Dictionary<int, (int Parent, string Name)>
		{
			[10] = (5, "AgentVitals.exe"),
			[5] = (10, "cmd.exe"),
		};

		Assert.Null(AgentProcess.Ancestor(processes, 10, HookTarget.Claude.Processes));
	}

	[Fact]
	public void FindsCodexAboveCmd() // Codex запускает hook через cmd.exe /C; из npm — node запускает codex.exe
	{
		var processes = new Dictionary<int, (int Parent, string Name)>
		{
			[10] = (5, "AgentVitals.exe"),
			[5] = (3, "cmd.exe"),
			[3] = (2, "codex.exe"),
			[2] = (1, "node.exe"),
			[1] = (0, "WindowsTerminal.exe"),
		};

		Assert.Equal(3, AgentProcess.Ancestor(processes, 10, HookTarget.Codex.Processes));
	}

	[Fact]
	public void RunningProcessIsAlive() =>
		Assert.True(AgentProcess.Alive(Environment.ProcessId, DateTime.UtcNow));

	[Fact]
	public void NumberTakenByALaterProcessIsDead() => // hook писал раньше, чем запустился процесс с этим номером
		Assert.False(AgentProcess.Alive(Environment.ProcessId, DateTime.UtcNow.AddYears(-1)));

	[Fact]
	public void ExitedProcessIsDead()
	{
		int pid;

		using (var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit") { CreateNoWindow = true })!)
		{
			process.WaitForExit();
			pid = process.Id;
		}

		Assert.False(AgentProcess.Alive(pid, DateTime.UtcNow));
	}
}
