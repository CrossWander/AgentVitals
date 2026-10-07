using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace AgentVitals.Tests;

public class QuotaAlarmTests
{
	private readonly QuotaAlarm _alarm = new();

	private static QuotaWindow Session(double used) => new("langWindowSession", null, used, null, Limiting: true);

	private static QuotaWindow Week(double used) => new("langWindowWeek", null, used, null, Limiting: true);

	private List<double> Check(params QuotaWindow[] windows) =>
		_alarm.Check("Claude", windows).Select(window => window.Used).ToList();

	[Fact]
	public void FiresOnceWhenSessionReachesNinety()
	{
		Check(Session(85));

		Assert.Equal([90], Check(Session(90)));
		Assert.Empty(Check(Session(95)));
		Assert.Empty(Check(Session(100)));
	}

	[Fact]
	public void FirstSnapshotIsReference() // после запуска или входа красный значок и так виден
	{
		Assert.Empty(Check(Session(95)));
		Assert.Empty(Check(Session(97)));
	}

	[Fact]
	public void FiresAgainAfterReset()
	{
		Check(Session(85));
		Check(Session(91));
		Check(Session(2));

		Assert.Equal([92], Check(Session(92)));
	}

	[Fact]
	public void SessionAndWeekFireSeparately()
	{
		Check(Session(50), Week(80));
		Check(Session(91), Week(85));

		Assert.Equal([93], Check(Session(92), Week(93)));
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
