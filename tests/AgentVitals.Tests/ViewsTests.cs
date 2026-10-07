using System;
using Xunit;

namespace AgentVitals.Tests;

public class ViewsTests
{
	[Fact]
	public void EachTabShowsItsOwnUpdateTime() // у Codex со вчерашними данными не должно быть «только что»
	{
		var now = DateTime.UtcNow;
		var claude = new ProviderState(new ClaudeProvider()) { Updated = now };
		var codex = new ProviderState(new CodexProvider());

		Assert.Equal("langUpdatedNow", Views.Provider(claude, now).Updated);
		Assert.Equal("", Views.Provider(codex, now).Updated);
	}

	[Theory]
	[InlineData("Codex", "Codex")] // появился Gemini — выбор не съехал на соседа
	[InlineData("Gemini", "Claude")] // выбранный пропал — первый
	[InlineData(null, "Claude")]
	public void TabSelectionFollowsTheName(string? selected, string expected) =>
		Assert.Equal(expected, Popover.Keep(selected, ["Claude", "Codex"]));
}
