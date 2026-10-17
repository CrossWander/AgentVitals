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
	[InlineData(80, "langHealthWarning")]
	[InlineData(60, "langHealthGood")]
	public void BadgeSaysUsedUpAtHundred(double used, string key) => Assert.Equal(key, Text.Badge(used, new Thresholds(75, 90)));

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
	public void FeatureTitleFallsBackToItsName() => // перевода нет — имя функции как есть
		Assert.Equal("agent_mode", Text.Title(new QuotaWindow(WindowKind.MonthFeature, "agent_mode", 0, null, Limiting: true)));

	[Fact]
	public void MissingCostIsADash() => Assert.Equal("—", Text.Cost(null));

	[Theory]
	[InlineData(0.5, "langUpdatedNow")]
	[InlineData(605, "langUpdatedAgo")] // вспомненный после ночи ответ: «10 ч 5 мин назад»
	[InlineData(3000, "langUpdatedAt")] // после выходных: «обновлено пт 18:40», а не «50 ч назад»
	public void AgeOfTheAnswerReadsAtAGlance(double minutes, string key)
	{
		var now = System.DateTime.UtcNow;

		Assert.Equal(key, Text.Ago(now.AddMinutes(-minutes), now));
	}

	[Fact]
	public void TurnShowsHowLongItRuns() // «работает · 3 мин»; первую минуту — без таймера
	{
		var now = System.DateTime.UtcNow;
		var session = new Session("s", SessionState.Working, null, "repo", null, TurnStarted: now.AddMinutes(-3));

		Assert.Equal("langSessionWorking · langMinutes", Text.Session(session, now));
		Assert.Equal("langSessionWorking", Text.Session(session with { TurnStarted = now.AddSeconds(-20) }, now));
	}

	[Fact]
	public void QuotaToastTellsWhenItRunsOut() // «осталось 40%, хватит до 13:40, сброс через 2 ч 30 мин»
	{
		var now = System.DateTime.UtcNow;
		var window = new QuotaWindow(WindowKind.Session, null, 60, now.AddHours(2.5), Limiting: true);

		Assert.Equal(("langAlertPace", "langAlertLeft, langPaceRunout, langResetsIn"), Text.QuotaToast(new QuotaAlert("Claude", window, QuotaLevel.Pace), now));
		Assert.Equal(("langAlertImportant", "langAlertLeft, langResetsIn"), Text.QuotaToast(new QuotaAlert("Claude", window with { Used = 76, ResetsAt = now.AddHours(0.5) }, QuotaLevel.Important), now));
	}

	[Fact]
	public void ResetToastSaysTheWindowIsBack() =>
		Assert.Equal(("langAlertReset", "langAlertResetText"),
			Text.QuotaToast(new QuotaAlert("Claude", new QuotaWindow(WindowKind.Session, null, 0, null, Limiting: true), QuotaLevel.Reset), System.DateTime.UtcNow));

	[Fact]
	public void SessionToastSaysWhatHappened()
	{
		var session = new Session("s", SessionState.Waiting, null, "repo", null);

		Assert.Equal(("langToastWaiting", "repo"), Text.SessionToast(new SessionAlert(session, Waiting: true)));
		Assert.Equal("langToastDone", Text.SessionToast(new SessionAlert(session with { State = SessionState.Idle, TurnDuration = System.TimeSpan.FromMinutes(12) }, Waiting: false)).Title);
	}
}
