using Xunit;

namespace AgentVitals.Tests;

public class AutostartTests
{
	[Fact]
	public void MovedExeIsNotAutostarted() // запись в Run осталась от старой папки — переключатель не должен врать
	{
		Assert.False(Autostart.Launches(@"""C:\Old\AgentVitals.exe""", @"C:\New\AgentVitals.exe"));
	}

	[Fact]
	public void SameExeIsAutostarted()
	{
		Assert.True(Autostart.Launches(@"""C:\Tools\AgentVitals.exe""", @"c:\tools\agentvitals.exe"));
	}

	[Fact]
	public void MissingValueIsNotAutostarted()
	{
		Assert.False(Autostart.Launches(null, @"C:\Tools\AgentVitals.exe"));
	}
}
