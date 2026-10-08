using Xunit;

namespace AgentVitals.Tests;

// Без запущенного приложения Localization отдаёт ключ строки — по нему и видно, какая подпись выбрана.
public class TextTests
{
	[Theory]
	[InlineData(100, "langHealthExhausted")]
	[InlineData(130, "langHealthExhausted")]
	[InlineData(99.6, "langHealthExhausted")] // на карточке уже «100%»
	[InlineData(99.4, "langHealthCritical")]
	[InlineData(60, "langHealthWarning")]
	[InlineData(10, "langHealthGood")]
	public void BadgeSaysUsedUpAtHundred(double used, string key) => Assert.Equal(key, Text.Badge(used));

	[Theory]
	[InlineData(999L, "langUnits", 999.0)]
	[InlineData(12_400L, "langThousands", 12.4)]
	[InlineData(152_345_678L, "langMillions", 152.345678)]
	[InlineData(2_800_000_000L, "langBillions", 2.8)]
	public void TokensScaleToTheirOrder(long count, string key, double value)
	{
		var scaled = Text.Scale(count);

		Assert.Equal(key, scaled.Key);
		Assert.Equal(value, scaled.Value, 6);
	}

	[Fact]
	public void MissingCostIsADash() => Assert.Equal("—", Text.Cost(null));

	[Fact]
	public void TurnShowsHowLongItRuns() // «работает · 3 мин»; первую минуту — без таймера
	{
		var now = System.DateTime.UtcNow;
		var session = new LiveSession("s", SessionState.Working, null, "repo", null, Since: now.AddMinutes(-3));

		Assert.Equal("langSessionWorking · langMinutes", Text.Session(session, now));
		Assert.Equal("langSessionWorking", Text.Session(session with { Since = now.AddSeconds(-20) }, now));
	}

	[Fact]
	public void SessionToastSaysWhatHappened()
	{
		var session = new LiveSession("s", SessionState.Waiting, null, "repo", null);

		Assert.Equal(("langToastWaiting", "repo"), Text.SessionToast(new SessionAlert(session, Waiting: true)));
		Assert.Equal("langToastDone", Text.SessionToast(new SessionAlert(session with { State = SessionState.Idle, Took = System.TimeSpan.FromMinutes(12) }, Waiting: false)).Title);
	}
}
