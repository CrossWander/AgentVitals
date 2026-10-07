using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace AgentVitals.Tests;

public class UsageDaysTests
{
	private static readonly TimeZoneInfo _kyiv = TimeZoneInfo.CreateCustomTimeZone("UTC+3", TimeSpan.FromHours(3), "UTC+3", "UTC+3");
	private static readonly DateOnly _today = new(2026, 10, 7);

	private static readonly PriceList _prices = new(new Dictionary<string, Price>
	{
		["claude-opus-5-5"] = new(4, 5, 8, 0.20m, 20),
	});

	private static UsageRecord Answer(DateTime utc, string model = "claude-opus-5-5", long output = 1_000_000) =>
		new(Guid.NewGuid().ToString(), utc, model, 0, output, 0, 0, 0);

	private static DateTime Utc(int day, int hour, int minute) => new(2026, 10, day, hour, minute, 0, DateTimeKind.Utc);

	[Fact]
	public void LateEveningUtcIsTheNextLocalDay() // 23:30 UTC — уже 02:30 по Киеву
	{
		var days = UsageDays.Build([Answer(Utc(6, 23, 30))], null, _today, _kyiv);

		Assert.Equal(1_000_000, days[^1].Output);
		Assert.Equal(0, days[^2].Output);
	}

	[Fact]
	public void ThirtyDaysIncludingEmptyOnes()
	{
		var days = UsageDays.Build([], null, _today, _kyiv);

		Assert.Equal(30, days.Count);
		Assert.Equal(_today.AddDays(-29), days[0].Date);
		Assert.Equal(_today, days[^1].Date);
		Assert.All(days, day => Assert.Equal(0, day.Total));
	}

	[Fact]
	public void OlderAnswersStayOutside() =>
		Assert.All(UsageDays.Build([Answer(Utc(7, 10, 0).AddDays(-40))], null, _today, _kyiv), day => Assert.Equal(0, day.Total));

	[Fact]
	public void PauseOverHalfAnHourStartsNewStretch()
	{
		DateTime[] times = [Utc(7, 10, 0), Utc(7, 10, 10), Utc(7, 10, 20), Utc(7, 11, 0), Utc(7, 11, 5)];

		Assert.Equal(TimeSpan.FromMinutes(25), UsageDays.Working(times)); // 20 мин + 5 мин, 40 мин перерыва — не работа
	}

	[Fact]
	public void SingleAnswerIsNoWorkTime() => Assert.Equal(TimeSpan.Zero, UsageDays.Working([Utc(7, 10, 0)]));

	[Fact]
	public void NoPricesNoCost() => Assert.Null(UsageDays.Build([Answer(Utc(7, 10, 0))], null, _today, _kyiv)[^1].Cost);

	[Fact]
	public void UnknownModelIsLeftOutOfCost()
	{
		var day = UsageDays.Build([Answer(Utc(7, 10, 0)), Answer(Utc(7, 10, 5), "claude-new-9")], _prices, _today, _kyiv)[^1];

		Assert.Equal(20m, day.Cost); // только opus: 1M выхода × $20
		Assert.True(day.Unpriced);
		Assert.Equal(2_000_000, day.Output); // токены считаются все
	}
}
