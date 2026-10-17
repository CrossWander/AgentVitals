using System.Text.Json.Nodes;
using Xunit;

namespace AgentVitals.Tests;

public class GitHubStatusTests
{
	// Общий индикатор — major из-за Actions: о Copilot он ничего не говорит.
	private const string _summary = """
		{ "status": { "indicator": "major", "description": "Partial System Outage" },
		  "components": [ { "id": "a1", "name": "Actions", "status": "major_outage" },
		                  { "id": "c1", "name": "Copilot", "status": "STATE" } ],
		  "incidents": [ { "name": "Actions are down", "components": [ { "id": "a1", "name": "Actions" } ] },
		                 { "name": "Copilot is slow", "components": [ { "id": "c1", "name": "Copilot" } ] } ] }
		""";

	private static JsonNode Summary(string state) => JsonNode.Parse(_summary.Replace("STATE", state))!;

	[Fact]
	public void ReadsCopilotComponentAndItsIncident()
	{
		var status = GitHubStatus.Parse(Summary("degraded_performance"))!;

		Assert.Equal("minor", status.Indicator);
		Assert.Equal(Health.Warning, status.Health);
		Assert.Equal("Copilot is slow", status.Incident);
		Assert.Equal("Copilot", status.Name);
	}

	[Theory]
	[InlineData("operational", "none")]
	[InlineData("degraded_performance", "minor")]
	[InlineData("partial_outage", "major")]
	[InlineData("major_outage", "critical")]
	[InlineData("under_maintenance", "maintenance")]
	public void MapsComponentStatus(string state, string indicator) => Assert.Equal(indicator, GitHubStatus.Parse(Summary(state))!.Indicator);

	[Fact]
	public void OtherIncidentsAreNotCopilots()
	{
		var summary = Summary("operational");
		((JsonArray)summary["incidents"]!).RemoveAt(1);

		Assert.Null(GitHubStatus.Parse(summary)!.Incident);
	}

	[Fact]
	public void NoComponentIsNoStatus() =>
		Assert.Null(GitHubStatus.Parse(JsonNode.Parse("""{ "components": [ { "id": "a1", "name": "Actions", "status": "operational" } ] }""")));
}
