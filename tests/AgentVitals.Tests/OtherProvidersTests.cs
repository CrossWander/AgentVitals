using System;
using System.Buffers.Text;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace AgentVitals.Tests;

public class OtherProvidersTests
{
	[Fact]
	public void CodexAccountIsTheEmailInItsIdToken()
	{
		var payload = Base64Url.EncodeToString(Encoding.UTF8.GetBytes("""{"email":"name@example.com","https://api.openai.com/auth":{"chatgpt_plan_type":"free"}}"""));

		Assert.Equal("name@example.com", CodexProvider.Email($"eyJhbGciOiJSUzI1NiJ9.{payload}.signature"));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("not-a-jwt")]
	[InlineData("a.%%%.c")]
	[InlineData("a.bm90IGpzb24.c")] // «not json»
	[InlineData("a.WzFd.c")] // «[1]» — не объект
	public void BrokenIdTokenIsNoEmail(string? token) => Assert.Null(CodexProvider.Email(token));

	[Fact]
	public void ReadsCodexWindows()
	{
		var usage = JsonNode.Parse("""
			{ "rate_limit": {
			    "primary_window": { "used_percent": 25.5, "reset_at": 1705312800, "limit_window_seconds": 18000 },
			    "secondary_window": { "used_percent": 45.0, "reset_at": 1705744800 } },
			  "plan_type": "plus" }
			""")!;

		var snapshot = CodexProvider.Parse(usage);

		Assert.Equal("Plus", snapshot.Plan);
		Assert.Equal([WindowKind.Session, WindowKind.Week], snapshot.Windows.Select(window => window.Kind));
		Assert.Equal(25.5, snapshot.Windows[0].Used);
		Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1705312800), snapshot.Windows[0].ResetsAt);
		Assert.All(snapshot.Windows, window => Assert.True(window.Limiting));
	}

	[Theory]
	[InlineData(2_592_000, WindowKind.Month)] // бесплатный план: одно окно на 30 дней, а не «сессия»
	[InlineData(604_800, WindowKind.Week)]
	[InlineData(86_400, WindowKind.Day)]
	[InlineData(18_000, WindowKind.Session)]
	[InlineData(18_500, WindowKind.Session)] // Codex сам допускает ±5%
	public void CodexWindowIsNamedByItsLength(int seconds, WindowKind kind)
	{
		var usage = JsonNode.Parse($$"""
			{ "rate_limit": { "primary_window": { "used_percent": 3, "reset_at": 1794633889, "limit_window_seconds": {{seconds}} } },
			  "plan_type": "free" }
			""")!;

		Assert.Equal(kind, Assert.Single(CodexProvider.Parse(usage).Windows).Kind);
	}

	[Fact]
	public void CodexWeekComesFirstWhenTheServerSaysSo() // длина решает, а не место в ответе
	{
		var usage = JsonNode.Parse("""
			{ "rate_limit": {
			    "primary_window": { "used_percent": 10, "limit_window_seconds": 604800 },
			    "secondary_window": { "used_percent": 20, "limit_window_seconds": 18000 } } }
			""")!;

		Assert.Equal([WindowKind.Week, WindowKind.Session], CodexProvider.Parse(usage).Windows.Select(window => window.Kind));
	}

	[Fact]
	public void CodexWithoutWindowsIsEmpty() =>
		Assert.Empty(CodexProvider.Parse(JsonNode.Parse("""{ "plan_type": "free" }""")!).Windows);

	[Fact]
	public void FoldsGeminiModelsIntoFamilies()
	{
		var quota = JsonNode.Parse("""
			{ "buckets": [
			    { "modelId": "gemini-2.5-pro", "remainingFraction": 0.4, "resetTime": "2026-10-08T00:00:00Z" },
			    { "modelId": "gemini-2.5-pro-preview", "remainingFraction": 0.7 },
			    { "modelId": "gemini-2.5-flash", "remainingFraction": 0.9 },
			    { "modelId": "gemini-2.5-flash-lite", "remainingFraction": 1.0 } ] }
			""")!;

		var snapshot = GeminiProvider.Parse(quota, "Gemini Code Assist");

		Assert.Equal("Gemini Code Assist", snapshot.Plan);
		Assert.Equal(["Pro", "Flash", "Flash Lite"], snapshot.Windows.Select(window => window.Label));
		Assert.Equal(60, snapshot.Windows[0].Used, precision: 6);
		Assert.Equal(DateTimeOffset.Parse("2026-10-08T00:00:00Z"), snapshot.Windows[0].ResetsAt);
	}
}
