using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using Xunit;

namespace AgentVitals.Tests;

public class QuotaTests
{
	private static QuotaWindow Window(double used, bool limiting = true) =>
		new("langWindowSession", null, used, null, limiting);

	[Theory]
	[InlineData(0, Health.Good)]
	[InlineData(49.9, Health.Good)]
	[InlineData(50, Health.Warning)]
	[InlineData(79.9, Health.Warning)]
	[InlineData(80, Health.Critical)]
	[InlineData(130, Health.Critical)]
	public void RatesByThreshold(double used, Health expected) => Assert.Equal(expected, Quota.Rate(used));

	[Fact]
	public void ExhaustedWeekBeatsEmptySession()
	{
		List<QuotaWindow> windows = [Window(0), Window(100)];

		Assert.Equal(100, Quota.Bottleneck(windows)!.Used);
		Assert.Equal(Health.Critical, Quota.Rate(windows));
	}

	[Fact]
	public void ModelWindowDoesNotColourWhileLimitingWindowsExist()
	{
		List<QuotaWindow> windows = [Window(10), Window(95, limiting: false)];

		Assert.Equal(Health.Good, Quota.Rate(windows));
	}

	[Fact]
	public void OnlyModelWindowsStillColour()
	{
		List<QuotaWindow> windows = [Window(95, limiting: false)];

		Assert.Equal(Health.Critical, Quota.Rate(windows));
	}

	[Fact]
	public void NoWindowsIsUnknown() => Assert.Equal(Health.Unknown, Quota.Rate([]));

	[Fact]
	public void WorstPrefersKnownOverUnknown()
	{
		Assert.Equal(Health.Good, Quota.Worst([Health.Unknown, Health.Good]));
		Assert.Equal(Health.Critical, Quota.Worst([Health.Warning, Health.Critical]));
		Assert.Equal(Health.Unknown, Quota.Worst([]));
	}

	[Theory]
	[InlineData("team", "Team")]
	[InlineData("claude_max", "Max")]
	[InlineData("pro", "Pro")]
	[InlineData(null, "")]
	public void NamesPlan(string? subscription, string expected) => Assert.Equal(expected, Quota.PlanName(subscription));
}

public class RetryTests
{
	private static readonly DateTime _now = new(2026, 10, 7, 12, 0, 0);

	[Fact]
	public void UsesDelta() =>
		Assert.Equal(_now.AddSeconds(120), Api.RetryAt(new RetryConditionHeaderValue(TimeSpan.FromSeconds(120)), _now));

	[Fact]
	public void ZeroDeltaWaitsFiveMinutes() =>
		Assert.Equal(_now.AddMinutes(5), Api.RetryAt(new RetryConditionHeaderValue(TimeSpan.Zero), _now));

	[Fact]
	public void MissingHeaderWaitsFiveMinutes() => Assert.Equal(_now.AddMinutes(5), Api.RetryAt(null, _now));

	[Fact]
	public void PastDateWaitsFiveMinutes() =>
		Assert.Equal(_now.AddMinutes(5), Api.RetryAt(new RetryConditionHeaderValue(new DateTimeOffset(_now.AddHours(-1))), _now));

	[Fact]
	public void UsesFutureDate()
	{
		var at = new DateTimeOffset(_now.AddMinutes(30));

		Assert.Equal(at.LocalDateTime, Api.RetryAt(new RetryConditionHeaderValue(at), _now));
	}
}
