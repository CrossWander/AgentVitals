using System.Text.Json.Nodes;
using Xunit;

namespace AgentVitals.Tests;

public class StatusPageTests
{
	[Fact]
	public void ReadsIncident()
	{
		var status = StatusPage.Parse(JsonNode.Parse("""
			{ "status": { "indicator": "minor", "description": "Partially Degraded Service" },
			  "incidents": [ { "name": "Elevated errors on Claude Opus" } ] }
			"""), "Anthropic");

		Assert.Equal("minor", status.Indicator);
		Assert.Equal(Health.Warning, status.Health);
		Assert.Equal("Elevated errors on Claude Opus", status.Incident);
		Assert.Equal("Anthropic", status.Name); // в подписях — «Все системы Anthropic работают»
	}

	[Theory]
	[InlineData("none", Health.Good)]
	[InlineData("maintenance", Health.Warning)]
	[InlineData("major", Health.Critical)]
	[InlineData("critical", Health.Critical)]
	[InlineData("weird", Health.Unknown)]
	public void RatesIndicator(string indicator, Health expected) =>
		Assert.Equal(expected, new ServiceStatus(indicator, "", null).Health);

	[Fact]
	public void MissingFieldsMeanOperational()
	{
		var status = StatusPage.Parse(JsonNode.Parse("{}"), "Anthropic");

		Assert.Equal("none", status.Indicator);
		Assert.Null(status.Incident);
	}
}
