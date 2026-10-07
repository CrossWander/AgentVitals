using System;
using Avalonia;
using Avalonia.Controls;

namespace AgentVitals;

/// <summary>Где поставить окно у значка в трее — при любом положении панели задач.</summary>
internal static class Placement
{
	/// <summary>Верхний левый угол окна: со стороны рабочей области и в её пределах.</summary>
	public static PixelPoint Near(PixelRect icon, PixelRect work, PixelSize window)
	{
		var x = Math.Clamp(icon.Center.X - window.Width / 2, work.X, Math.Max(work.X, work.Right - window.Width));
		var y = Math.Clamp(icon.Center.Y - window.Height / 2, work.Y, Math.Max(work.Y, work.Bottom - window.Height));

		if (icon.Y >= work.Bottom)
		{
			y = work.Bottom - window.Height;
		}
		else if (icon.Bottom <= work.Y)
		{
			y = work.Y;
		}
		else if (icon.X >= work.Right)
		{
			x = work.Right - window.Width;
		}
		else if (icon.Right <= work.X)
		{
			x = work.X;
		}

		return new(x, y);
	}

	public static void Place(Window window, PixelRect icon)
	{
		if ((window.Screens.ScreenFromPoint(icon.Center) ?? window.Screens.Primary) is not { } screen)
		{
			return;
		}

		window.Position = Near(icon, screen.WorkingArea, PixelSize.FromSize(window.ClientSize, screen.Scaling));
	}
}
