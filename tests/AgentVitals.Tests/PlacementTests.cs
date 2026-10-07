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
	public void SecondMonitorUsesItsOwnArea()
	{
		var point = Placement.Near(new PixelRect(3700, 1045, 24, 30), new PixelRect(1920, 0, 1920, 1040), _window);

		Assert.Equal(new PixelPoint(3440, 440), point);
	}
}
