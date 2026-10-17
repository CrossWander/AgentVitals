using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace AgentVitals.Tests;

/// <summary>README на трёх языках: переводы идут за английским оригиналом, а ссылки ведут туда, где что-то есть.</summary>
public class ReadmeTests
{
	private static readonly string[] _files = ["README.md", "README.ru.md", "README.uk.md"];

	[Fact]
	public void TranslationsKeepTheOriginalsShape() // правка одного README должна дойти до всех трёх
	{
		var original = Shape(Read(_files[0]));

		foreach (var file in _files.Skip(1))
		{
			Assert.Equal(original, Shape(Read(file)));
		}
	}

	[Fact]
	public void LinksLeadSomewhere() // app.png переехал в src\AgentVitals, а README ещё звал его из корня
	{
		foreach (var file in _files)
		{
			var text = Read(file);
			var anchors = Headings(text).Select(Slug).ToHashSet();

			foreach (Match link in Regex.Matches(text, @"\]\(([^)\s]+)\)|src=""([^""]+)"""))
			{
				var target = link.Groups[1].Success ? link.Groups[1].Value : link.Groups[2].Value;

				if (target.StartsWith('#'))
				{
					Assert.True(anchors.Contains(target[1..]), $"{file}: no section {target}");
				}
				else if (!target.StartsWith("https://", StringComparison.Ordinal))
				{
					Assert.True(File.Exists(Path.Combine(LangTests.Root(), target)), $"{file}: no file {target}");
				}
			}
		}
	}

	private static string Read(string file) => File.ReadAllText(Path.Combine(LangTests.Root(), file)).Replace("\r\n", "\n");

	/// <summary>То, что перевод не меняет: число разделов, картинки и код в тексте.</summary>
	private static string Shape(string text) =>
		$"{Headings(text).Count()} sections\n"
		+ string.Join("\n", Regex.Matches(text, @"src=""([^""]+)""").Select(match => match.Groups[1].Value)) + "\n"
		+ string.Join("\n", Regex.Matches(text, "`[^`\n]+`").Select(match => match.Value).Order(StringComparer.Ordinal));

	private static IEnumerable<string> Headings(string text) =>
		Regex.Matches(text, "^## (.+)$", RegexOptions.Multiline).Select(match => match.Groups[1].Value.Trim());

	/// <summary>Якорь раздела, как его делает GitHub: строчные буквы, без знаков, пробелы — дефисы.</summary>
	private static string Slug(string heading) =>
		Regex.Replace(heading.ToLowerInvariant(), @"[^\p{L}\p{Nd} _-]", "").Replace(' ', '-');
}
