using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace AgentVitals.Tests;

public class ViewsTests
{
	private static readonly Thresholds _levels = new(75, 90);

	[Fact]
	public void EachTabShowsItsOwnUpdateTime() // у Codex со вчерашними данными не должно быть «только что»
	{
		var now = DateTime.UtcNow;
		var claude = new ProviderState(new ClaudeProvider()) { Updated = now };
		var codex = new ProviderState(new CodexProvider());

		Assert.Equal("langUpdatedNow", Views.Provider(claude, now, _levels).Updated);
		Assert.Equal("", Views.Provider(codex, now, _levels).Updated);
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
	public void HeaderNamesTheTightestWindow() // исчерпано окно — «исчерпано», как на его карточке
	{
		var now = DateTime.UtcNow;
		var claude = new ProviderState(new ClaudeProvider()) { Snapshot = new("Max", null, [new(WindowKind.Session, null, 100, null, Limiting: true)], null) };

		Assert.Equal("langHealthExhausted", Views.Build([claude], null, HookLink.On, [], now, _levels).HealthText);
		Assert.Equal("langHealthUnknown", Views.Build([], null, HookLink.On, [], now, _levels).HealthText);
	}

	[Theory]
	[InlineData(HookLink.Off, "langHooksHint")]
	[InlineData(HookLink.Moved, "langHooksHint")] // программу перенесли — hooks смотрят на старый путь
	[InlineData(HookLink.On, "langNoSessions")]
	public void HintSaysWhySessionsAreEmpty(HookLink hooks, string hint) =>
		Assert.Equal(hint, Views.Build([], null, hooks, [], DateTime.UtcNow, _levels).SessionsHint);

	[Fact]
	public void TabOpensItsAgentsUsagePage() =>
		Assert.Equal("https://claude.ai/settings/usage", Views.Provider(new ProviderState(new ClaudeProvider()), DateTime.UtcNow, _levels).Dashboard);

	[Theory]
	[InlineData("feature/APP-1234", "APP-1234-retry-failed-payments-in-checkout\nfeature/APP-1234")]
	[InlineData("", "APP-1234-retry-failed-payments-in-checkout")]
	public void SessionTipShowsTheWholeName(string branch, string tip) => // в строке длинное имя обрезано троеточием
		Assert.Equal(tip, new SessionView("APP-1234-retry-failed-payments-in-checkout", branch, "", Avalonia.Media.Brushes.Gray).Tip);

	[Theory]
	[InlineData(WindowKind.Session, 2.5, 50d)] // половина пяти часов прошла — по ровному темпу половина и потрачена
	[InlineData(WindowKind.Week, 24 * 7, 0d)]
	[InlineData(WindowKind.Week, 24 * 3.5, 50d)]
	[InlineData(WindowKind.WeekModel, 24 * 3.5, 50d)] // «Неделя · Opus» — те же семь дней
	[InlineData(WindowKind.Model, 24, null)] // длина окна модели неизвестна
	[InlineData(WindowKind.Session, -0.1, null)] // сброс прошёл — сравнивать не с чем
	public void ExpectedPaceIsTheElapsedShare(WindowKind kind, double hoursLeft, double? expected)
	{
		var now = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
		var window = new QuotaWindow(kind, null, 30, now.AddHours(hoursLeft), Limiting: true);

		Assert.Equal(expected, Quota.Expected(window, now));
	}

	[Theory]
	[InlineData(76, "langPaceAhead")]
	[InlineData(54, "langPaceEven")] // ±5 пунктов — ещё ровно
	[InlineData(20, "langPaceSpare")]
	public void CardTellsThePace(double used, string word)
	{
		var now = DateTime.UtcNow;
		var state = new ProviderState(new ClaudeProvider())
		{
			Snapshot = new("Max", null, [new(WindowKind.Session, null, used, now.AddHours(2.5), Limiting: true)], null),
		};

		var card = Assert.Single(Views.Provider(state, now, _levels).Cards);

		Assert.Equal(word, card.Caption); // в окне — вместо «использовано» у числа
		Assert.EndsWith(" · " + word, card.Footer); // в карточке наведения — после сброса
		Assert.Equal(50, card.Pace!.Value, 3);
		Assert.Equal("langPaceTip", card.PaceTip); // что за белая метка — подсказкой над карточкой
	}

	[Fact]
	public void NoPaceNoTip()
	{
		var state = new ProviderState(new ClaudeProvider())
		{
			Snapshot = new("Max", null, [new(WindowKind.Model, "Opus", 30, DateTime.UtcNow.AddDays(2), Limiting: false)], null),
		};

		Assert.Null(Assert.Single(Views.Provider(state, DateTime.UtcNow, _levels).Cards).PaceTip);
	}

	[Fact]
	public void PassedResetShowsAnEmptyWindow() // после сна вход истёк, а сессия за это время сбросилась — не 91%
	{
		var now = DateTime.UtcNow;
		var state = new ProviderState(new ClaudeProvider())
		{
			Snapshot = new("Max", null,
			[
				new(WindowKind.Session, null, 91, now.AddMinutes(-5), Limiting: true),
				new(WindowKind.Week, null, 40, now.AddDays(2), Limiting: true),
			], null),
		};

		var cards = Views.Provider(state, now, _levels).Cards;

		Assert.Equal(("0", 0d, "langResetDone"), (cards[0].Value, cards[0].Fill, cards[0].Resets));
		Assert.Equal("40", cards[1].Value);
	}

	private static UsageDay Day(int day, long input = 0, long output = 0, decimal? cost = 0, bool unpriced = false) =>
		new(new DateOnly(2026, 10, day), input, output, 0, 0, cost, unpriced, TimeSpan.Zero);

	[Fact]
	public void CodexHasNoStatisticsBlock() =>
		Assert.Null(Views.Provider(new ProviderState(new CodexProvider()), DateTime.UtcNow, _levels).Usage);

	[Fact]
	public void ClaudeCountsUntilTheFirstRead() // первое чтение логов — секунды: не нули, а «считаю…»
	{
		var usage = Views.Provider(new ProviderState(new ClaudeProvider()), DateTime.UtcNow, _levels).Usage!;

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
		var bars = Views.Chart([Day(6, input: 10, output: 30), Day(7, input: 20, output: 20)], ChartMode.Tokens).Bars;

		Assert.Equal([22.5, 67.5], bars[0].Parts.Select(part => part.Height)); // самый большой день — 40 — во всю высоту 90; снизу вход
		Assert.Equal(45, bars[1].Parts[0].Height);
		Assert.Equal(0.6, bars[0].Opacity);
		Assert.Equal(1, bars[1].Opacity); // сегодня — ярче
	}

	[Fact]
	public void EmptyMonthDrawsNothing() =>
		Assert.All(Views.Chart([Day(6), Day(7)], ChartMode.Cost).Bars, bar => Assert.All(bar.Parts, part => Assert.Equal(0, part.Height)));

	[Fact]
	public void ModelsStackCostByFamily()
	{
		var today = Day(7, cost: 20) with { Models = new ModelCosts(15, 5, 0, 0, 0) };

		var bar = Views.Chart([Day(6), today], ChartMode.Models).Bars[1];

		Assert.Equal([67.5, 22.5, 0, 0, 0], bar.Parts.Select(part => part.Height)); // $20 — верх шкалы, снизу Opus
	}

	[Fact]
	public void ScaleFitsTheBiggestDay() =>
		Assert.Equal(["$30", "$15", "$0"], Views.Chart([Day(6, cost: 10m), Day(7, cost: 21.3m)], ChartMode.Cost).AxisY);

	[Fact]
	public void NoDaysNoTotal() => Assert.Equal("", Views.Chart([], ChartMode.Cost).Total); // «$0.00» за месяц, которого нет, — неправда

	[Fact]
	public void LegendNamesEachLayer()
	{
		Assert.Equal(["langCostNote"], Views.Legend(ChartMode.Cost).Select(item => item.Text));
		Assert.Equal(["langInput", "langOutput"], Views.Legend(ChartMode.Tokens).Select(item => item.Text));
		Assert.Equal(["Opus", "Sonnet", "Haiku", "Fable", "langOther"], Views.Legend(ChartMode.Models).Select(item => item.Text));
	}

	[Fact]
	public void CacheTotalSaysWhatTheCacheSaved()
	{
		Assert.EndsWith(" · langCacheSaved", Views.Total([Day(6) with { Saved = 3.80m }, Day(7) with { Saved = 1.20m }], ChartMode.Cache));
		Assert.DoesNotContain("·", Views.Total([Day(6), Day(7)], ChartMode.Cache)); // без цен экономию не посчитать
	}

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
