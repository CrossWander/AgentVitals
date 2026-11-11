using System;
using Xunit;

namespace AgentVitals.Tests;

/// <summary>«Не беспокоить»: до какого момента тихо. Утро — по местным часам машины, поэтому и тест считает от них.</summary>
public class QuietTests
{
	private static readonly DateTime _now = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

	[Fact]
	public void HalfHourAndHourCountFromNow() =>
		Assert.Equal((_now.AddMinutes(30), _now.AddHours(1)), (Quiet.Until(QuietSpan.HalfHour, _now), Quiet.Until(QuietSpan.Hour, _now)));

	[Theory]
	[InlineData(23, 12)] // вечером — до восьми завтра
	[InlineData(3, 11)] // ночью — до восьми этого же утра
	[InlineData(8, 12)] // ровно в восемь утро уже настало — до следующего
	public void MorningIsTheNextEightOClock(int hour, int day)
	{
		var local = new DateTime(2026, 11, 11, hour, 0, 0, DateTimeKind.Local);

		Assert.Equal(new DateTime(2026, 11, day, 8, 0, 0), Quiet.Until(QuietSpan.Morning, local.ToUniversalTime()).ToLocalTime());
	}

	[Fact]
	public void QuietOnlyBeforeItsEnd() // время вышло — снова звучит; «пока не выключу» само не кончается
	{
		var until = _now.AddMinutes(30);

		Assert.Equal(
			new[] { false, true, false, true },
			new[] { Quiet.On(null, _now), Quiet.On(until, until.AddSeconds(-1)), Quiet.On(until, until), Quiet.On(Quiet.Until(QuietSpan.Forever, _now), _now.AddYears(50)) });
	}

	[Fact]
	public void LineSaysUntilWhen() // в карточке у значка и в шапке окна; выключено или время вышло — строки нет
	{
		Assert.Equal(
			new[] { "", "", "langQuietLine", "langQuietLineForever" },
			new[] { Text.QuietLine(null, _now), Text.QuietLine(_now.AddHours(-1), _now), Text.QuietLine(_now.AddHours(1), _now), Text.QuietLine(Quiet.Forever, _now) });
	}
}
