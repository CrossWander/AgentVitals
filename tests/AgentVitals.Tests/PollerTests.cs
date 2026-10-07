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

	[Fact]
	public void NewIntervalAppliesAtOnce() // с 10 минут на минуту — без ожидания прежних 10 минут
	{
		var state = new ProviderState(new ClaudeProvider()) { Started = _now };

		Assert.Equal(_now.AddMinutes(10), state.NextFetch(600));
		Assert.Equal(_now.AddMinutes(1), state.NextFetch(60));
	}

	[Fact]
	public void FirstFetchIsDueAtOnce() => Assert.True(new ProviderState(new ClaudeProvider()).NextFetch(60) < _now);

	[Theory]
	[InlineData(60, 60)]
	[InlineData(30, 60)] // прежний вариант 30 с: сервер на нём отвечал 429
	[InlineData(0, 60)]
	[InlineData(-5, 60)]
	[InlineData(999_999, 86_400)]
	public void ClampsHandEditedInterval(int seconds, int expected) =>
		Assert.Equal(_now.AddSeconds(expected), Poller.Schedule(null, seconds, _now));
}
