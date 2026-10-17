using System;
using Xunit;

namespace AgentVitals.Tests;

public class SessionAlarmTests
{
	private static readonly TimeSpan _fiveMinutes = TimeSpan.FromMinutes(5);

	private static Session Live(SessionState state, TimeSpan? took = null, string id = "s") => new(id, state, null, "repo", null, TurnDuration: took);

	[Fact]
	public void LongTurnFinishedIsTold()
	{
		var alert = SessionAlarm.Check([Live(SessionState.Working)], [Live(SessionState.Idle, TimeSpan.FromMinutes(12))], _fiveMinutes);

		Assert.False(alert?.Waiting);
	}

	[Fact]
	public void ShortTurnIsNotTold() => // за минуту человек и так смотрит в терминал
		Assert.Null(SessionAlarm.Check([Live(SessionState.Working)], [Live(SessionState.Idle, TimeSpan.FromMinutes(1))], _fiveMinutes));

	[Fact]
	public void EscIsNotTold() => // ход прервал сам человек: длительности нет
		Assert.Null(SessionAlarm.Check([Live(SessionState.Working)], [Live(SessionState.Idle)], TimeSpan.Zero));

	[Fact]
	public void PermissionRequestIsToldAtOnce()
	{
		var alert = SessionAlarm.Check([Live(SessionState.Working)], [Live(SessionState.Waiting)], _fiveMinutes);

		Assert.True(alert?.Waiting);
	}

	[Fact]
	public void PermissionRequestOutranksFinishedTurn() // вопрос о разрешении держит Claude, пока не ответят
	{
		var alert = SessionAlarm.Check(
			[Live(SessionState.Working, id: "a"), Live(SessionState.Working, id: "b")],
			[Live(SessionState.Idle, TimeSpan.FromMinutes(12), id: "a"), Live(SessionState.Waiting, id: "b")],
			_fiveMinutes);

		Assert.Equal("b", alert?.Session.Id);
	}

	[Fact]
	public void FirstReadIsSilent() => // после запуска трея неизвестно, что было до него
		Assert.Null(SessionAlarm.Check([], [Live(SessionState.Waiting)], _fiveMinutes));

	[Fact]
	public void UnchangedStateIsSilent() =>
		Assert.Null(SessionAlarm.Check([Live(SessionState.Waiting)], [Live(SessionState.Waiting)], _fiveMinutes));

	[Theory]
	[InlineData(true, true, false, true)]
	[InlineData(false, true, false, false)] // конец хода — тихий тост: агент не стоит
	[InlineData(true, false, false, false)] // звук выключен в настройках
	[InlineData(true, true, true, false)] // человек и так в окне сессии — вопрос перед глазами
	public void ChimesOnlyWhenTheAgentWaits(bool waiting, bool enabled, bool inFront, bool chimes) =>
		Assert.Equal(chimes, SessionAlarm.Chimes(new SessionAlert(Live(SessionState.Waiting), waiting), enabled, inFront));

	[Fact]
	public void ChimeIsBuiltIn() // звук внутри exe: файла рядом нет, и пропасть ему неоткуда
	{
		var wave = Sound.Chime;

		Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wave, 0, 4));
		Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wave, 8, 4));
	}
}
