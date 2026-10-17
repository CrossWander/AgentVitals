using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AgentVitals.Tests;

/// <summary>Разбор ответа и отказов Copilot. Настоящий вход из «Диспетчера учётных данных» тесты не читают.</summary>
public class CopilotAgentTests
{
	// План Free с машины пользователя, без личных полей.
	private const string _free = """
		{
		  "copilot_plan": "individual",
		  "access_type_sku": "free_limited_copilot",
		  "login": "octocat",
		  "quota_reset_date_utc": "2026-11-01T00:00:00.000Z",
		  "quota_snapshots": {
		    "chat": { "entitlement": 200, "remaining": 200, "percent_remaining": 100.0, "unlimited": false, "overage_permitted": false, "overage_count": 0 },
		    "completions": { "entitlement": 2000, "remaining": 1326, "percent_remaining": 66.3, "unlimited": false, "overage_permitted": false, "overage_count": 0 },
		    "premium_interactions": { "entitlement": 0, "remaining": 0, "percent_remaining": 0.0, "unlimited": false, "overage_permitted": false, "overage_count": 0 }
		  }
		}
		""";

	// Платный план, пример ClaudeBar.
	private const string _paid = """{"login":"octocat","copilot_plan":"business","quota_snapshots":{"premium_interactions":{"entitlement":300,"remaining":298,"percent_remaining":99.3,"unlimited":false}},"quota_reset_date_utc":"2026-03-01T00:00:00.000Z"}""";

	// Строкой, а не JsonObject: числа из разобранного ответа читаются как double, а созданные в памяти — нет.
	private static JsonNode Snapshots(params (string Name, int Entitlement, int Remaining)[] quotas) => JsonNode.Parse(
		"{\"access_type_sku\":\"copilot_pro\",\"quota_snapshots\":{"
		+ string.Join(",", quotas.Select(quota => $"\"{quota.Name}\":{{\"entitlement\":{quota.Entitlement},\"remaining\":{quota.Remaining},\"unlimited\":false}}"))
		+ "}}")!;

