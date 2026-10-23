using System;
using System.Collections.Generic;
using System.Linq;
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

	/// <summary>Плашка: по центру верхнего края рабочей области — под панелью задач, если та наверху.</summary>
	public static PixelPoint Notch(PixelRect work, PixelSize window) =>
		new(work.X + ((work.Width - window.Width) / 2), work.Y);

	/// <summary>Значок над треем: угол рабочей области на стороне значка трея по обеим осям.</summary>
	public static PixelPoint Chip(PixelRect icon, PixelRect work, PixelSize window, int margin) =>
		new(icon.Center.X < work.Center.X ? work.X + margin : work.Right - window.Width - margin,
			icon.Center.Y < work.Center.Y ? work.Y + margin : work.Bottom - window.Height - margin);

	/// <summary>Виджет на рабочем столе: где его оставили, пока тот экран на месте; иначе — правый верхний угол главного.
	/// Экран ищется по центру окна: у прижатой к краю карточки угол окна с полем под тень — за экраном.</summary>
	/// <param name="saved">Запомненный верхний левый угол; null — ещё не двигали.</param>
	/// <param name="screens">Границы всех экранов.</param>
	public static PixelPoint Desk(PixelPoint? saved, IReadOnlyList<PixelRect> screens, PixelRect primaryWork, PixelSize window, int margin) =>
		saved is { } place && screens.Any(screen => screen.Contains(new PixelRect(place, window).Center))
			? place
			: new(primaryWork.Right - window.Width - margin, primaryWork.Y + margin);

	/// <summary>Тост, который лёг бы на значок над треем, встаёт над ним.</summary>
	public static PixelPoint Above(PixelPoint at, PixelSize window, PixelRect? obstacle, int margin) =>
		obstacle is { } over && new PixelRect(at, window).Intersects(over)
			? new(at.X, over.Y - window.Height - margin)
			: at;

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

	/// <summary>Плашка — по центру верхнего края рабочей области экрана со значком трея.</summary>
	public static void PlaceNotch(Window window, PixelRect icon)
	{
		if (Screen(window, icon) is { } screen)
		{
			window.Position = Notch(screen.WorkingArea, PixelSize.FromSize(window.ClientSize, screen.Scaling));
		}
	}

	/// <summary>Значок над треем — в углу рабочей области у значка трея.</summary>
	/// <param name="margin">Отступ от угла в точках интерфейса.</param>
	public static void PlaceChip(Window window, PixelRect icon, double margin)
	{
		if (Screen(window, icon) is { } screen)
		{
			window.Position = Chip(icon, screen.WorkingArea, PixelSize.FromSize(window.ClientSize, screen.Scaling), (int)Math.Round(margin * screen.Scaling));
		}
	}

	/// <summary>Рабочая область экрана со значком: переехала панель задач — плашка и значок встают заново.</summary>
	public static PixelRect? Work(Window window, PixelRect icon) => Screen(window, icon)?.WorkingArea;

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
