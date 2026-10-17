using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AgentVitals.Tests;

/// <summary>Разбор ответа Cursor — по тестам ClaudeBar: на живом аккаунте не проверено.</summary>
public class CursorProviderTests
{
	private const string _usage = """
		{
		  "billingCycleStart": "2026-02-06T03:34:49.000Z",
		  "billingCycleEnd": "2026-03-06T03:34:49.000Z",
		  "membershipType": "ultra",
		  "limitType": "user",
		  "isUnlimited": false,
		  "individualUsage": {
		    "plan": { "enabled": true, "used": 326, "limit": 40000, "remaining": 39674,
		              "breakdown": { "included": 40000, "bonus": 0, "total": 40000 },
		              "autoPercentUsed": 0.033, "apiPercentUsed": 0.586, "totalPercentUsed": 0.815 },
		    "onDemand": { "enabled": false, "used": 0, "limit": null, "remaining": null }
		  }
		}
		""";

	private static string Jwt(string sub) =>
		"e30." + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{{\"sub\":\"{sub}\"}}")).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".sig";

	private static Snapshot Plan(string plan, bool unlimited = false) => CursorProvider.Parse(JsonNode.Parse(
		$$"""{ "billingCycleEnd": "2026-03-06T03:34:49.000Z", "isUnlimited": {{(unlimited ? "true" : "false")}}, "individualUsage": { "plan": {{plan}} } }""")!, null);

	[Theory]
	[InlineData("auth0|user_01ABC", "user_01ABC")]
	[InlineData("github|user.x-1_y", "user.x-1_y")]
	[InlineData("auth0|bad id", null)] // в cookie ушло бы что попало
	[InlineData("auth0|", null)]
	public void TakesUserIdFromToken(string sub, string? id) => Assert.Equal(id, CursorProvider.UserId(Jwt(sub)));

	[Fact]
	public void BrokenTokenHasNoUserId() => Assert.Null(CursorProvider.UserId("not-a-jwt"));

	[Fact]
	public void ReadsUsage()
	{
		var snapshot = CursorProvider.Parse(JsonNode.Parse(_usage)!, "dev@example.com");

		Assert.Equal([(WindowKind.Month, null), (WindowKind.MonthFeature, "Auto"), (WindowKind.MonthFeature, "API")],
			snapshot.Windows.Select(window => (window.Kind, window.Label)));
		Assert.Equal([0.815, 0.033, 0.586], snapshot.Windows.Select(window => window.Used)); // уже проценты: 0,815%
		Assert.Equal([true, false, false], snapshot.Windows.Select(window => window.Limiting)); // Auto и API — части общего лимита
		Assert.All(snapshot.Windows, window => Assert.Equal(new DateTimeOffset(2026, 3, 6, 3, 34, 49, TimeSpan.Zero), window.ResetsAt));
		Assert.Equal("Ultra", snapshot.Plan);
		Assert.Equal("dev@example.com", snapshot.Account);
	}

	[Fact]
	public void CountsUsedWithoutPercent() =>
		Assert.Equal(0.815, Plan("""{ "enabled": true, "used": 326, "limit": 40000 }""").Windows[0].Used, 6);

	[Fact]
	public void LimitIsTheLargerOfTwo() =>
		Assert.Equal(10, Plan("""{ "enabled": true, "used": 50, "limit": 0, "breakdown": { "total": 500 } }""").Windows[0].Used, 6);

	[Fact]
	public void UnlimitedWithoutPlanIsEmpty() => Assert.Empty(Plan("""{ "enabled": false }""", unlimited: true).Windows);

	[Fact]
	public void NoPlanIsAParseError() => Assert.Throws<JsonException>(() => Plan("""{ "enabled": false }"""));

	[Fact]
	public async Task SendsTheSessionCookie()
	{
		var answer = new Answer(HttpStatusCode.OK, _usage);
		using var http = new HttpClient(answer);
		var token = Jwt("auth0|user_01ABC");

		await CursorProvider.AskAsync(http, token, null, CancellationToken.None);

		var sent = Assert.Single(answer.Requests);
		Assert.Equal("https://cursor.com/api/usage-summary", sent.Url.ToString());
		Assert.Equal($"WorkosCursorSessionToken=user_01ABC%3A%3A{token}", sent.Cookie);
		Assert.Null(sent.Authorization);
	}

	[Fact]
	public async Task RejectedCookieAsksToSignInAgain() // текст ошибки уходит в журнал
	{
		using var http = new HttpClient(new Answer(HttpStatusCode.Unauthorized));
		var token = Jwt("auth0|user_01ABC");

		var error = await Assert.ThrowsAsync<SignInException>(() => CursorProvider.AskAsync(http, token, null, CancellationToken.None));

		Assert.Equal(SignInProblem.Rejected, error.Problem);
		Assert.DoesNotContain(token, error.ToString());
	}
}
