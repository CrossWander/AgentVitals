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

	private static readonly Snapshot _snapshot = new("Max", null, [], null);

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
	public async Task WakeRetriesOnlyWhileTheNetworkIsDown() // после сна сеть поднимается не сразу
	{
		using var http = new HttpClient();
		Exception? answer = new HttpRequestException();
		var state = new ProviderState(new Fake(() => answer is null ? _snapshot : throw answer));
		var status = new ServiceStatus("none", "All Systems Operational", null);

		await state.FetchAsync(http, _now);
		Assert.True(Poller.Offline([state], status));

		answer = new TaskCanceledException(); // таймаут HttpClient
		await state.FetchAsync(http, _now);
		Assert.True(Poller.Offline([state], status));

		answer = new TokenExpiredException("fake"); // сервер ответил: сеть есть, повтор не поможет
		await state.FetchAsync(http, _now);
		Assert.False(Poller.Offline([state], status));

		answer = null;
		await state.FetchAsync(http, _now);
		Assert.False(Poller.Offline([state], status));
		Assert.True(Poller.Offline([state], null)); // статус-страница не ответила
	}

	[Fact]
	public void IconAndHeaderSeeThePassedReset() // карточка показывала 0%, а значок и шапка — старые 95%
	{
		var reset = new QuotaWindow(WindowKind.Session, null, 95, _now.AddMinutes(-1), Limiting: true);
		var state = new ProviderState(new ClaudeProvider()) { Snapshot = new("Max", null, [reset], null) };

		Assert.Equal(0, state.Tightest(_now));
		Assert.Equal(0, Assert.Single(state.Windows(_now)).Used);
	}

	[Fact]
	public async Task ExpiredSignInHasNoTightest() // старые проценты при истёкшем входе ничего не говорят — серое
	{
		using var http = new HttpClient();
		var state = new ProviderState(new Fake(() => throw new TokenExpiredException("fake")))
		{
			Snapshot = new("Max", null, [new(WindowKind.Session, null, 40, null, Limiting: true)], null),
		};

		Assert.Equal(40, state.Tightest(_now));
		await state.FetchAsync(http, _now);
		Assert.Null(state.Tightest(_now));
	}

	[Fact]
	public async Task AnswerIsKeptWithItsTime()
	{
		using var http = new HttpClient();
		var state = new ProviderState(new Fake(() => _snapshot));

		await state.FetchAsync(http, _now);

		Assert.Same(_snapshot, state.Snapshot);
		Assert.NotNull(state.Updated);
		Assert.Null(state.Error);
	}

	[Fact]
	public void NetworkErrorRetriesInFiveMinutes() =>
		Assert.Equal(_now.AddMinutes(5), Poller.Schedule(new HttpRequestException(), 60, _now));

	[Fact]
	public async Task NewIntervalAppliesAtOnce() // с 10 минут на минуту — без ожидания прежних 10 минут
	{
		using var http = new HttpClient();
		var state = new ProviderState(new Fake(() => _snapshot));

		await state.FetchAsync(http, _now);

		Assert.Equal(_now.AddMinutes(10), state.NextFetch(600));
		Assert.Equal(_now.AddMinutes(1), state.NextFetch(60));
	}

	[Fact]
	public async Task RejectedSignInWaitsForNewCredentials() // 401 при свежем expiresAt или истёкший вход Codex/Gemini
	{
		using var temp = new TempDirectory();
		using var http = new HttpClient();
		var credentials = temp.File("auth.json");
		File.WriteAllText(credentials, "{}");
		var state = new ProviderState(new Fake(() => throw new TokenExpiredException("fake"), credentials));

		await state.FetchAsync(http, _now);
		Assert.Equal(DateTime.MaxValue, state.NextFetch(60));

		File.SetLastWriteTimeUtc(credentials, DateTime.UtcNow.AddMinutes(1)); // CLI агента обновил вход
		Assert.Equal(_now, state.NextFetch(60));
	}

	[Fact]
	public async Task SignInRefreshedDuringRequestIsTriedAgain() // Claude Code обновил вход, пока шёл запрос с 401
	{
		using var temp = new TempDirectory();
		using var http = new HttpClient();
		var credentials = temp.File("auth.json");
		File.WriteAllText(credentials, "{}");
		var state = new ProviderState(new Fake(() =>
		{
			File.SetLastWriteTimeUtc(credentials, DateTime.UtcNow.AddMinutes(1));

			throw new TokenExpiredException("fake");
		}, credentials));

		await state.FetchAsync(http, _now);

		Assert.Equal(_now, state.NextFetch(60));
	}

	[Fact]
	public void FirstFetchIsDueAtOnce() => Assert.True(new ProviderState(new ClaudeProvider()).NextFetch(60) < _now);

	[Fact]
	public void AgentShowsUpOnceItsCredentialsAppear() // проверка — раз в тик, а не на каждый кадр значка
	{
		using var temp = new TempDirectory();
		var credentials = temp.File("auth.json");
		var state = new ProviderState(new Fake(() => _snapshot, credentials));
		Assert.False(state.Installed);

		File.WriteAllText(credentials, "{}"); // человек вошёл в CLI агента
		Assert.False(state.Installed);

		state.CheckInstalled();
		Assert.True(state.Installed);
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

	/// <summary>Агент, который отвечает тем, что вернёт или бросит answer.</summary>
	private sealed class Fake(Func<Snapshot> answer, string credentials = "none.json") : IProvider
	{
		public string Name => "Fake";

		public string Cli => "fake";

		public string Credentials => credentials;

		public string? Dashboard => null;

		public IUsageLog? Usage => null;

		public Task<Snapshot> FetchAsync(HttpClient http, CancellationToken cancellation) => Task.FromResult(answer());
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
