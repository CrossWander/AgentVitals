using System;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace AgentVitals.Tests;

public class OtherProvidersTests
{
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
