using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace AgentVitals.Tests;

/// <summary>Строки, которые проверить можно только в самих словарях: в тестах Localization отдаёт ключи.</summary>
public class LangTests
{
	[Fact]
	public void NoAgentsHintNamesEveryAgent() // только Copilot, а входа git нет: подсказка не должна звать ставить Claude
	{
		var files = Directory.GetFiles(Lang(), "*.axaml");
		Assert.Equal(3, files.Length);

		foreach (var file in files)
		{
			var line = File.ReadLines(file).Single(line => line.Contains("x:Key=\"langNoAgents\"", StringComparison.Ordinal));

			Assert.Contains("Copilot", line, StringComparison.Ordinal);
			Assert.Contains("Cursor", line, StringComparison.Ordinal);
		}
	}

	[Fact]
	public void EverySignInProblemHasText() // у агента своей строки может не быть — тогда общая, и она должна быть
	{
		foreach (var file in Directory.GetFiles(Lang(), "*.axaml"))
		{
			var text = File.ReadAllText(file);

			foreach (var problem in Enum.GetValues<SignInProblem>())
			{
				Assert.Contains($"x:Key=\"langError{problem}\"", text, StringComparison.Ordinal);
			}
		}
	}

	[Fact]
	public void EveryKeyIsInEveryLanguage() // забытый перевод показал бы человеку «langPercentLeft»
	{
		var keys = Directory.GetFiles(Lang(), "*.axaml")
			.Select(file => Regex.Matches(File.ReadAllText(file), "x:Key=\"(\\w+)\"").Select(match => match.Groups[1].Value).Order(StringComparer.Ordinal).ToList())
			.ToList();

		Assert.All(keys, list => Assert.Equal(keys[0], list));
	}

	private static string Lang() => Path.Combine(Root(), "src", "AgentVitals", "Lang");

	internal static string Root()
	{
		var folder = new DirectoryInfo(AppContext.BaseDirectory);

		while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "AgentVitals.slnx")))
		{
			folder = folder.Parent;
		}

		return folder?.FullName ?? throw new DirectoryNotFoundException("AgentVitals.slnx");
	}
}
