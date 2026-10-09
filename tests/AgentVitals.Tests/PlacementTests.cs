using Avalonia;
using Xunit;

namespace AgentVitals.Tests;

public class PlacementTests
{
	private static readonly PixelSize _window = new(400, 600);

	[Fact]
	public void BottomTaskbarPutsWindowAboveIconInsideScreen()
	{
		var point = Placement.Near(new PixelRect(1800, 1045, 24, 30), new PixelRect(0, 0, 1920, 1040), _window);

		Assert.Equal(new PixelPoint(1520, 440), point);
	}

	[Fact]
	public void TopTaskbarPutsWindowBelow()
	{
		var point = Placement.Near(new PixelRect(1000, 5, 24, 30), new PixelRect(0, 40, 1920, 1040), _window);

		Assert.Equal(new PixelPoint(812, 40), point);
	}

	[Fact]
	public void RightTaskbarPutsWindowLeftOfIcon()
	{
		var point = Placement.Near(new PixelRect(1870, 1000, 24, 30), new PixelRect(0, 0, 1860, 1080), _window);

		Assert.Equal(new PixelPoint(1460, 480), point);
	}

	[Fact]
	public void LeftTaskbarPutsWindowRightOfIcon()
	{
		var point = Placement.Near(new PixelRect(10, 500, 24, 30), new PixelRect(60, 0, 1860, 1080), _window);

		Assert.Equal(new PixelPoint(60, 215), point);
	}

	[Fact]
	public void TallWindowKeepsHeaderOnScreen() // масштаб 150% и десяток сессий
	{
		var point = Placement.Near(new PixelRect(1800, 1045, 24, 30), new PixelRect(0, 0, 1920, 1040), new PixelSize(400, 1200));

		Assert.Equal(0, point.Y);
	}

	[Fact]
	public void AutoHideTaskbarPutsWindowAboveIcon() // рабочая область — весь экран, значок внутри неё
	{
		var point = Placement.Near(new PixelRect(1800, 1050, 24, 30), new PixelRect(0, 0, 1920, 1080), _window);

		Assert.Equal(new PixelPoint(1520, 450), point);
	}

	[Fact]
	public void OverflowFlyoutPutsWindowAboveIcon() // Windows 10: значок во всплывающей области над панелью
	{
		var point = Placement.Near(new PixelRect(1500, 960, 24, 40), new PixelRect(0, 0, 1920, 1040), _window);

		Assert.Equal(new PixelPoint(1312, 360), point);
	}

	[Fact]
	public void AutoHideTopTaskbarPutsWindowBelowIcon()
	{
		var point = Placement.Near(new PixelRect(1000, 5, 24, 30), new PixelRect(0, 0, 1920, 1080), _window);

		Assert.Equal(new PixelPoint(812, 35), point);
	}

	[Fact]
	public void AutoHideRightTaskbarPutsWindowLeftOfIcon()
	{
		var point = Placement.Near(new PixelRect(1890, 500, 24, 30), new PixelRect(0, 0, 1920, 1080), _window);

		Assert.Equal(new PixelPoint(1490, 215), point);
	}

	[Fact]
	public void ToastSitsInTheCornerOfTheWorkArea() // как карточка AudioDirigent: угол над часами, а не над значком
	{
		var point = Placement.Corner(new PixelRect(1920, 40, 1920, 1040), new PixelSize(372, 120), 6);

		Assert.Equal(new PixelPoint(3462, 954), point);
	}

	[Fact]
	public void ToastStandsLeftOfAnOpenWindow() // окно у значка закрыло бы угол: тост рядом, на той же высоте
	{
		var point = Placement.Corner(new PixelRect(0, 0, 1920, 1040), new PixelSize(372, 120), 6, avoid: new PixelRect(1370, 200, 420, 840));

		Assert.Equal(new PixelPoint(998, 914), point);
	}

	[Fact]
	public void ToastKeepsTheCornerBesideAWindowElsewhere()
	{
		var point = Placement.Corner(new PixelRect(0, 0, 1920, 1040), new PixelSize(372, 120), 6, avoid: new PixelRect(0, 600, 420, 440));

		Assert.Equal(new PixelPoint(1542, 914), point);
	}

	[Fact]
	public void SecondMonitorUsesItsOwnArea()
	{
		var point = Placement.Near(new PixelRect(3700, 1045, 24, 30), new PixelRect(1920, 0, 1920, 1040), _window);

		Assert.Equal(new PixelPoint(3440, 440), point);
	}
}
