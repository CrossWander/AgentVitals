using System;
using System.Collections.Generic;
using System.Linq;
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

	[Theory]
	[InlineData(Avalonia.Input.Key.D1, Avalonia.Input.KeyModifiers.Control, 0)]
	[InlineData(Avalonia.Input.Key.NumPad3, Avalonia.Input.KeyModifiers.Control, 2)]
	[InlineData(Avalonia.Input.Key.D2, Avalonia.Input.KeyModifiers.None, null)] // без Ctrl цифра — просто цифра
	[InlineData(Avalonia.Input.Key.D0, Avalonia.Input.KeyModifiers.Control, null)]
	public void CtrlDigitPicksTab(Avalonia.Input.Key key, Avalonia.Input.KeyModifiers modifiers, int? tab) =>
		Assert.Equal(tab, Popover.TabKey(key, modifiers));

	[Theory]
	[InlineData(Avalonia.Input.Key.F5, Avalonia.Input.KeyModifiers.None, true)]
	[InlineData(Avalonia.Input.Key.R, Avalonia.Input.KeyModifiers.Control, true)]
	[InlineData(Avalonia.Input.Key.R, Avalonia.Input.KeyModifiers.None, false)]
	public void F5OrCtrlRRefreshes(Avalonia.Input.Key key, Avalonia.Input.KeyModifiers modifiers, bool refresh) =>
		Assert.Equal(refresh, Popover.RefreshKey(key, modifiers));

	[Fact]
	public void TabOpensItsAgentsUsagePage() =>
		Assert.Equal("https://claude.ai/settings/usage", Views.Provider(new ProviderState(new ClaudeProvider()), DateTime.UtcNow).Dashboard);

	[Theory]
	[InlineData("feature/POS20-13047", "POS20-13047-prepaid-internet-orders-report\nfeature/POS20-13047")]
	[InlineData("", "POS20-13047-prepaid-internet-orders-report")]
	public void SessionTipShowsTheWholeName(string branch, string tip) => // в строке длинное имя обрезано троеточием
		Assert.Equal(tip, new SessionView("POS20-13047-prepaid-internet-orders-report", branch, "", Avalonia.Media.Brushes.Gray).Tip);

	[Fact]
	public void PassedResetShowsAnEmptyWindow() // после сна вход истёк, а сессия за это время сбросилась — не 91%
	{
		var now = DateTime.UtcNow;
		var state = new ProviderState(new ClaudeProvider())
		{
			Snapshot = new("Max", null,
			[
				new("langWindowSession", null, 91, now.AddMinutes(-5), Limiting: true),
				new("langWindowWeek", null, 40, now.AddDays(2), Limiting: true),
			], null),
		};

		var cards = Views.Provider(state, now).Cards;

		Assert.Equal(("0", 0d, "langResetDone"), (cards[0].Value, cards[0].Fill, cards[0].Resets));
		Assert.Equal("40", cards[1].Value);
	}

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

	[Theory]
	[InlineData(21.3, 30)]
	[InlineData(40, 40)]
	[InlineData(124.61, 160)]
	[InlineData(294_400_000, 300_000_000)]
	[InlineData(0, 0)]
	public void ScaleIsTwoRoundSteps(double top, double ceiling) => Assert.Equal(ceiling, Views.Ceiling(top), 6);

	[Fact]
	public void AxisSaysMoneyForCost() => Assert.Equal(["$40", "$20", "$0"], Views.AxisY(40, ChartMode.Cost));

	[Fact]
	public void EmptyAxisShowsOnlyZero() => Assert.Equal(["", "", "$0"], Views.AxisY(0, ChartMode.Cost));

	[Fact]
	public void DatesEveryWeekFromTheFirstDay()
	{
		var days = Enumerable.Range(0, 30).Select(offset => Day(1) with { Date = new DateOnly(2026, 9, 8).AddDays(offset) }).ToList();

		Assert.Equal([0, 7, 14, 21, 28], Views.AxisX(days).Select(tick => tick.Index));
	}
}
