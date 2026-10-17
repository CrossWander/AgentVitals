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

	/// <summary>Верхний левый угол окна в правом нижнем углу рабочей области — там, где Windows показывает уведомления.</summary>
	public static PixelPoint Corner(PixelRect work, PixelSize window, int margin) =>
		new(work.Right - window.Width - margin, work.Bottom - window.Height - margin);

	/// <summary>Окно не выше рабочей области экрана со значком: что не влезло, прокручивается внутри окна.</summary>
	public static void Fit(Window window, PixelRect icon)
	{
		if (Screen(window, icon) is { } screen)
		{
			window.MaxHeight = screen.WorkingArea.Height / screen.Scaling;
		}
	}

	public static void Place(Window window, PixelRect icon)
	{
		if (Screen(window, icon) is { } screen)
		{
			window.Position = Near(icon, screen.WorkingArea, PixelSize.FromSize(window.ClientSize, screen.Scaling));
		}
	}

	/// <summary>В угол того экрана, где значок.</summary>
	/// <param name="margin">Отступ от угла в точках интерфейса.</param>
	public static void PlaceInCorner(Window window, PixelRect icon, double margin)
	{
		if (Screen(window, icon) is { } screen)
		{
			window.Position = Corner(screen.WorkingArea, PixelSize.FromSize(window.ClientSize, screen.Scaling), (int)Math.Round(margin * screen.Scaling));
		}
	}

	private static Avalonia.Platform.Screen? Screen(Window window, PixelRect icon) =>
		window.Screens.ScreenFromPoint(icon.Center) ?? window.Screens.Primary;
}

/// <summary>Окно у значка в трее: поповер, карточка, меню, тост. Держится у значка при любой смене размера.</summary>
internal class TrayWindow : Window
{
	private PixelRect _anchor;

	public TrayWindow()
	{
		// Высота зависит от содержимого: после смены экрана или вкладки окно ставится заново.
		// Resized, а не SizeChanged: при показе Windows возвращает окну прежний размер, Avalonia
		// растягивает его снова, а Bounds при этом не меняются и SizeChanged молчит.
		Resized += (_, _) => Place(_anchor);
	}

	/// <summary>Показать у значка.</summary>
	/// <param name="anchor">Где значок, в точках экрана.</param>
	protected void ShowAt(PixelRect anchor)
	{
		_anchor = anchor;
		Placement.Fit(this, anchor);
		Show();
		Place(anchor);
	}

	/// <summary>Куда встать относительно значка: по умолчанию — рядом с ним.</summary>
	protected virtual void Place(PixelRect anchor) => Placement.Place(this, anchor);
}
