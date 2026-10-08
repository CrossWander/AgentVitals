using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
	public void WakeRetriesOnlyWhileTheNetworkIsDown() // после сна сеть поднимается не сразу
	{
		var state = new ProviderState(new ClaudeProvider());
		var status = new ServiceStatus("none", "All Systems Operational", null);

		state.Record(new HttpRequestException(), _now);
		Assert.True(Poller.Offline([state], status));

		state.Record(new TaskCanceledException(), _now); // таймаут HttpClient
		Assert.True(Poller.Offline([state], status));

		state.Record(new TokenExpiredException("claude"), _now); // сервер ответил: сеть есть, повтор не поможет
		Assert.False(Poller.Offline([state], status));

		state.Record(null, _now);
		Assert.False(Poller.Offline([state], status));
		Assert.True(Poller.Offline([state], null)); // статус-страница не ответила
	}

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
	public void RejectedSignInWaitsForNewCredentials() // 401 при свежем expiresAt или истёкший вход Codex/Gemini
	{
		using var temp = new TempDirectory();
		var credentials = temp.File("auth.json");
		File.WriteAllText(credentials, "{}");
		var state = new ProviderState(new SignedOut(credentials)) { Started = _now };

		state.Record(new TokenExpiredException("fake"), File.GetLastWriteTimeUtc(credentials));
		Assert.Equal(DateTime.MaxValue, state.NextFetch(60));

		File.SetLastWriteTimeUtc(credentials, DateTime.UtcNow.AddMinutes(1)); // CLI агента обновил вход
		Assert.Equal(_now, state.NextFetch(60));
	}

	[Fact]
	public void SignInRefreshedDuringRequestIsTriedAgain() // Claude Code обновил вход, пока шёл запрос с 401
	{
		using var temp = new TempDirectory();
		var credentials = temp.File("auth.json");
		File.WriteAllText(credentials, "{}");
		var state = new ProviderState(new SignedOut(credentials)) { Started = _now };
		var before = File.GetLastWriteTimeUtc(credentials);

		File.SetLastWriteTimeUtc(credentials, DateTime.UtcNow.AddMinutes(1));
		state.Record(new TokenExpiredException("fake"), before);

		Assert.Equal(_now, state.NextFetch(60));
	}

	[Fact]
	public void FirstFetchIsDueAtOnce() => Assert.True(new ProviderState(new ClaudeProvider()).NextFetch(60) < _now);

	[Fact]
	public void AgentShowsUpOnceItsCredentialsAppear() // проверка — раз в тик, а не на каждый кадр значка
	{
		using var temp = new TempDirectory();
		var credentials = temp.File("auth.json");
		var state = new ProviderState(new SignedOut(credentials));
		Assert.False(state.Available);

		File.WriteAllText(credentials, "{}"); // человек вошёл в CLI агента
		Assert.False(state.Available);

		state.CheckInstalled();
		Assert.True(state.Available);
	}

	[Fact]
	public async Task BrokenStatisticsDoNotStopPolling() // тик опроса — async void: исключение уронило бы программу
	{
		using var http = new HttpClient();
		var state = new ProviderState(new WithLog(new Broken()));

		await state.ReadUsageAsync(http, _now);

		Assert.Null(state.Days);
	}

	[Fact]
	public async Task StatisticsComeFromTheAgentsLog()
	{
		using var http = new HttpClient();
		IReadOnlyList<UsageDay> days = [new(new DateOnly(2026, 10, 7), 1, 2, 3, 4, null, false, TimeSpan.Zero)];
		var state = new ProviderState(new WithLog(new Fixed(days)));

		await state.ReadUsageAsync(http, _now);

		Assert.Same(days, state.Days);
	}

	[Theory]
	[InlineData(60, 60)]
	[InlineData(30, 60)] // прежний вариант 30 с: сервер на нём отвечал 429
	[InlineData(0, 60)]
	[InlineData(-5, 60)]
	[InlineData(999_999, 86_400)]
	public void ClampsHandEditedInterval(int seconds, int expected) =>
		Assert.Equal(_now.AddSeconds(expected), Poller.Schedule(null, seconds, _now));

	private sealed class SignedOut(string credentials) : IProvider
	{
		public string Name => "Fake";

		public string Cli => "fake";

		public string Credentials => credentials;

		public string? Dashboard => null;

		public IUsageLog? Usage => null;

		public Task<Snapshot> FetchAsync(HttpClient http, CancellationToken cancellation) => throw new TokenExpiredException(Cli);
	}

	private sealed class WithLog(IUsageLog log) : IProvider
	{
		public string Name => "Fake";

		public string Cli => "fake";

		public string Credentials => "";

		public string? Dashboard => null;

		public IUsageLog? Usage => log;

		public Task<Snapshot> FetchAsync(HttpClient http, CancellationToken cancellation) => throw new NotSupportedException();
	}

	private sealed class Broken : IUsageLog
	{
		public Task<IReadOnlyList<UsageDay>> ReadAsync(HttpClient http, DateTime now, CancellationToken cancellation) =>
			throw new InvalidOperationException("ошибка в разборе");
	}

	private sealed class Fixed(IReadOnlyList<UsageDay> days) : IUsageLog
	{
		public Task<IReadOnlyList<UsageDay>> ReadAsync(HttpClient http, DateTime now, CancellationToken cancellation) => Task.FromResult(days);
	}
}
