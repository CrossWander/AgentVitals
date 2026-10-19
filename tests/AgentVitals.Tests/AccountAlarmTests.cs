using System;
using Xunit;

namespace AgentVitals.Tests;

public class AccountAlarmTests
{
	private static readonly DateTime _now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

	private readonly AccountAlarm _alarm = new();

	private static QuotaWindow Window(WindowKind kind, double used, DateTime resets) => new(kind, null, used, new DateTimeOffset(resets), Limiting: true);

	private static Account Spent(string key, params QuotaWindow[] windows) => new(key, new Snapshot("Max", null, windows, null), _now.AddHours(-1), null);

	private static Account Fresh(string key) => Spent(key, Window(WindowKind.Session, 10, _now.AddHours(3)));

	private AccountBack? Check(string? inUse, DateTime now, params Account[] accounts) => _alarm.Check([("Claude", inUse, accounts)], now);

	[Fact]
	public void TellsWhenRememberedAccountIsBack()
	{
		var a = Spent("A", Window(WindowKind.Session, 100, _now.AddHours(1)));

		Assert.Null(Check("B", _now, a, Fresh("B")));
		Assert.Equal(new AccountBack("Claude", "A"), Check("B", _now.AddHours(2), a, Fresh("B")));
		Assert.Null(Check("B", _now.AddHours(3), a, Fresh("B")));
	}

	[Fact]
	public void QuietWhileWeekStillExhausted() // сессия сбросилась, а неделя ещё на 100% — работать всё равно нельзя
	{
		var a = Spent("A", Window(WindowKind.Session, 100, _now.AddHours(1)), Window(WindowKind.Week, 100, _now.AddDays(3)));

		Check("B", _now, a, Fresh("B"));

		Assert.Null(Check("B", _now.AddHours(2), a, Fresh("B")));
	}

	[Fact]
	public void QuietAboutAccountInUse() // о нём — тосты квоты, как в 1.3.0
	{
		var a = Spent("A", Window(WindowKind.Session, 100, _now.AddHours(1)));

		Check("A", _now, a);

		Assert.Null(Check("A", _now.AddHours(2), a));
	}

	[Fact]
	public void QuietAtStartupWhenAlreadyBack() // сбросилось, пока программа не работала: видно по плашке
	{
		var a = Spent("A", Window(WindowKind.Session, 100, _now.AddHours(1)));

		Assert.Null(Check("B", _now.AddHours(2), a, Fresh("B")));
	}
}
