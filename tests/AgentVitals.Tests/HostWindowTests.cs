using System.Collections.Generic;
using Xunit;

namespace AgentVitals.Tests;

public class HostWindowTests
{
	// Как на деле в VS Code: claude.exe → хост расширения своего окна → главный Code.exe со всеми окнами.
	private static readonly List<(string, IReadOnlyList<(nint, string)>)> _code =
	[
		("claude.exe", []),
		("Code.exe", []),
		("Code.exe", [(1, "Toast.axaml - HyperXAudioGuard - Visual Studio Code"), (2, "Задача - ProjectsComfy - Visual Studio Code")]),
		("explorer.exe", [(9, "Program Manager")]),
	];

	[Theory]
	[InlineData("ProjectsComfy", 2)]
	[InlineData("hyperxaudioguard", 1)] // регистр в имени папки и в заголовке бывает разный
	public void PicksTheWindowOfTheSessionFolder(string folder, int window) =>
		Assert.Equal(window, HostWindow.Pick(_code, folder));

	[Fact]
	public void SessionInASubfolderFindsItsProjectWindow() => // заголовок VS Code называет корень проекта, а не подпапку
		Assert.Equal(2, HostWindow.Pick(_code, @"C:\Work\ProjectsComfy\posscheduler"));

	[Fact]
	public void NoTitleNamesTheFolderTakesTheTopWindow() => // заголовок Windows Terminal — имя вкладки, а не папка
		Assert.Equal(5, HostWindow.Pick([("claude.exe", []), ("pwsh.exe", []), ("WindowsTerminal.exe", [(5, "claude"), (6, "pwsh")])], "repo"));

	[Fact]
	public void NearestProcessWithWindowsWins() // консольное окно у самой оболочки, выше — чужие окна
		=> Assert.Equal(3, HostWindow.Pick([("claude.exe", []), ("cmd.exe", [(3, "C:\\WINDOWS\\system32\\cmd.exe")]), ("Code.exe", [(4, "repo - Visual Studio Code")])], "repo"));

	[Fact]
	public void StopsAtTheDesktopShell() => // окна explorer — рабочий стол и папки, сессии там нет
		Assert.Null(HostWindow.Pick([("claude.exe", []), ("cmd.exe", []), ("explorer.exe", [(9, "repo")])], "repo"));

	[Fact]
	public void NoWindowsNoHost() => Assert.Null(HostWindow.Pick([("claude.exe", []), ("services.exe", [])], "repo"));
}
