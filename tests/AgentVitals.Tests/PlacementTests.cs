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
	public void SecondMonitorUsesItsOwnArea()
	{
		var point = Placement.Near(new PixelRect(3700, 1045, 24, 30), new PixelRect(1920, 0, 1920, 1040), _window);

		Assert.Equal(new PixelPoint(3440, 440), point);
	}

	private static readonly PixelRect _work = new(0, 0, 1920, 1040);

	[Fact]
	public void NotchHangsFromTheTopCentre() => Assert.Equal(new PixelPoint(900, 0), Placement.Notch(_work, new PixelSize(120, 4)));

	[Fact]
	public void NotchSitsUnderATopTaskbar() => Assert.Equal(new PixelPoint(900, 40), Placement.Notch(new PixelRect(0, 40, 1920, 1040), new PixelSize(120, 30)));

	[Fact]
	public void ChipTakesTheCornerNearTheTray() // панель внизу — правый нижний; слева — левый нижний
	{
		Assert.Equal(new PixelPoint(1920 - 200 - 12, 1040 - 34 - 12), Placement.Chip(new PixelRect(1800, 1050, 24, 24), _work, new PixelSize(200, 34), 12));
		Assert.Equal(new PixelPoint(60 + 12, 1040 - 34 - 12), Placement.Chip(new PixelRect(10, 1000, 24, 24), new PixelRect(60, 0, 1860, 1040), new PixelSize(200, 34), 12));
	}

	[Fact]
	public void DeskKeepsItsPlaceWhileTheScreenIsThere()
	{
		PixelRect[] two = [new(0, 0, 1920, 1080), new(1920, 0, 1920, 1080)];

		Assert.Equal(new PixelPoint(2500, 300), Placement.Desk(new PixelPoint(2500, 300), two, _work, new PixelSize(400, 200), 24));
		Assert.Equal(new PixelPoint(1920 - 400 - 24, 24), Placement.Desk(new PixelPoint(2500, 300), [two[0]], _work, new PixelSize(400, 200), 24));
		Assert.Equal(new PixelPoint(1920 - 400 - 24, 24), Placement.Desk(null, two, _work, new PixelSize(400, 200), 24));
	}

	[Fact]
	public void DeskStaysPressedToTheEdge() // поле под тень: карточка у левого и верхнего края — угол окна за экраном
	{
		Assert.Equal(new PixelPoint(-8, -8), Placement.Desk(new PixelPoint(-8, -8), [new(0, 0, 1920, 1080)], _work, new PixelSize(400, 200), 24));
	}

	[Fact]
	public void ToastRisesAboveTheChip()
	{
		var chip = new PixelRect(1708, 994, 200, 34);

		Assert.Equal(new PixelPoint(1500, 994 - 120 - 6), Placement.Above(new PixelPoint(1500, 914), new PixelSize(400, 120), chip, 6));
		Assert.Equal(new PixelPoint(1500, 914), Placement.Above(new PixelPoint(1500, 914), new PixelSize(400, 120), null, 6));
	}
}
