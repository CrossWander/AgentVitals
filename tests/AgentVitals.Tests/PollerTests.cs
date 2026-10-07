using System;
using System.Net.Http;
using System.Text.Json;
using Xunit;

namespace AgentVitals.Tests;

public class PollerTests
{
	private static readonly DateTime _now = new(2026, 10, 7, 12, 0, 0);

	[Fact]
	public void SuccessWaitsInterval() => Assert.Equal(_now.AddMinutes(15), Poller.Schedule(null, 15, _now));

	[Fact]
	public void ExpiredTokenRechecksOnNextTick() =>
		Assert.Equal(_now, Poller.Schedule(new TokenExpiredException("claude"), 15, _now));

	[Fact]
	public void RateLimitWaitsUntilAllowed() =>
		Assert.Equal(_now.AddMinutes(42), Poller.Schedule(new RateLimitedException(_now.AddMinutes(42)), 15, _now));

	[Fact]
	public void BrokenCredentialsRetryInFiveMinutes() =>
		Assert.Equal(_now.AddMinutes(5), Poller.Schedule(new JsonException("truncated"), 15, _now));

	[Fact]
	public void NetworkErrorRetriesInFiveMinutes() =>
		Assert.Equal(_now.AddMinutes(5), Poller.Schedule(new HttpRequestException(), 15, _now));

	[Theory]
	[InlineData(1, 1)]
	[InlineData(0, 1)]
	[InlineData(-5, 1)]
	[InlineData(5000, 1440)]
	public void ClampsHandEditedInterval(int minutes, int expected) =>
		Assert.Equal(_now.AddMinutes(expected), Poller.Schedule(null, minutes, _now));
}
