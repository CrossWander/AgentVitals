using System;
using System.Collections.Generic;
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

	[Fact]
	public void TabOpensItsAgentsUsagePage() =>
		Assert.Equal("https://claude.ai/settings/usage", Views.Provider(new ProviderState(new ClaudeProvider()), DateTime.UtcNow).Dashboard);

	private static UsageDay Day(int day, long input = 0, long output = 0, decimal? cost = 0, bool unpriced = false) =>
		new(new DateOnly(2026, 10, day), input, output, 0, 0, cost, unpriced, TimeSpan.Zero);

	[Fact]
	public void CodexHasNoStatisticsBlock() =>
		Assert.Null(Views.Provider(new ProviderState(new CodexProvider()), DateTime.UtcNow).Usage);

	[Fact]
	public void ClaudeCountsUntilTheFirstRead() // первое чтение логов — секунды: не нули, а «считаю…»
	{
		var usage = Views.Provider(new ProviderState(new ClaudeProvider()), DateTime.UtcNow).Usage!;

		Assert.Equal("…", usage.Cost);
		Assert.Equal("langCounting", usage.CostNote);
	}

	[Fact]
	public void TodayIsTheLastDay()
	{
		var usage = Views.Usage([Day(6, cost: 9.80m), Day(7, cost: 12.34m)]);

		Assert.Equal("$12.34", usage.Cost);
		Assert.Equal("langCostNote", usage.CostNote);
	}

	[Fact]
	public void CostSaysWhenItIsPartial()
	{
		Assert.Equal("langCostPartial", Views.Usage([Day(6), Day(7, unpriced: true)]).CostNote);
		Assert.Equal("langCostMissing", Views.Usage([Day(6, cost: null), Day(7, cost: null)]).CostNote);
		Assert.Equal("—", Views.Usage([Day(6, cost: null), Day(7, cost: null)]).Cost);
	}

	[Fact]
	public void StackedBarsShareOneScale()
	{
		var bars = Views.Bars([Day(6, input: 10, output: 30), Day(7, input: 20, output: 20)], ChartMode.Tokens);

		Assert.Equal(22.5, bars[0].Low); // самый большой день — 40 — во всю высоту 90
		Assert.Equal(67.5, bars[0].High);
		Assert.Equal(45, bars[1].Low);
		Assert.Equal(0.6, bars[0].Opacity);
		Assert.Equal(1, bars[1].Opacity); // сегодня — ярче
	}

	[Fact]
	public void EmptyMonthDrawsNothing() => Assert.All(Views.Bars([Day(6), Day(7)], ChartMode.Cost), bar => Assert.Equal(0, bar.Low));
}
