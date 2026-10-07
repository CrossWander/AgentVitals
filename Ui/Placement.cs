using System;
using Avalonia;
using Avalonia.Controls;

namespace AgentVitals;

/// <summary>Где поставить окно у значка в трее — при любом положении панели задач.</summary>
internal static class Placement
{
	/// <summary>Верхний левый угол окна: рядом со значком, со стороны рабочей области и в её пределах.</summary>
	public static PixelPoint Near(PixelRect icon, PixelRect work, PixelSize window)
	{
		var x = icon.Center.X - window.Width / 2;
		var y = icon.Center.Y - window.Height / 2;

		// Значок вне рабочей области — панель задач с той стороны. Внутри неё (автоскрываемая панель,
		// всплывающая область скрытых значков) — со стороны ближайшего края, чтобы не закрыть значок.
		var bottom = icon.Y >= work.Bottom ? 0 : work.Bottom - icon.Center.Y;
		var top = icon.Bottom <= work.Y ? 0 : icon.Center.Y - work.Y;
		var right = icon.X >= work.Right ? 0 : work.Right - icon.Center.X;
		var left = icon.Right <= work.X ? 0 : icon.Center.X - work.X;
		var nearest = Math.Min(Math.Min(bottom, top), Math.Min(right, left));

		if (nearest == bottom)
		{
			y = Math.Min(icon.Y, work.Bottom) - window.Height;
		}
		else if (nearest == top)
		{
			y = Math.Max(icon.Bottom, work.Y);
		}
		else if (nearest == right)
		{
			x = Math.Min(icon.X, work.Right) - window.Width;
		}
		else
		{
			x = Math.Max(icon.Right, work.X);
		}

		// В пределах рабочей области; не влезает — видна шапка, а не низ.
		return new(
			Math.Max(Math.Min(x, work.Right - window.Width), work.X),
			Math.Max(Math.Min(y, work.Bottom - window.Height), work.Y));
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
