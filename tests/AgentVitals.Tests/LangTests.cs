using System;
using System.IO;
using System.Linq;
using Xunit;

namespace AgentVitals.Tests;

/// <summary>Строки, которые проверить можно только в самих словарях: в тестах Localization отдаёт ключи.</summary>
public class LangTests
{
	[Fact]
	public void NoAgentsHintNamesEveryAgent() // только Copilot, а входа git нет: подсказка не должна звать ставить Claude
	{
		var files = Directory.GetFiles(Path.Combine(Root(), "Lang"), "*.axaml");
		Assert.Equal(3, files.Length);

		foreach (var file in files)
		{
			var line = File.ReadLines(file).Single(line => line.Contains("x:Key=\"langNoAgents\"", StringComparison.Ordinal));

			Assert.Contains("Copilot", line, StringComparison.Ordinal);
			Assert.Contains("Cursor", line, StringComparison.Ordinal);
		}
	}

	private static string Root()
	{
		var folder = new DirectoryInfo(AppContext.BaseDirectory);

		while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "AgentVitals.slnx")))
		{
			folder = folder.Parent;
		}

		return folder?.FullName ?? throw new DirectoryNotFoundException("AgentVitals.slnx");
	}
}
