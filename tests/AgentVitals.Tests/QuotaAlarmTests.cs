using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace AgentVitals.Tests;

public class QuotaAlarmTests
{
	private readonly QuotaAlarm _alarm = new();

	private static QuotaWindow Session(double used) => new("langWindowSession", null, used, null, Limiting: true);

	private static QuotaWindow Week(double used) => new("langWindowWeek", null, used, null, Limiting: true);

	private List<(double Used, bool Critical)> Check(params QuotaWindow[] windows) =>
		[.. _alarm.Check("Claude", windows).Select(alert => (alert.Window.Used, alert.Critical))];

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
		var model = new QuotaWindow("langWindowWeekModel", "Opus", 50, null, Limiting: false);
		Check(model);

		Assert.Empty(Check(model with { Used = 99 }));
	}

	[Fact]
	public void ProvidersAreIndependent()
	{
		_alarm.Check("Claude", [Session(50)]);
		_alarm.Check("Codex", [Session(95)]);

		Assert.Single(_alarm.Check("Claude", [Session(91)]));
	}
}
