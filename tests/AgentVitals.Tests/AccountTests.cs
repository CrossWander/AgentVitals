using System;
using Xunit;

namespace AgentVitals.Tests;

public class AccountTests
{
	private static readonly DateTime _now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

	private static QuotaWindow Window(WindowKind kind, double used, DateTime? resets, bool limiting = true) =>
		new(kind, null, used, resets is { } at ? new DateTimeOffset(at) : null, limiting);

	private static Account With(params QuotaWindow[] windows) => new("a", new Snapshot("Max", "a@x.z", windows, null), _now.AddHours(-3), null);

	[Fact]
	public void SessionAndWeekExhaustedWaitsForWeek()
	{
		var (state, blocker) = With(Window(WindowKind.Session, 100, _now.AddHours(1)), Window(WindowKind.Week, 100, _now.AddDays(3))).Availability(_now);

		Assert.Equal(Availability.Busy, state);
		Assert.Equal(WindowKind.Week, blocker?.Kind);
	}

	[Fact]
	public void SessionResetWeekExhaustedStaysBusy()
	{
		var (state, blocker) = With(Window(WindowKind.Session, 100, _now.AddMinutes(-1)), Window(WindowKind.Week, 100, _now.AddDays(3))).Availability(_now);

		Assert.Equal(Availability.Busy, state);
		Assert.Equal(WindowKind.Week, blocker?.Kind);
	}

	[Fact]
	public void AllExhaustedResetIsBack() =>
		Assert.Equal((Availability.Back, null), With(Window(WindowKind.Session, 100, _now.AddMinutes(-1))).Availability(_now));

	[Fact]
	public void AlmostFullIsFree() =>
		Assert.Equal((Availability.Free, null), With(Window(WindowKind.Session, 99.4, _now.AddHours(1))).Availability(_now));

	[Fact]
	public void RoundedUpIsExhausted() =>
		Assert.Equal(Availability.Busy, With(Window(WindowKind.Session, 99.6, _now.AddHours(1))).Availability(_now).State);

	[Fact]
	public void NonLimitingWindowIsIgnored() =>
		Assert.Equal((Availability.Free, null), With(Window(WindowKind.Month, 100, _now.AddDays(3), limiting: false)).Availability(_now));

	[Fact]
	public void NoSnapshotIsFree() => Assert.Equal((Availability.Free, null), new Account("a", null, null, null).Availability(_now));

	[Fact]
	public void UnknownResetWaitsLongest()
	{
		var (_, blocker) = With(Window(WindowKind.Session, 100, null), Window(WindowKind.Week, 100, _now.AddDays(3))).Availability(_now);

		Assert.Equal(WindowKind.Session, blocker?.Kind);
	}

	[Fact]
	public void WindowsAfterResetAreEmpty() =>
		Assert.Equal(0, With(Window(WindowKind.Session, 100, _now.AddMinutes(-1))).Windows(_now)[0].Used);
}
