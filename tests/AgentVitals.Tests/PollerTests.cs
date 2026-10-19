using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
	public void SuccessWaitsInterval() => Assert.Equal(_now.AddMinutes(2), AgentState.Schedule(null, 120, _now));

	[Fact]
	public void ExpiredTokenRechecksOnNextTick() =>
		Assert.Equal(_now, AgentState.Schedule(new SignInException("claude"), 60, _now));

	[Fact]
	public void RateLimitWaitsUntilAllowed() =>
		Assert.Equal(_now.AddMinutes(42), AgentState.Schedule(new RateLimitedException(_now.AddMinutes(42)), 60, _now));

	[Theory]
	[InlineData(1, 5)]
	[InlineData(2, 10)]
	[InlineData(3, 20)]
	[InlineData(4, 30)]
	[InlineData(9, 30)]
	public void RefusalsInARowWaitLonger(int refusals, int minutes) => Assert.Equal(TimeSpan.FromMinutes(minutes), AgentState.Backoff(refusals));

	[Fact]
	public async Task RepeatedRefusalWaitsLongerThanTheServerSays() // сервер всякий раз говорит «через ноль» — пауза растёт
	{
		using var http = new HttpClient();
		var start = _now;
		var state = new AgentState(new Fake(() => throw new RateLimitedException(start.AddMinutes(5))));

		await state.FetchAsync(http, start);
		Assert.Equal(_now.AddMinutes(5), state.NextFetch(60));

		start = _now.AddMinutes(5);
		await state.FetchAsync(http, start);
		Assert.Equal(_now.AddMinutes(15), state.NextFetch(60));
	}

	[Fact]
	public async Task LongerServerWaitWins()
	{
		using var http = new HttpClient();
		var state = new AgentState(new Fake(() => throw new RateLimitedException(_now.AddHours(1))));

		await state.FetchAsync(http, _now);

		Assert.Equal(_now.AddHours(1), state.NextFetch(60));
	}

	[Fact]
	public async Task AnswerEndsTheRefusals()
	{
		using var http = new HttpClient();
		var refuse = true;
		var state = new AgentState(new Fake(() => refuse ? throw new RateLimitedException(_now.AddMinutes(5)) : _snapshot));

		await state.FetchAsync(http, _now);
		await state.FetchAsync(http, _now);
		refuse = false;
		await state.FetchAsync(http, _now);
		refuse = true;
		await state.FetchAsync(http, _now);

		Assert.Equal(_now.AddMinutes(5), state.NextFetch(60));
	}

	[Fact]
	public void BrokenCredentialsRetryInFiveMinutes() =>
		Assert.Equal(_now.AddMinutes(5), AgentState.Schedule(new JsonException("truncated"), 60, _now));

	[Fact]
	public async Task WakeRetriesOnlyWhileTheNetworkIsDown() // после сна сеть поднимается не сразу
	{
		using var http = new HttpClient();
		Exception? answer = new HttpRequestException();
		var state = new AgentState(new Fake(() => answer is null ? _snapshot : throw answer));

		await state.FetchAsync(http, _now);
		Assert.True(Poller.Offline([state]));

		answer = new TaskCanceledException(); // таймаут HttpClient
		await state.FetchAsync(http, _now);
		Assert.True(Poller.Offline([state]));

		answer = new SignInException("fake"); // сервер ответил: сеть есть, повтор не поможет
		await state.FetchAsync(http, _now);
		Assert.False(Poller.Offline([state]));

		answer = null;
		await state.FetchAsync(http, _now);
		Assert.False(Poller.Offline([state]));
	}

	[Fact]
	public async Task SilentStatusPageCountsAsOffline() // после сна статус-страница молчит, пока нет сети
	{
		using var http = new HttpClient();
		Exception? answer = new HttpRequestException();
		var state = new AgentState(new Fake(() => _snapshot, status: new Page(() => answer is null ? new("none", "", null, "Fake") : throw answer)));

		await state.FetchAsync(http, _now);
		await state.FetchStatusAsync(http, _now, force: true);
		Assert.Null(state.Status);
		Assert.True(Poller.Offline([state]));

		answer = null;
		await state.FetchStatusAsync(http, _now, force: true);
		Assert.Equal("none", state.Status!.Indicator);
		Assert.False(Poller.Offline([state]));
	}

	[Fact]
	public async Task StatusIsAskedEveryFiveMinutes()
	{
		using var http = new HttpClient();
		var asked = 0;
		var state = new AgentState(new Fake(() => _snapshot, status: new Page(() =>
		{
			asked++;

			return new("none", "", null, "Fake");
		})));

		await state.FetchStatusAsync(http, _now, force: false);
		await state.FetchStatusAsync(http, _now.AddMinutes(4), force: false);
		Assert.Equal(1, asked);

		await state.FetchStatusAsync(http, _now.AddMinutes(5), force: false);
		await state.FetchStatusAsync(http, _now.AddMinutes(6), force: true); // «Обновить» не ждёт срока
		Assert.Equal(3, asked);
	}

	[Fact]
	public void AgentWithoutStatusPageIsNotOffline() => Assert.False(Poller.Offline([new AgentState(new Fake(() => _snapshot))]));

	[Fact]
	public void IconAndHeaderSeeThePassedReset() // карточка показывала 0%, а значок и шапка — старые 95%
	{
		var reset = new QuotaWindow(WindowKind.Session, null, 95, _now.AddMinutes(-1), Limiting: true);
		var state = new AgentState(new ClaudeAgent()) { Snapshot = new("Max", null, [reset], null) };

		Assert.Equal(0, state.Tightest(_now));
		Assert.Equal(0, Assert.Single(state.Windows(_now)).Used);
	}

	[Fact]
	public async Task ExpiredSignInHasNoTightest() // старые проценты при истёкшем входе ничего не говорят — серое
	{
		using var http = new HttpClient();
		var state = new AgentState(new Fake(() => throw new SignInException("fake")))
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
		var state = new AgentState(new Fake(() => _snapshot));

		await state.FetchAsync(http, _now);

		Assert.Same(_snapshot, state.Snapshot);
		Assert.NotNull(state.Updated);
		Assert.Null(state.Error);
	}

	[Fact]
	public void NetworkErrorRetriesInFiveMinutes() =>
		Assert.Equal(_now.AddMinutes(5), AgentState.Schedule(new HttpRequestException(), 60, _now));

	[Fact]
	public async Task NewIntervalAppliesAtOnce() // с 10 минут на минуту — без ожидания прежних 10 минут
	{
		using var http = new HttpClient();
		var state = new AgentState(new Fake(() => _snapshot));

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
		var state = new AgentState(new Fake(() => throw new SignInException("fake"), credentials));

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
		var state = new AgentState(new Fake(() =>
		{
			File.SetLastWriteTimeUtc(credentials, DateTime.UtcNow.AddMinutes(1));

			throw new SignInException("fake");
		}, credentials));

		await state.FetchAsync(http, _now);

		Assert.Equal(_now, state.NextFetch(60));
	}

	[Fact]
	public void NoSubscriptionIsAskedAgainNextDay() => // подписку могли оформить, пока программа работает; вход git от этого не меняется
		Assert.Equal(_now.AddDays(1), AgentState.Schedule(new SignInException("git", SignInProblem.NoSubscription), 60, _now));

	[Fact]
	public async Task NoSubscriptionDoesNotWaitForNewCredentials()
	{
		using var temp = new TempDirectory();
		var credentials = temp.File("auth.json");
		File.WriteAllText(credentials, "{}");
		var state = new AgentState(new Fake(() => throw new SignInException("git", SignInProblem.NoSubscription), credentials));
		using var http = new HttpClient();

		await state.FetchAsync(http, _now);

		Assert.Equal(_now.AddDays(1), state.NextFetch(60));
	}

	[Fact]
	public void FirstFetchIsDueAtOnce() => Assert.True(new AgentState(new ClaudeAgent()).NextFetch(60) < _now);

	[Fact]
	public void AgentShowsUpOnceItsCredentialsAppear() // проверка — раз в тик, а не на каждый кадр значка
	{
		using var temp = new TempDirectory();
		var credentials = temp.File("auth.json");
		var state = new AgentState(new Fake(() => _snapshot, credentials));
		Assert.False(state.Installed);

		File.WriteAllText(credentials, "{}"); // человек вошёл в CLI агента
		Assert.False(state.Installed);

		state.CheckInstalled();
		Assert.True(state.Installed);
	}

	[Fact]
	public async Task UnconfirmedAgentStaysHidden() // признаки Copilot есть у всех с VS Code — этого мало, чтобы показать вкладку
	{
		using var temp = new TempDirectory();
		var credentials = temp.File("auth.json");
		File.WriteAllText(credentials, "{}");
		var state = new AgentState(new Fake(() => throw new SignInException("git", SignInProblem.NoSubscription), credentials, confirmFirst: true));
		using var http = new HttpClient();

		await state.FetchAsync(http, _now);

		Assert.True(state.Installed); // спрашивать — да
		Assert.False(state.Visible); // показывать — нет
	}

	[Fact]
	public async Task ConfirmedAgentStaysAfterRejection() // ошибка видна на вкладке, а не прячет её
	{
		using var temp = new TempDirectory();
		var credentials = temp.File("auth.json");
		File.WriteAllText(credentials, "{}");
		var calls = 0;
		var state = new AgentState(new Fake(() => ++calls == 1 ? _snapshot : throw new SignInException("git", SignInProblem.Rejected), credentials, confirmFirst: true));
		using var http = new HttpClient();

		await state.FetchAsync(http, _now);
		await state.FetchAsync(http, _now.AddMinutes(1));

		Assert.True(state.Visible);
	}

	[Fact]
	public void RememberedAnswerConfirms() // подтверждение из quota.json: вкладка сразу после запуска
	{
		using var temp = new TempDirectory();
		var credentials = temp.File("auth.json");
		File.WriteAllText(credentials, "{}");
		var state = new AgentState(new Fake(() => _snapshot, credentials, confirmFirst: true));

		state.Recall(new RememberedQuota(_snapshot, _now, null), _now);

		Assert.True(state.Visible);
	}

	[Fact]
	public void DefaultOrderIsCatalog() =>
		Assert.Equal(["Claude", "Codex", "Gemini", "Copilot", "Cursor"], AgentCatalog.All().Select(agent => agent.Name));

	[Fact]
	public void ArrangeFollowsSavedOrderThenCatalog() // незнакомое имя в config.json пропускается
	{
		List<AgentState> agents = [.. new[] { "Claude", "Codex", "Gemini", "Copilot", "Cursor" }.Select(name => new AgentState(new Fake(() => _snapshot, name: name)))];

		Assert.Equal(
			["Cursor", "Claude", "Codex", "Gemini", "Copilot"],
			Poller.Arrange(agents, ["Cursor", "Nobody", "Claude"]).Select(state => state.Agent.Name));
	}

	[Fact]
	public void SwitchedOffAgentHasNoTab()
	{
		using var temp = new TempDirectory();
		var credentials = temp.File("auth.json");
		File.WriteAllText(credentials, "{}");
		var state = new AgentState(new Fake(() => _snapshot, credentials)) { Snapshot = _snapshot };

		state.Off = true;

		Assert.True(state.Installed);
		Assert.False(state.Visible);
	}

	[Fact]
	public void OrdinaryAgentShowsOnceInstalled()
	{
		using var temp = new TempDirectory();
		var credentials = temp.File("auth.json");
		File.WriteAllText(credentials, "{}");

		Assert.True(new AgentState(new Fake(() => _snapshot, credentials)).Visible);
	}

	[Fact]
	public async Task BrokenStatisticsDoNotStopPolling() // тик опроса — async void: исключение уронило бы программу
	{
		using var http = new HttpClient();
		var state = new AgentState(new WithLog(new Broken()));

		await state.ReadUsageAsync(http, _now);

		Assert.Null(state.Days);
	}

	[Fact]
	public async Task StatisticsComeFromTheAgentsLog()
	{
		using var http = new HttpClient();
		IReadOnlyList<UsageDay> days = [new(new DateOnly(2026, 10, 7), 1, 2, 3, 4, null, false, TimeSpan.Zero)];
		var state = new AgentState(new WithLog(new Fixed(days)));

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
		Assert.Equal(_now.AddSeconds(expected), AgentState.Schedule(null, seconds, _now));

	/// <summary>Агент, который отвечает тем, что вернёт или бросит answer.</summary>
	private sealed class Fake(Func<Snapshot> answer, string credentials = "none.json", IStatusPage? status = null, bool confirmFirst = false, string name = "Fake") : IAgent
	{
		public bool ConfirmFirst => confirmFirst;

		public string Name => name;

		public string Cli => "fake";

		public string Credentials => credentials;

		public string? Dashboard => null;

		public IUsageLog? Usage => null;

		public IStatusPage? Status => status;

		public HookTarget? Hooks => null;

		public Task<Snapshot> FetchAsync(HttpClient http, CancellationToken cancellation) => Task.FromResult(answer());
	}

	private sealed class WithLog(IUsageLog log) : IAgent
	{
		public string Name => "Fake";

		public string Cli => "fake";

		public string Credentials => "";

		public string? Dashboard => null;

		public IUsageLog? Usage => log;

		public IStatusPage? Status => null;

		public HookTarget? Hooks => null;

		public Task<Snapshot> FetchAsync(HttpClient http, CancellationToken cancellation) => throw new NotSupportedException();
	}

	/// <summary>Статус-страница, которая отвечает тем, что вернёт или бросит answer.</summary>
	private sealed class Page(Func<ServiceStatus> answer) : IStatusPage
	{
		public string Name => "Fake";

		public string Url => "https://status.example.com";

		public Task<ServiceStatus> FetchAsync(HttpClient http, CancellationToken cancellation) => Task.FromResult(answer());
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
