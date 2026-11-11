using System;
using Avalonia;
using Xunit;

namespace AgentVitals.Tests;

/// <summary>Окно у трея закрывается само — по курсору и часам, без окон: настоящее окно держится на Win32.</summary>
public class AutoCloseTests
{
	private static readonly DateTime _t = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
	private static readonly PixelRect _window = new(1488, 140, 432, 892);
	private static readonly PixelRect _icon = new(1745, 1040, 24, 24);
	private static readonly PixelPoint _onIcon = new(1757, 1052);
	private static readonly PixelPoint _inside = new(1700, 500);
	private static readonly PixelPoint _away = new(400, 300);

	[Fact]
	public void ClosesSecondsAfterTheMouseLeaves() // сперва гаснет 0,3 с, потом закрывается
	{
		var close = Opened(_onIcon);

		Assert.Equal(
			new[] { AutoCloseStep.Stay, AutoCloseStep.Stay, AutoCloseStep.Fade, AutoCloseStep.Close },
			new[] { Step(close, _away, 0), Step(close, _away, 2900), Step(close, _away, 3000), Step(close, _away, 3300) });
	}

	[Fact]
	public void StaysWhileTheMouseIsNear() // над окном и над значком сколько угодно; отсчёт — с ухода, а не с открытия
	{
		var close = Opened(_onIcon);

		Assert.Equal(
			new[] { AutoCloseStep.Stay, AutoCloseStep.Stay, AutoCloseStep.Stay, AutoCloseStep.Stay, AutoCloseStep.Fade },
			new[] { Step(close, _inside, 0), Step(close, _inside, 60_000), Step(close, _onIcon, 120_000), Step(close, _away, 120_000), Step(close, _away, 123_000) });
	}

	[Fact]
	public void AFewPointsPastTheEdgeStillCount() // промах на пару точек мимо окна не запускает отсчёт
	{
		var close = Opened(_onIcon);
		var edge = new PixelPoint(_window.X - 20, 500);
		var past = new PixelPoint(_window.X - 40, 500);

		Assert.Equal(
			new[] { AutoCloseStep.Stay, AutoCloseStep.Stay, AutoCloseStep.Fade },
			new[] { Step(close, edge, 60_000), Step(close, past, 60_000), Step(close, past, 63_000) });
	}

	[Fact]
	public void WaitsUntilTheMouseMoves() // открыли щелчком по плашке: окно читают, не трогая мышь, а дрожь руки — не движение
	{
		var notch = new PixelPoint(960, 5);
		var close = Opened(notch);

		Assert.Equal(
			new[] { AutoCloseStep.Stay, AutoCloseStep.Stay, AutoCloseStep.Stay, AutoCloseStep.Fade },
			new[] { Step(close, notch, 60_000), Step(close, new(965, 9), 61_000), Step(close, new(990, 40), 62_000), Step(close, new(990, 40), 65_000) });
	}

	[Fact]
	public void MouseBackStartsTheCountOver() // и пока окно гаснет — оно остаётся
	{
		var close = Opened(_onIcon);
		Step(close, _away, 0);

		Assert.Equal(
			new[] { AutoCloseStep.Fade, AutoCloseStep.Stay, AutoCloseStep.Stay, AutoCloseStep.Stay, AutoCloseStep.Fade },
			new[] { Step(close, _away, 3100), Step(close, _inside, 3200), Step(close, _away, 4000), Step(close, _away, 6900), Step(close, _away, 7000) });
	}

	[Fact]
	public void KeyInTheWindowStartsTheCountOver() // Ctrl+1…9 и F5 — с окном работают и без мыши
	{
		var close = Opened(_onIcon);
		Step(close, _away, 0);
		Step(close, _away, 2000);

		close.Touch();

		Assert.Equal(
			new[] { AutoCloseStep.Stay, AutoCloseStep.Stay, AutoCloseStep.Fade },
			new[] { Step(close, _away, 2100), Step(close, _away, 5000), Step(close, _away, 5100) });
	}

	[Fact]
	public void ByHandNeverCloses() // «Вручную» — как до настройки: ✕, значок или Esc
	{
		var close = Opened(_onIcon);
		close.Next(_away, _window, _icon, _t, 0);

		Assert.Equal(AutoCloseStep.Stay, close.Next(_away, _window, _icon, At(3_600_000), 0));
	}

	[Fact]
	public void OpeningAgainForgetsTheLastTime() // прошлый уход мыши не закрывает окно, открытое заново
	{
		var close = Opened(_onIcon);
		Step(close, _away, 0);
		Step(close, _away, 2000);

		close.Open(_away);

		Assert.Equal(
			new[] { AutoCloseStep.Stay, AutoCloseStep.Stay },
			new[] { Step(close, _away, 4000), Step(close, _away, 60_000) });
	}

	private static AutoClose Opened(PixelPoint cursor)
	{
		var close = new AutoClose();
		close.Open(cursor);

		return close;
	}

	/// <summary>Шаг с настройкой «3 с».</summary>
	private static AutoCloseStep Step(AutoClose close, PixelPoint cursor, int milliseconds) =>
		close.Next(cursor, _window, _icon, At(milliseconds), 3);

	private static DateTime At(int milliseconds) => _t.AddMilliseconds(milliseconds);
}
