using System;
using System.Net.Http;
using System.Text.Json;
using Xunit;

namespace AgentVitals.Tests;

public class PollerTests
{
	private static readonly DateTime _now = new(2026, 10, 7, 12, 0, 0);

	[Fact]
	public void SuccessWaitsInterval() => Assert.Equal(_now.AddMinutes(2), Poller.Schedule(null, 120, _now));

	[Fact]
	public void ExpiredTokenRechecksOnNextTick() =>
		Assert.Equal(_now, Poller.Schedule(new TokenExpiredException("claude"), 60, _now));

	[Fact]
	public void RateLimitWaitsUntilAllowed() =>
		Assert.Equal(_now.AddMinutes(42), Poller.Schedule(new RateLimitedException(_now.AddMinutes(42)), 60, _now));

	[Fact]
	public void BrokenCredentialsRetryInFiveMinutes() =>
		Assert.Equal(_now.AddMinutes(5), Poller.Schedule(new JsonException("truncated"), 60, _now));

	[Fact]
	public void NetworkErrorRetriesInFiveMinutes() =>
		Assert.Equal(_now.AddMinutes(5), Poller.Schedule(new HttpRequestException(), 60, _now));

	[Theory]
	[InlineData(30, 30)]
	[InlineData(10, 30)]
	[InlineData(0, 30)]
	[InlineData(-5, 30)]
	[InlineData(999_999, 86_400)]
	public void ClampsHandEditedInterval(int seconds, int expected) =>
		Assert.Equal(_now.AddSeconds(expected), Poller.Schedule(null, seconds, _now));
}
