using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

		Assert.Equal(new ExtraUsage(4.95m, 500m, "USD"), snapshot.Extra);
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

		Assert.Equal(new ExtraUsage(5.41m, 20m, "USD"), ClaudeProvider.Parse(usage, "", null).Extra);
	}

	// Вход в «Диспетчере учётных данных» — по реверсу pitboard: на живом режиме не проверено, «Диспетчер» здесь — словарь.
	private static string Login(string token, long expires) => "{\"claudeAiOauth\":{\"accessToken\":\"" + token + "\",\"expiresAt\":" + expires + "}}";

	private static readonly string _pieces = Convert.ToBase64String(Encoding.UTF8.GetBytes(Login("new", 2)));

	private static Dictionary<string, string> Chunked(string meta) => new()
	{
		[ClaudeProvider.Vault] = Login("old", 1), // целая запись, оставшаяся с прошлого раза
		[ClaudeProvider.Vault + "#m"] = meta,
		[ClaudeProvider.Vault + "#0"] = _pieces[..10],
		[ClaudeProvider.Vault + "#1"] = _pieces[10..],
	};

	[Fact]
	public void ReadsWholeVaultEntry() =>
		Assert.Equal(Login("a", 1), ClaudeProvider.FromVault(new Dictionary<string, string> { [ClaudeProvider.Vault] = Login("a", 1) }.GetValueOrDefault));

	[Fact]
	public void JoinsVaultPieces() => // куски важнее целой записи
		Assert.Equal(Login("new", 2), ClaudeProvider.FromVault(Chunked($$"""{"n":2,"l":{{_pieces.Length}}}""").GetValueOrDefault));

	[Theory]
	[InlineData(3, 0)] // куска #2 нет
	[InlineData(2, 1)] // длина не сошлась
	[InlineData(0, 0)]
	[InlineData(257, 0)]
	public void BrokenPiecesMeanNoEntry(int count, int extra) =>
		Assert.Null(ClaudeProvider.FromVault(Chunked($$"""{"n":{{count}},"l":{{_pieces.Length + extra}}}""").GetValueOrDefault));

	[Fact]
	public void PiecesLongerThanTheyMayBeMeanNoEntry() => // больше n·2400 знаков
		Assert.Null(ClaudeProvider.FromVault(Chunked("""{"n":2,"l":4801}""").GetValueOrDefault));

	[Fact]
	public void NeverReadsThePending() // #p — недописанная запись: Claude Code ещё пишет куски
	{
		List<string> asked = [];
		var vault = Chunked($$"""{"n":2,"l":{{_pieces.Length}}}""");

		ClaudeProvider.FromVault(target =>
		{
			asked.Add(target);

			return vault.GetValueOrDefault(target);
		});

		Assert.DoesNotContain(asked, target => target.EndsWith("#p", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData(1000, 2000, "vault")] // новый режим: файл остался запасным и устарел
	[InlineData(2000, 1000, "file")] // режим выключили: старая запись не мешает файлу
	public void PicksLaterExpiry(long file, long vault, string winner) =>
		Assert.Equal(winner, (string?)ClaudeProvider.Pick(Login("file", file), Login("vault", vault))!["accessToken"]);

	[Fact]
	public void FileAloneOrVaultAloneIsEnough()
	{
		Assert.Equal("file", (string?)ClaudeProvider.Pick(Login("file", 1), null)!["accessToken"]);
		Assert.Equal("vault", (string?)ClaudeProvider.Pick(null, Login("vault", 1))!["accessToken"]);
		Assert.Equal("file", (string?)ClaudeProvider.Pick(Login("file", 1), "not json")!["accessToken"]);
		Assert.Null(ClaudeProvider.Pick(null, null));
	}
}
