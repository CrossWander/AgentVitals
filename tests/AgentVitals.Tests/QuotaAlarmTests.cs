using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace AgentVitals.Tests;

public class QuotaAlarmTests
{
	private static readonly Thresholds _default = new(75, 90);

	private readonly QuotaAlarm _alarm = new();

	private static QuotaWindow Session(double used) => new(WindowKind.Session, null, used, null, Limiting: true);

	private static QuotaWindow Week(double used) => new(WindowKind.Week, null, used, null, Limiting: true);

	private List<(double Used, bool Critical)> Check(params QuotaWindow[] windows) =>
		[.. _alarm.Check("Claude", windows, _default).Select(alert => (alert.Window.Used, alert.Critical))];

	[Fact]
	public void LevelsComeFromSettings() // пороги 60 и 80 вместо 75 и 90
	{
		Assert.Empty(_alarm.Check("Claude", [Session(55)], new Thresholds(60, 80)));
		Assert.Equal([(61, false)], _alarm.Check("Claude", [Session(61)], new Thresholds(60, 80)).Select(alert => (alert.Window.Used, alert.Critical)));
		Assert.Equal([(80, true)], _alarm.Check("Claude", [Session(80)], new Thresholds(60, 80)).Select(alert => (alert.Window.Used, alert.Critical)));
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
	public void ProvidersAreIndependent()
	{
		_alarm.Check("Claude", [Session(50)], _default);
		_alarm.Check("Codex", [Session(95)], _default);

		Assert.Single(_alarm.Check("Claude", [Session(91)], _default));
	}

	[Fact]
	public void ToastIsAboutTheGravestOfAllAgents() // выше порога у двух разом бывает разве что при запуске — тост один, о главном
	{
		var alert = _alarm.Check([("Claude", [Session(80)]), ("Codex", [Session(92)])], _default);

		Assert.Equal(("Codex", true), (alert?.Provider, alert?.Critical));
		Assert.Null(_alarm.Check([("Claude", [Session(85)]), ("Codex", [Session(93)])], _default)); // о «важно» у Claude уже сказано
	}
}
