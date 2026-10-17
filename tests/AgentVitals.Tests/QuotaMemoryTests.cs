using System;
using System.Collections.Generic;
using System.IO;
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
