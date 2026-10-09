using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace AgentVitals.Tests;

public class ClaudeProviderTests
{
	// Живой ответ /api/oauth/usage от 2026-10-07 (тариф Team), сокращённый до используемых полей.
	private const string _real = """
		{
		  "five_hour": { "utilization": 12.0, "resets_at": "2026-10-07T16:00:00.355842+00:00" },
		  "seven_day": { "utilization": 17.0, "resets_at": "2026-10-14T01:00:00.355870+00:00" },
		  "seven_day_opus": null,
		  "extra_usage": { "is_enabled": false, "monthly_limit": null, "used_credits": null, "decimal_places": null },
		  "limits": [
		    { "kind": "session", "group": "session", "percent": 12, "severity": "normal",
		      "resets_at": "2026-10-07T16:00:00.355842+00:00", "scope": null, "is_active": false },
		    { "kind": "weekly_all", "group": "weekly", "percent": 17, "severity": "normal",
		      "resets_at": "2026-10-14T01:00:00.355870+00:00", "scope": null, "is_active": true }
		  ],
		  "spend": { "used": { "amount_minor": 0, "currency": "USD", "exponent": 2 }, "limit": null, "enabled": false }
		}
		""";

	// Фикстура ClaudeBar (Modules/Providers/Tests/ClaudeAPITests.swift): модельный лимит и включённый spend.
	private const string _scoped = """
		{
		  "limits": [
		    { "kind": "session", "percent": 23, "resets_at": "2026-07-02T07:09:59Z", "scope": null },
		    { "kind": "weekly_all", "percent": 10, "resets_at": "2026-07-02T10:59:59Z", "scope": null },
		    { "kind": "weekly_scoped", "percent": 17, "resets_at": "2026-07-02T11:00:00Z",
		      "scope": { "model": { "id": null, "display_name": "Fable" }, "surface": null } },
		    { "kind": "weekly_scoped", "percent": 99, "resets_at": "2026-07-02T11:00:00Z",
		      "scope": { "model": { "id": null, "display_name": "Fable 5" }, "surface": null } }
		  ],
		  "spend": { "enabled": true,
		    "used":  { "amount_minor": 495,   "currency": "USD", "exponent": 2 },
		    "limit": { "amount_minor": 50000, "currency": "USD", "exponent": 2 } }
		}
		""";

	[Fact]
	public void ReadsRealResponse()
	{
		var snapshot = ClaudeProvider.Parse(JsonNode.Parse(_real)!, "Team", "me@example.com");

		Assert.Equal("Team", snapshot.Plan);
		Assert.Equal("me@example.com", snapshot.Account);
		Assert.Null(snapshot.Extra);
		Assert.Collection(
			snapshot.Windows,
			session =>
			{
				Assert.Equal(WindowKind.Session, session.Kind);
				Assert.Equal(12, session.Used);
				Assert.True(session.Limiting);
				Assert.Equal(DateTimeOffset.Parse("2026-10-07T16:00:00.355842+00:00"), session.ResetsAt);
			},
			week =>
			{
				Assert.Equal(WindowKind.Week, week.Kind);
				Assert.Equal(17, week.Used);
				Assert.True(week.Limiting);
			});
	}

	[Fact]
	public void ReadsModelLimitAndSpend()
	{
		var snapshot = ClaudeProvider.Parse(JsonNode.Parse(_scoped)!, "Max", null);

		Assert.Equal(3, snapshot.Windows.Count);

		var model = snapshot.Windows[2];
		Assert.Equal(WindowKind.WeekModel, model.Kind);
		Assert.Equal("Fable", model.Label);
		Assert.Equal(17, model.Used);
		Assert.False(model.Limiting);

		Assert.Equal(new Spend(4.95m, 500m, "USD"), snapshot.Extra);
	}

	[Fact]
	public void FallsBackWhenLimitsEmpty()
	{
		var usage = JsonNode.Parse("""
			{ "limits": [],
			  "five_hour": { "utilization": 40.0, "resets_at": "2026-10-07T16:00:00Z" },
			  "seven_day": { "utilization": 60.0, "resets_at": null } }
			""")!;

		var snapshot = ClaudeProvider.Parse(usage, "", null);

		Assert.Equal([40d, 60d], snapshot.Windows.Select(window => window.Used));
		Assert.Null(snapshot.Windows[1].ResetsAt);
	}

	[Fact]
	public void IgnoresUnknownKinds()
	{
		var usage = JsonNode.Parse("""
			{ "limits": [ { "kind": "monthly_all", "percent": 90 }, { "kind": "session", "percent": 5 } ] }
			""")!;

		var window = Assert.Single(ClaudeProvider.Parse(usage, "", null).Windows);
		Assert.Equal(WindowKind.Session, window.Kind);
	}

	[Fact]
	public void NothingUsableIsParseError() // иначе пустой снимок затёр бы последний удачный: серый значок без объяснений
	{
		var usage = JsonNode.Parse("""{ "limits": null, "five_hour": null, "seven_day": null, "spend": null, "extra_usage": null }""")!;

		Assert.Throws<JsonException>(() => ClaudeProvider.Parse(usage, "", null));
	}

	[Fact]
	public void ReadsExtraUsageCredits()
	{
		var usage = JsonNode.Parse("""
			{ "extra_usage": { "is_enabled": true, "used_credits": 541, "monthly_limit": 2000, "decimal_places": 2, "currency": "USD" } }
			""")!;

		Assert.Equal(new Spend(5.41m, 20m, "USD"), ClaudeProvider.Parse(usage, "", null).Extra);
	}
}
