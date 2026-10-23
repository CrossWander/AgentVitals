using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using AgentVitals.Core.Sessions;
using AgentVitals.Platform;

namespace AgentVitals;

/// <summary>Виджет на рабочем столе: под всеми окнами, виден и после Win+D, перетаскивается мышью и помнит место.</summary>
internal partial class DeskWindow : Window, IWidgetWindow
{
	// Отступ от угла главного экрана, пока виджет не двигали, — в точках интерфейса.
	private const double _margin = 24;

	private bool _hidden;

	// Впереди рабочий стол (Win+D): виджет на это время поверх, иначе его закрыл бы сам рабочий стол.
	private bool _front;

	public DeskWindow()
	{
		InitializeComponent();
		Opened += (_, _) => ApplyStyle();

		// Агентов стало больше — виджет шире: место пересчитывается, а запомненное остаётся.
		Resized += (_, _) => Place();
	}

	public event Action<string>? AgentChosen;

	public event Action<Session>? SessionChosen;

	// Раскрывать нечего: щелчок по агенту или сессии — сразу их окно.
	public event Action? Clicked
	{
		add { }
		remove { }
	}

	public PixelRect Area => IsVisible ? new(Position, PixelSize.FromSize(ClientSize, RenderScaling)) : default;

	public void Open(PixelRect anchor)
	{
		if (!IsVisible)
		{
			Show();
		}

		Place();
		Bottom();
	}

	public void Render(WidgetView view) => DataContext = view;

	// Один вид: ни полоски, ни панели; нажимается всегда — его таскают мышью.
	public void Show(WidgetLook look, bool clickable)
	{
	}

	public void Private(bool hide)
	{
		_hidden = hide;

		if (OperatingSystem.IsWindows() && TryGetPlatformHandle() is { } handle)
		{
			WindowStyle.Private(handle.Handle, hide);
		}
	}

	/// <remarks>ponytail: впереди рабочий стол и после щелчка по обоям — тогда виджет на это время тоже поверх окон;
	/// отличить Win+D от щелчка по обоям — если это будет мешать.</remarks>
	public void Tick()
	{
		if (!OperatingSystem.IsWindows() || TryGetPlatformHandle() is not { } handle)
		{
			return;
		}

		var front = WindowStyle.DesktopInFront();

		if (front == _front)
		{
			return;
		}

		_front = front;

		if (front)
		{
			WindowStyle.Topmost(handle.Handle);
		}
		else
		{
			WindowStyle.Bottom(handle.Handle);
		}
	}

	private void Place()
	{
		PixelPoint? saved = Store.Current is { WidgetX: { } x, WidgetY: { } y } ? new PixelPoint(x, y) : null;

		Position = Placement.Desk(
			saved,
			[.. Screens.All.Select(screen => screen.Bounds)],
			Screens.Primary?.WorkingArea ?? default,
			PixelSize.FromSize(ClientSize, RenderScaling),
			(int)Math.Round(_margin * RenderScaling));
	}

	/// <summary>Нажали — тянем окно. Отпустили на том же месте — это щелчок по строке под мышью.</summary>
	/// <remarks>BeginMoveDrag на Windows возвращается, когда кнопку отпустили: системный цикл перетаскивания.</remarks>
	private void OnPress(object? sender, PointerPressedEventArgs e)
	{
		if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
		{
			return;
		}

		var before = Position;
		BeginMoveDrag(e);

		if (Position == before)
		{
			Choose(e.Source as StyledElement);

			return;
		}

		Store.Current.WidgetX = Position.X;
		Store.Current.WidgetY = Position.Y;
		Store.Save();
		Bottom();
	}

	private void Choose(StyledElement? element)
	{
		for (; element is not null; element = element.Parent)
		{
			switch (element.DataContext)
			{
				case WidgetAgent agent:
					AgentChosen?.Invoke(agent.Name);

					return;

				case WidgetSession session:
					SessionChosen?.Invoke(session.Session);

					return;
			}
		}
	}

	private void Bottom()
	{
		if (OperatingSystem.IsWindows() && TryGetPlatformHandle() is { } handle && !_front)
		{
			WindowStyle.Bottom(handle.Handle);
		}
	}

	private void ApplyStyle()
	{
		if (OperatingSystem.IsWindows() && TryGetPlatformHandle() is { } handle)
		{
			WindowStyle.Quiet(handle.Handle);
			WindowStyle.Private(handle.Handle, _hidden);
			WindowStyle.Bottom(handle.Handle);
		}
	}
}