	[Fact]
	public void ReadsFreePlan()
	{
		var snapshot = CopilotAgent.Parse(JsonNode.Parse(_free)!);

		Assert.Equal(["chat", "completions"], snapshot.Windows.Select(window => window.Label)); // премиум с entitlement 0 — пропущен
		Assert.All(snapshot.Windows, window => Assert.Equal(WindowKind.MonthFeature, window.Kind));
		Assert.All(snapshot.Windows, window => Assert.True(window.Limiting));
		Assert.Equal(0, snapshot.Windows[0].Used, 1);
		Assert.Equal(33.7, snapshot.Windows[1].Used, 1);
		Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), snapshot.Windows[0].ResetsAt);
		Assert.Equal("Free", snapshot.Plan);
		Assert.Equal("octocat", snapshot.Account);
	}

	[Fact]
	public void ReadsPaidPlan()
	{
		var snapshot = CopilotAgent.Parse(JsonNode.Parse(_paid)!);

		var window = Assert.Single(snapshot.Windows);
		Assert.Equal("premium_interactions", window.Label);
		Assert.Equal(0.7, window.Used, 1);
		Assert.Equal("Business", snapshot.Plan);
	}

	[Fact]
	public void PremiumComesFirstAndStrangersLast() =>
		Assert.Equal(["premium_interactions", "chat", "completions", "agent_mode"], CopilotAgent.Parse(Snapshots(
			("completions", 100, 50), ("agent_mode", 10, 5), ("chat", 100, 50), ("premium_interactions", 300, 150))).Windows.Select(window => window.Label));

	[Fact]
	public void SkipsUnlimited()
	{
		var snapshot = CopilotAgent.Parse(JsonNode.Parse("""
			{ "access_type_sku": "copilot_pro", "quota_snapshots": {
			  "chat": { "entitlement": 0, "remaining": 0, "unlimited": true },
			  "completions": { "entitlement": 2000, "remaining": 2000, "percent_remaining": 100, "unlimited": true },
			  "premium_interactions": { "entitlement": 300, "remaining": 150, "percent_remaining": 50, "unlimited": false } } }
			""")!);

		Assert.Equal("premium_interactions", Assert.Single(snapshot.Windows).Label);
	}

	[Fact]
	public void CountsFromRemainingWithoutPercent() =>
		Assert.Equal(25, CopilotAgent.Parse(JsonNode.Parse(
			"""{"access_type_sku":"copilot_pro","quota_snapshots":{"chat":{"entitlement":200,"remaining":150,"unlimited":false}}}""")!).Windows[0].Used, 1);

	[Fact]
	public void FallsBackToQuotaResetDate() => // дата без времени — полночь UTC: так GitHub и сбрасывает
		Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), CopilotAgent.Parse(JsonNode.Parse(
			"""{"access_type_sku":"copilot_pro","quota_reset_date":"2026-11-01","quota_snapshots":{"chat":{"entitlement":100,"remaining":50,"percent_remaining":50,"unlimited":false}}}""")!).Windows[0].ResetsAt);

	[Fact]
	public void FreeWithoutQuotasIsNoCopilot() =>
		Assert.Equal(SignInProblem.NoSubscription, Assert.Throws<SignInException>(() => CopilotAgent.Parse(JsonNode.Parse(
			"""{"access_type_sku":"free_limited_copilot","quota_snapshots":{"chat":{"entitlement":0,"remaining":0,"percent_remaining":0,"unlimited":false}}}""")!)).Problem);

	[Fact]
	public void PaidWithoutQuotasIsEmptyNotError() // безлимит: только строка тарифа
	{
		var snapshot = CopilotAgent.Parse(JsonNode.Parse(
			"""{"access_type_sku":"copilot_enterprise_seat","copilot_plan":"enterprise","quota_snapshots":{"chat":{"unlimited":true}}}""")!);

		Assert.Empty(snapshot.Windows);
		Assert.Equal("Enterprise", snapshot.Plan);
	}

	[Theory]
	[InlineData(HttpStatusCode.Unauthorized, SignInProblem.Rejected)]
	[InlineData(HttpStatusCode.Forbidden, SignInProblem.NoSubscription)]
	[InlineData(HttpStatusCode.NotFound, SignInProblem.NoSubscription)]
	public void RefusalSaysWhy(HttpStatusCode status, SignInProblem problem)
	{
		using var response = new HttpResponseMessage(status);

		Assert.Equal(problem, Assert.IsType<SignInException>(CopilotAgent.Refusal(response)).Problem);
	}

	[Fact]
	public void ExhaustedRateLimitWaits() // лимит запросов GitHub — ждать, а не «нет Copilot»
	{
		using var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
		response.Headers.Add("X-RateLimit-Remaining", "0");

		Assert.IsType<RateLimitedException>(CopilotAgent.Refusal(response));
	}

	[Fact]
	public void ExhaustedRateLimitWaitsUntilGitHubResets() // GitHub говорит, когда лимит вернётся, — не гадать пять минут
	{
		var reset = DateTimeOffset.UtcNow.AddMinutes(17);
		using var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
		response.Headers.Add("X-RateLimit-Remaining", "0");
		response.Headers.Add("X-RateLimit-Reset", reset.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));

		var until = Assert.IsType<RateLimitedException>(CopilotAgent.Refusal(response)).Until;

		Assert.Equal(reset.ToUnixTimeSeconds(), new DateTimeOffset(until, TimeSpan.Zero).ToUnixTimeSeconds());
	}

	[Theory]
	[InlineData("""{"entitlement":200,"remaining":-20,"percent_remaining":-10,"unlimited":false}""")]
	[InlineData("""{"entitlement":200,"remaining":-20,"unlimited":false}""")]
	public void OverageGoesPastAHundred(string quota) =>
		Assert.Equal(110, CopilotAgent.Parse(JsonNode.Parse(
			"{\"access_type_sku\":\"copilot_pro\",\"quota_snapshots\":{\"premium_interactions\":" + quota + "}}")!).Windows[0].Used, 1);

	[Fact]
	public void OtherAnswersAreNotRefusals()
	{
		using var response = new HttpResponseMessage(HttpStatusCode.InternalServerError);

		Assert.Null(CopilotAgent.Refusal(response));
	}

	[Fact]
	public async Task TokenGoesOnlyToCopilotUser()
	{
		var answer = new Answer(HttpStatusCode.OK, _paid);
		using var http = new HttpClient(answer);

		await CopilotAgent.AskAsync(http, "gho_secret", CancellationToken.None);

		var sent = Assert.Single(answer.Requests);
		Assert.Equal("https://api.github.com/copilot_internal/user", sent.Url.ToString());
		Assert.Equal("token gho_secret", sent.Authorization);
		Assert.Null(http.DefaultRequestHeaders.Authorization); // общий клиент не несёт токен в другие запросы
	}

	[Fact]
	public async Task RejectionDoesNotLeakTheToken() // текст ошибки уходит в журнал
	{
		using var http = new HttpClient(new Answer(HttpStatusCode.Unauthorized));

		var error = await Assert.ThrowsAsync<SignInException>(() => CopilotAgent.AskAsync(http, "gho_secret", CancellationToken.None));

		Assert.Equal(SignInProblem.Rejected, error.Problem);
		Assert.DoesNotContain("gho_secret", error.ToString());
	}
}
