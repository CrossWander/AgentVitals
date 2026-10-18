using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace AgentVitals.Tests;

public class QuotaAlarmTests
{
	private static readonly Thresholds _default = new(75, 90);

	private static readonly DateTime Now = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

	private readonly QuotaAlarm _alarm = new();

	private static QuotaWindow Session(double used) => new(WindowKind.Session, null, used, null, Limiting: true);

	private static QuotaWindow Week(double used) => new(WindowKind.Week, null, used, null, Limiting: true);

	private List<(double Used, bool Critical)> Check(params QuotaWindow[] windows) =>
		[.. _alarm.Check("Claude", windows, _default, Now).Select(alert => (alert.Window.Used, alert.Critical))];

	private List<(double Used, QuotaLevel Level)> Levels(params QuotaWindow[] windows) =>
		[.. _alarm.Check("Claude", windows, _default, Now).Select(alert => (alert.Window.Used, alert.Level))];

	// Сессия, у которой прошла половина: ровный темп — 50%.
	private static QuotaWindow Halfway(double used) => new(WindowKind.Session, null, used, Now.AddHours(2.5), Limiting: true);

	[Fact]
	public void SwitchingAccountIsNotAReset() // прежний на 95%, новый на 10% — окно не сбрасывалось
	{
		Assert.Equal(QuotaLevel.Critical, _alarm.Check("Claude", [Session(95)], _default, Now, "a").Single().Level);

		Assert.DoesNotContain(_alarm.Check("Claude", [Session(10)], _default, Now, "b"), alert => alert.Level == QuotaLevel.Reset);
	}

	[Fact]
	public void LevelsComeFromSettings() // пороги 60 и 80 вместо 75 и 90
	{
		Assert.Empty(_alarm.Check("Claude", [Session(55)], new Thresholds(60, 80), Now));
		Assert.Equal([(61, false)], _alarm.Check("Claude", [Session(61)], new Thresholds(60, 80), Now).Select(alert => (alert.Window.Used, alert.Critical)));
		Assert.Equal([(80, true)], _alarm.Check("Claude", [Session(80)], new Thresholds(60, 80), Now).Select(alert => (alert.Window.Used, alert.Critical)));
	}

	[Theory]
	[InlineData(75, 90, false, 5, 80, 90)]
	[InlineData(85, 90, false, 5, 85, 90)] // «важно» не догоняет «критично»
	[InlineData(75, 80, true, -5, 75, 80)] // и «критично» не опускается до «важно»
	[InlineData(50, 90, false, -5, 50, 90)] // ниже 50 — уже не тревога
	[InlineData(75, 95, true, 5, 75, 95)] // выше 95 — поздно
	[InlineData(75, 90, true, -5, 75, 85)]
	public void LevelStepsByFiveWithinBounds(int important, int critical, bool moveCritical, int delta, int newImportant, int newCritical) =>
		Assert.Equal(new Thresholds(newImportant, newCritical), QuotaAlarm.Shift(new Thresholds(important, critical), moveCritical, delta));

	[Fact]
	public void WarnsOnceWhenAQuarterIsLeft()
	{
		Check(Session(70));

		Assert.Equal([(75, false)], Check(Session(75)));
		Assert.Empty(Check(Session(80)));
		Assert.Empty(Check(Session(89)));
	}

	[Fact]
	public void WarnsCriticalOnceAtNinety()
	{
		Check(Session(70));
		Check(Session(76));

		Assert.Equal([(90, true)], Check(Session(90)));
		Assert.Empty(Check(Session(95)));
		Assert.Empty(Check(Session(100)));
	}

	[Fact]
	public void JumpPastBothLevelsWarnsOnceAsCritical() // за минуту опроса бывает и так
	{
		Check(Session(70));

		Assert.Equal([(92, true)], Check(Session(92)));
		Assert.Empty(Check(Session(93)));
	}

	[Fact]
	public void AlreadyHighAtStartIsToldOnce() // запуск утром или после обновления — как раз когда важно знать
	{
		Assert.Equal([(95, true)], Check(Session(95)));
		Assert.Empty(Check(Session(97)));
	}

	[Fact]
	public void StartPastAQuarterStillWarnsAtNinety()
	{
		Assert.Equal([(80, false)], Check(Session(80)));

		Assert.Equal([(91, true)], Check(Session(91)));
	}

