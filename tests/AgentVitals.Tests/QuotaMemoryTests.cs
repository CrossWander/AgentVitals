using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace AgentVitals.Tests;

public class QuotaMemoryTests
{
	private static readonly DateTime _now = new(2026, 10, 14, 19, 25, 0, DateTimeKind.Utc);

	private static readonly Snapshot _answer = new(
		"Max",
		"you@example.com",
		[
			new(WindowKind.Session, null, 41, new DateTimeOffset(2026, 10, 14, 22, 0, 0, TimeSpan.Zero), Limiting: true),
			new(WindowKind.WeekModel, "Opus", 12.5, null, Limiting: false),
		],
		new ExtraUsage(3.20m, 50m, "USD"));

	[Fact]
	public void LastAnswerSurvivesARestart() // после перезапуска окно сразу с цифрами, а не пустое
	{
		using var temp = new TempDirectory();
		var file = temp.File("quota.json");

		QuotaMemory.Save(file, new Dictionary<string, RememberedQuota> { ["Claude"] = new(_answer, _now, _now.AddMinutes(5)) });
		var memory = QuotaMemory.Load(file)["Claude"];

		Assert.Equal(_answer.Windows, memory.Snapshot!.Windows);
		Assert.Equal((_answer.Plan, _answer.Account, _answer.Extra), (memory.Snapshot.Plan, memory.Snapshot.Account, memory.Snapshot.Extra));
		Assert.Equal((_now, _now.AddMinutes(5)), (memory.Updated, memory.RetryAt));
	}

	[Fact]
	public void AccountsSurviveRestart() // запомненные аккаунты и какой из них в работе
	{
		using var temp = new TempDirectory();
		var file = temp.File("quota.json");
		Account[] accounts = [new("A", _answer, _now.AddHours(-2), null), new("B", _answer, _now, _now.AddMinutes(5))];

		QuotaMemory.Save(file, new Dictionary<string, RememberedQuota> { ["Claude"] = new(_answer, _now, _now.AddMinutes(5), "B", accounts) });
		var state = new AgentState(new ClaudeAgent());
		state.Recall(QuotaMemory.Load(file)["Claude"], _now);

		Assert.Equal("B", state.Key);
		Assert.Equal(["A", "B"], state.Accounts.Select(account => account.Key));
		Assert.Equal(_now.AddHours(-2), state.Accounts[0].Updated);
		Assert.IsType<RateLimitedException>(state.Error);
	}

	[Fact]
	public void LegacyFileLoadsWithoutAccounts() // файл от 1.3.0: аккаунт ещё неизвестен, цифры видны
	{
		using var temp = new TempDirectory();
		var file = temp.File("quota.json");
		File.WriteAllText(file, """{"Claude":{"snapshot":{"plan":"Max","account":"you@example.com","windows":[],"extra":null},"updated":"2026-10-14T19:00:00Z","retryAt":null}}""");

		var memory = QuotaMemory.Load(file)["Claude"];
		var state = new AgentState(new ClaudeAgent());
		state.Recall(memory, _now);

		Assert.Null(memory.Key);
		Assert.Null(memory.Accounts);
		Assert.Null(state.Key);
		Assert.Empty(state.Accounts);
		Assert.Equal("you@example.com", state.Snapshot?.Account);
	}

	[Fact]
	public void NewFileKeepsTopLevelForOlderVersions() // откат на 1.3.0 видит цифры аккаунта в работе
	{
		using var temp = new TempDirectory();
		var file = temp.File("quota.json");

		QuotaMemory.Save(file, new Dictionary<string, RememberedQuota> { ["Claude"] = new(_answer, _now, null, "A", [new("A", _answer, _now, null)]) });
		var claude = JsonDocument.Parse(File.ReadAllText(file)).RootElement.GetProperty("Claude");

		Assert.True(claude.TryGetProperty("snapshot", out _));
		Assert.True(claude.TryGetProperty("updated", out _));
		Assert.Equal("A", claude.GetProperty("key").GetString());
		Assert.Equal(1, claude.GetProperty("accounts").GetArrayLength());
	}

	[Fact]
	public void MissingOrBrokenMemoryIsEmpty() // испорченный файл — не повод не запуститься
	{
		using var temp = new TempDirectory();
		var file = temp.File("quota.json");

		Assert.Empty(QuotaMemory.Load(file));

		File.WriteAllText(file, "{ not json");
		Assert.Empty(QuotaMemory.Load(file));
	}

	[Fact]
	public void FreshAnswerIsNotAskedAgainAtStart() // перезапуск не тратит лимит запросов
	{
		var state = new AgentState(new ClaudeAgent());

		state.Recall(new(_answer, _now.AddSeconds(-30), null), _now);

		Assert.Same(_answer, state.Snapshot);
		Assert.Equal(_now.AddSeconds(90), state.NextFetch(120));
	}

	[Fact]
	public void OldAnswerIsAskedAtOnce() // после ночи — сразу за свежими данными, а старые пока видны
	{
		var state = new AgentState(new ClaudeAgent());

		state.Recall(new(_answer, _now.AddHours(-10), null), _now);

		Assert.True(state.NextFetch(120) <= _now);
		Assert.Equal(_now.AddHours(-10), state.Updated);
	}

	[Fact]
	public void ServerWaitOutlivesARestart() // сервер велел ждать до 19:30 — перезапуск в 19:25 не спрашивает раньше
	{
		var state = new AgentState(new ClaudeAgent());

		state.Recall(new(null, null, _now.AddMinutes(5)), _now);

		Assert.Equal(_now.AddMinutes(5), state.NextFetch(120));
		Assert.Equal(_now.AddMinutes(5), state.Remember().RetryAt);
	}

	[Fact]
	public void PassedWaitIsForgotten()
	{
		var state = new AgentState(new ClaudeAgent());

		state.Recall(new(_answer, _now.AddHours(-1), _now.AddMinutes(-1)), _now);

		Assert.Null(state.Error);
		Assert.True(state.NextFetch(120) <= _now);
	}

	[Fact]
	public void RemembersTheAnswerAndItsTime()
	{
		var state = new AgentState(new ClaudeAgent()) { Snapshot = _answer, Updated = _now };

		Assert.Equal(new RememberedQuota(_answer, _now, null), state.Remember());
	}
}
