using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using Xunit;

namespace AgentVitals.Tests;

public class QuotaTests
{
	private static QuotaWindow Window(double used, bool limiting = true) =>
		new(WindowKind.Session, null, used, null, limiting);

	[Theory]
	[InlineData(0, Health.Good)]
	[InlineData(74.9, Health.Good)]
	[InlineData(75, Health.Warning)]
	[InlineData(89.9, Health.Warning)]
	[InlineData(90, Health.Critical)]
	[InlineData(130, Health.Critical)]
	public void ColoursByTheToastThresholds(double used, Health expected) => // что красное, о том и тост «критично»
		Assert.Equal(expected, Quota.Rate(used, new Thresholds(75, 90)));

	[Fact]
	public void ColoursFollowThresholdsFromSettings()
	{
		Assert.Equal(Health.Warning, Quota.Rate(55, new Thresholds(50, 60)));
		Assert.Equal(Health.Critical, Quota.Rate(60, new Thresholds(50, 60)));
	}

	[Theory]
	[InlineData(WindowKind.Session, 60, 2.5, 100.0)] // за 2,5 ч ушло 60%: оставшихся 40% хватит на 1 ч 40 мин
	[InlineData(WindowKind.Week, 80, 84, 21 * 60.0)] // за 3,5 дня 80% — ещё на 21 ч, а сброс через 84
	[InlineData(WindowKind.Session, 52, 2.5, null)] // ровный темп — хватит до сброса
	[InlineData(WindowKind.Session, 100, 2.5, null)] // уже исчерпано — прогнозировать нечего
	[InlineData(WindowKind.Model, 90, 2.5, null)] // длина окна неизвестна
	public void RunoutFollowsThePaceSoFar(WindowKind kind, double used, double hoursLeft, double? minutes)
	{
		var now = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
		var runout = Quota.Runout(new QuotaWindow(kind, null, used, now.AddHours(hoursLeft), Limiting: true), now);

		Assert.Equal(minutes, (runout - now)?.TotalMinutes is { } value ? Math.Round(value, 6) : null);
	}

	[Fact]
	public void FeatureWindowLastsAMonth() => Assert.Equal(TimeSpan.FromDays(30), QuotaWindow.LengthOf(WindowKind.MonthFeature));

	[Fact]
	public void LongMonthHasNoPaceYet() // сброс через 31 день: месяц у нас — 30 дней, метки и прогноза пока нет
	{
		var now = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
		var window = new QuotaWindow(WindowKind.MonthFeature, "chat", 10, now.AddDays(31), Limiting: true);

		Assert.Null(Quota.Expected(window, now));
		Assert.Null(Quota.Runout(window, now));
	}

	[Fact]
	public void NoRunoutWithoutReset() => Assert.Null(Quota.Runout(Window(95), DateTime.UtcNow));

	[Fact]
	public void ExhaustedWeekBeatsEmptySession() => Assert.Equal(100, Quota.Bottleneck([Window(0), Window(100)])!.Used);

	[Fact]
	public void ModelWindowDoesNotColourWhileLimitingWindowsExist() =>
		Assert.Equal(10, Quota.Bottleneck([Window(10), Window(95, limiting: false)])!.Used);

	[Fact]
	public void OnlyModelWindowsStillColour() => Assert.Equal(95, Quota.Bottleneck([Window(95, limiting: false)])!.Used);

	[Fact]
	public void NoWindowsIsUnknown() => Assert.Null(Quota.Bottleneck([]));

	[Fact]
	public void PassedResetEmptiesTheWindow() // после сна снимок старый, но сброс уже прошёл — окно точно пустое
	{
		var now = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

		Assert.Equal(0, (Window(91) with { ResetsAt = now.AddMinutes(-1) }).At(now).Used);
		Assert.Equal(91, (Window(91) with { ResetsAt = now.AddMinutes(1) }).At(now).Used);
		Assert.Equal(91, Window(91).At(now).Used);
	}

	[Theory]
	[InlineData("team", "Team")]
	[InlineData("pro", "Pro")]
	[InlineData(null, "")]
	public void NamesPlan(string? subscription, string expected) => Assert.Equal(expected, Quota.PlanName(subscription));
}

public class RetryTests
{
	private static readonly DateTime _now = new(2026, 10, 7, 12, 0, 0);

	[Fact]
	public void UsesDelta() =>
		Assert.Equal(_now.AddSeconds(120), Api.RetryAt(new RetryConditionHeaderValue(TimeSpan.FromSeconds(120)), _now));

	[Fact]
	public void ZeroDeltaWaitsFiveMinutes() =>
		Assert.Equal(_now.AddMinutes(5), Api.RetryAt(new RetryConditionHeaderValue(TimeSpan.Zero), _now));

	[Fact]
	public void MissingHeaderWaitsFiveMinutes() => Assert.Equal(_now.AddMinutes(5), Api.RetryAt(null, _now));

	[Fact]
	public void PastDateWaitsFiveMinutes() =>
		Assert.Equal(_now.AddMinutes(5), Api.RetryAt(new RetryConditionHeaderValue(new DateTimeOffset(_now.AddHours(-1))), _now));

	[Fact]
	public void UsesFutureDate() // всё время внутри — UTC: переход часов не сдвигает расписание
	{
		var at = new DateTimeOffset(_now.AddMinutes(30), TimeSpan.Zero);

		Assert.Equal(at.UtcDateTime, Api.RetryAt(new RetryConditionHeaderValue(at), _now));
	}
}
