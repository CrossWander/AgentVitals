using System;
using Xunit;

namespace AgentVitals.Tests;

public class SessionAlarmTests
{
	private static readonly TimeSpan _fiveMinutes = TimeSpan.FromMinutes(5);

	private static LiveSession Session(SessionState state, TimeSpan? took = null) => new("s", state, null, "repo", null, Took: took);

	[Fact]
	public void LongTurnFinishedIsTold()
	{
		var alert = Assert.Single(SessionAlarm.Check([Session(SessionState.Working)], [Session(SessionState.Idle, TimeSpan.FromMinutes(12))], _fiveMinutes));

		Assert.False(alert.Waiting);
	}

	[Fact]
	public void ShortTurnIsNotTold() => // за минуту человек и так смотрит в терминал
		Assert.Empty(SessionAlarm.Check([Session(SessionState.Working)], [Session(SessionState.Idle, TimeSpan.FromMinutes(1))], _fiveMinutes));

	[Fact]
	public void EscIsNotTold() => // ход прервал сам человек: длительности нет
		Assert.Empty(SessionAlarm.Check([Session(SessionState.Working)], [Session(SessionState.Idle)], TimeSpan.Zero));

	[Fact]
	public void PermissionRequestIsToldAtOnce()
	{
		var alert = Assert.Single(SessionAlarm.Check([Session(SessionState.Working)], [Session(SessionState.Waiting)], _fiveMinutes));

		Assert.True(alert.Waiting);
	}

	[Fact]
	public void FirstReadIsSilent() => // после запуска трея неизвестно, что было до него
		Assert.Empty(SessionAlarm.Check([], [Session(SessionState.Waiting)], _fiveMinutes));

	[Fact]
	public void UnchangedStateIsSilent() =>
		Assert.Empty(SessionAlarm.Check([Session(SessionState.Waiting)], [Session(SessionState.Waiting)], _fiveMinutes));
}
