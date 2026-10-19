using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AgentVitals.Tests;

public class AccountSwitchTests
{
	private static readonly DateTime _now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

	private static Snapshot Answer(string? account = "a@x.z") =>
		new("Max", account, [new(WindowKind.Session, null, 40, new DateTimeOffset(_now.AddHours(2)), Limiting: true)], null);

	[Fact]
	public async Task FirstAnswerNamesTheAccount()
	{
		using var http = new HttpClient();
		var state = new AgentState(new Login { Who = "A" });

		await state.FetchAsync(http, _now);

		Assert.Equal("A", state.Key);
		Assert.Equal("A", state.Accounts.Single().Key);
	}

	[Fact]
	public async Task LoginToAnotherAccountRemembersThePrevious() // прежний остаётся со своими цифрами
	{
		using var http = new HttpClient();
		var login = new Login { Who = "A" };
		var state = new AgentState(login);
		await state.FetchAsync(http, _now);

		login.Who = "B";
		state.CheckAccount(_now);

		Assert.Equal("B", state.Key);
		Assert.True(state.Pending);
		Assert.Null(state.Snapshot);
		Assert.Equal(["A", "B"], state.Accounts.Select(account => account.Key));
		Assert.NotNull(state.Accounts[0].Snapshot);
		Assert.True(state.Due(300, _now, force: false));
	}

	[Fact]
	public async Task RateLimitStaysWithItsAccount() // пауза прежнего не держит новый, а к прежнему возвращается
	{
		using var http = new HttpClient();
		var login = new Login { Who = "A" };
		var state = new AgentState(login);
		await state.FetchAsync(http, _now);

		login.Answer = () => throw new RateLimitedException(_now.AddMinutes(30));
		await state.FetchAsync(http, _now);

		login.Who = "B";
		state.CheckAccount(_now);
		Assert.True(state.Due(300, _now, force: false));

		login.Who = "A";
		state.CheckAccount(_now.AddMinutes(1));
		Assert.IsType<RateLimitedException>(state.Error);
		Assert.False(state.Due(300, _now.AddMinutes(1), force: true));
	}

	[Fact]
	public async Task AnswerIsDroppedWhenLoginChangesDuringRequest() // чей это ответ — неизвестно
	{
		using var http = new HttpClient();
		var login = new Login { Who = "A" };
		login.During = () => login.Who = "B";
		var state = new AgentState(login);

		await state.FetchAsync(http, _now);

		Assert.Null(state.Snapshot);
		Assert.Null(state.Key);
		Assert.True(state.Due(300, _now, force: false));
	}

	[Fact]
	public async Task AnswerStaysWhenLoginWithIdentityInsideIsRewritten() // Cursor пишет в свою базу всё время работы
	{
		using var http = new HttpClient();
		var login = new Login { Who = "A" };
		login.During = () => login.Stamp += TimeSpan.FromSeconds(1);
		var state = new AgentState(login);

		await state.FetchAsync(http, _now);

		Assert.NotNull(state.Snapshot);
		Assert.Equal("A", state.Key);
	}

	[Fact]
	public void OnlyClaudeKeepsTheAccountApartFromTheLogin() =>
		Assert.Equal(["Claude"], AgentCatalog.All().Where(agent => agent.IdentityApart).Select(agent => agent.Name));

	[Fact]
	public async Task AnswerIsDroppedWhenLoginFileIsRewrittenDuringRequest() // у Claude аккаунт записан не во входе
	{
		using var http = new HttpClient();
		var login = new Login { Who = "A", Apart = true };
		login.During = () => login.Stamp += TimeSpan.FromSeconds(1);
		var state = new AgentState(login);

		await state.FetchAsync(http, _now);

		Assert.Null(state.Snapshot);
		Assert.True(state.Due(300, _now, force: false));
	}

	[Fact]
	public async Task AccountFromTheAnswerSwitchesWithoutLocalIdentity() // так переключается Copilot
	{
		using var http = new HttpClient();
		var login = new Login { Answer = () => Answer("l1") };
		var state = new AgentState(login);
		await state.FetchAsync(http, _now);

		login.Answer = () => Answer("l2");
		await state.FetchAsync(http, _now.AddMinutes(5));

		Assert.Equal("l2", state.Key);
		Assert.False(state.Pending);
		Assert.Equal(["l1", "l2"], state.Accounts.Select(account => account.Key));
		Assert.Equal("l1", state.Accounts[0].Snapshot?.Account);
	}

	[Fact]
	public async Task RewrittenLoginWithoutLocalIdentityIsAskedNow()
	{
		using var http = new HttpClient();
		var login = new Login();
		var state = new AgentState(login);
		await state.FetchAsync(http, _now);

		Assert.False(state.Due(300, _now.AddMinutes(1), force: false));

		login.Stamp += TimeSpan.FromMinutes(1);
		Assert.True(state.Due(300, _now.AddMinutes(1), force: false));
	}

	[Fact]
	public async Task NeverAnsweredAccountIsNotRemembered()
	{
		using var http = new HttpClient();
		var login = new Login { Who = "A" };
		var state = new AgentState(login);
		await state.FetchAsync(http, _now);

		login.Who = "B";
		state.CheckAccount(_now);
		login.Who = "A";
		state.CheckAccount(_now);

		Assert.Equal("A", state.Accounts.Single().Key);
		Assert.NotNull(state.Snapshot);
	}

	[Fact]
	public async Task ForgetKeepsAccountInUse()
	{
		using var http = new HttpClient();
		var login = new Login { Who = "A" };
		var state = new AgentState(login);
		await state.FetchAsync(http, _now);
		login.Who = "B";
		state.CheckAccount(_now);
		await state.FetchAsync(http, _now);

		state.Forget("B");
		Assert.Equal(["A", "B"], state.Accounts.Select(account => account.Key));

		state.Forget("A");
		Assert.Equal("B", state.Accounts.Single().Key);
	}

	[Fact]
	public async Task NumbersOfAnotherAccountFromOldMemoryAreReplaced() // снимок от 1.3.0 без опознания
	{
		using var http = new HttpClient();
		var state = new AgentState(new Login { Who = "K", Answer = () => Answer("new@x.z") });
		state.Recall(new(Answer("old@x.z"), _now.AddMinutes(-10), null), _now);

		Assert.Null(state.Key);
		Assert.Empty(state.Accounts);

		await state.FetchAsync(http, _now);

		Assert.Equal("K", state.Key);
		Assert.Equal("new@x.z", state.Accounts.Single().Snapshot?.Account);
	}

	/// <summary>Агент, у которого меняют, кто вошёл и когда переписан вход; During зовётся посреди запроса.</summary>
	private sealed class Login : IAgent
	{
		public string? Who { get; set; }

		public DateTime Stamp { get; set; } = _now.AddHours(-1);

		public Func<Snapshot> Answer { get; set; } = () => AccountSwitchTests.Answer();

		public Action? During { get; set; }

		public string Name => "Fake";

		public string Cli => "fake";

		public string? Credentials => null;

		public bool Installed => true;

		public DateTime SignedIn => Stamp;

		public string? Dashboard => null;

		public IUsageLog? Usage => null;

		public IStatusPage? Status => null;

		public HookTarget? Hooks => null;

		public string? Identity() => Who;

		public bool Apart { get; set; }

		public bool IdentityApart => Apart;

		public Task<Snapshot> FetchAsync(HttpClient http, CancellationToken cancellation)
		{
			During?.Invoke();

			return Task.FromResult(Answer());
		}
	}
}