	[Fact]
	public void WarnsAgainAfterReset()
	{
		Check(Session(70));
		Check(Session(91));
		Check(Session(2));

		Assert.Equal([(76, false)], Check(Session(76)));
	}

	[Fact]
	public void SaysWhenACriticalWindowResets() // ждал сброса на 90% — можно работать дальше
	{
		Check(Session(70));
		Check(Session(93));

		Assert.Equal([(0, QuotaLevel.Reset)], Levels(Session(0)));
		Assert.Empty(Levels(Session(3)));
	}

	[Fact]
	public void ResetAfterOnlyImportantIsQuiet() // на 80% работа не стояла — и сброс не новость
	{
		Check(Session(80));

		Assert.Empty(Levels(Session(0)));
	}

	[Fact]
	public void RaisedThresholdIsNotAReset() // «критично» с 55% подняли до 90% — окно не сбрасывалось
	{
		_alarm.Check("Claude", [Session(56)], new Thresholds(50, 55), Now);

		Assert.Empty(_alarm.Check("Claude", [Session(57)], _default, Now));
	}

	[Fact]
	public void WarnsOnceWhenThePaceRunsOutBeforeTheReset() // половина окна прошла, а потрачено 60%
	{
		Assert.Equal([(60, QuotaLevel.Pace)], Levels(Halfway(60)));
		Assert.Empty(Levels(Halfway(63)));
		Assert.Equal([(76, QuotaLevel.Important)], Levels(Halfway(76))); // а «важно» — своим чередом
	}

	[Theory]
	[InlineData(52)] // ±5 пунктов от ровного — хватит до сброса
	[InlineData(30)]
	public void NoPaceWarningOnPace(double used) => Assert.Empty(Levels(Halfway(used)));

	[Fact]
	public void NoPaceWarningBelowHalf() // в начале окна темп скачет: 30% за первые полчаса — ещё не повод
	{
		Assert.Empty(Levels(new QuotaWindow(WindowKind.Session, null, 30, Now.AddHours(4.5), Limiting: true)));
	}

	[Fact]
	public void PaceWarnsAgainAfterReset()
	{
		Levels(Halfway(60));
		Levels(Halfway(0));

		Assert.Equal([(61, QuotaLevel.Pace)], Levels(Halfway(61)));
	}

	[Theory]
	[InlineData(QuotaLevel.Critical, true, true)]
	[InlineData(QuotaLevel.Important, true, false)] // четверть ещё есть — тихий тост
	[InlineData(QuotaLevel.Pace, true, false)] // ранний прогноз — тоже
	[InlineData(QuotaLevel.Reset, true, false)]
	[InlineData(QuotaLevel.Critical, false, false)] // звук выключен в настройках
	public void KnocksOnlyWhenAlmostUsedUp(QuotaLevel level, bool enabled, bool knocks) =>
		Assert.Equal(knocks, QuotaAlarm.Knocks(new QuotaAlert("Claude", Session(91), level), enabled));

	[Fact]
	public void SessionAndWeekWarnSeparately()
	{
		Check(Session(50), Week(70));
		Check(Session(76), Week(72));

		Assert.Equal([(77, false)], Check(Session(78), Week(77)));
	}

	[Fact]
	public void IgnoresModelWindows() // недельный Opus/Sonnet не останавливает работу
	{
		var model = new QuotaWindow(WindowKind.WeekModel, "Opus", 50, null, Limiting: false);
		Check(model);

		Assert.Empty(Check(model with { Used = 99 }));
	}

	[Fact]
	public void AgentsAreIndependent()
	{
		_alarm.Check("Claude", [Session(50)], _default, Now);
		_alarm.Check("Codex", [Session(95)], _default, Now);

		Assert.Single(_alarm.Check("Claude", [Session(91)], _default, Now));
	}

	[Fact]
	public void ToastIsAboutTheGravestOfAllAgents() // выше порога у двух разом бывает разве что при запуске — тост один, о главном
	{
		var alert = _alarm.Check([("Claude", "", [Session(80)]), ("Codex", "", [Session(92)])], _default, Now);

		Assert.Equal(("Codex", true), (alert?.Agent, alert?.Critical));
		Assert.Null(_alarm.Check([("Claude", "", [Session(85)]), ("Codex", "", [Session(93)])], _default, Now)); // о «важно» у Claude уже сказано
	}
}
