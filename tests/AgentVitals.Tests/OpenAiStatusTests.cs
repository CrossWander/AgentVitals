using System.Text.Json.Nodes;
using Xunit;

namespace AgentVitals.Tests;

public class OpenAiStatusTests
{
	private const string _cli = "01KMKFAMWKNQ84Z1766MV08ZDE";
	private const string _web = "01JVCV8YSWZFRSM1G5CVP253SK";
	private const string _compliance = "01JNKS9D9S72PMP1938PVFFQN4";

	// Кусок настоящего фида status.openai.com/proxy/status.openai.com от 2026-10-15.
	private static JsonNode Feed(string affected = "[]", string incidents = "[]", string codex = "Codex") => JsonNode.Parse($$"""
		{ "summary": {
		    "structure": { "items": [
		      { "group": { "id": "01K5H8S53SY1KMS4GQMNMZXTR1", "name": "ChatGPT", "components": [
		        { "component_id": "{{_compliance}}", "name": "Compliance API" } ] } },
		      { "group": { "id": "01KMKF9EBTCD8BN9PG8DJZXRSQ", "name": "{{codex}}", "components": [
		        { "component_id": "{{_web}}", "name": "Codex Web" },
		        { "component_id": "{{_cli}}", "name": "CLI" } ] } } ] },
		    "affected_components": {{affected}},
		    "ongoing_incidents": {{incidents}} } }
		""")!;

	[Fact]
	public void ForeignTroubleLeavesCodexGreen() // общий индикатор OpenAI — minor из-за Compliance API
	{
		var status = OpenAiStatus.Parse(Feed(
			$$"""[ { "component_id": "{{_compliance}}", "status": "degraded_performance" } ]""",
			$$"""[ { "name": "Delayed Costs data in the Compliance API", "affected_components": [ { "component_id": "{{_compliance}}" } ] } ]"""))!;

		Assert.Equal(("none", Health.Good, null, "Codex"), (status.Indicator, status.Health, status.Incident, status.Name));
	}

	[Fact]
	public void DegradedCodexIsAWarning()
	{
		var status = OpenAiStatus.Parse(Feed($$"""[ { "component_id": "{{_cli}}", "status": "degraded_performance" } ]"""))!;

		Assert.Equal(("minor", Health.Warning, "Degraded Performance"), (status.Indicator, status.Health, status.Description));
	}

	[Fact]
	public void WorstCodexComponentWins()
	{
		var status = OpenAiStatus.Parse(Feed($$"""
			[ { "component_id": "{{_web}}", "status": "degraded_performance" },
			  { "component_id": "{{_cli}}", "status": "full_outage" } ]
			"""))!;

		Assert.Equal(("critical", Health.Critical), (status.Indicator, status.Health));
	}

	[Fact]
	public void IncidentIsTheOneAboutCodex()
	{
		var status = OpenAiStatus.Parse(Feed(
			$$"""[ { "component_id": "{{_cli}}", "status": "partial_outage" } ]""",
			$$"""
			[ { "name": "Delayed Costs data", "affected_components": [ { "component_id": "{{_compliance}}" } ] },
			  { "name": "Codex tasks fail to start", "affected_components": [ { "component_id": "{{_cli}}" } ] } ]
			"""))!;

		Assert.Equal(("major", "Codex tasks fail to start"), (status.Indicator, status.Incident));
	}

	[Fact]
	public void NoCodexGroupMeansFallBack() => // группу переименовали — запасной путь, а не вечное «всё хорошо»
		Assert.Null(OpenAiStatus.Parse(Feed(codex: "Codex CLI")));

	[Fact]
	public void EmptyFeedIsNoGroup() => Assert.Null(OpenAiStatus.Parse(JsonNode.Parse("{}")));
}
