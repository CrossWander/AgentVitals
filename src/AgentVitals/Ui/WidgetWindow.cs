using System;
using Avalonia;
using AgentVitals.Core.Sessions;
using AgentVitals.Platform;

namespace AgentVitals;

/// <summary>Окно плашки или значка над треем: на экране трея, поверх окон, без фокуса. В покое и под мышью щелчки
/// проходят насквозь к окну под ним; панель после задержки — нажимается. Прячется, пока идёт полноэкранное приложение.</summary>
internal abstract class WidgetWindow : TrayWindow, IWidgetWindow
{
	private PixelRect _anchor;
	private PixelRect? _work;
	private WidgetLook? _look;
	private bool _hidden;

	// Открыто хозяином: спрятанное на время полноэкранного приложения возвращается, закрытое — нет.
	private bool _open;

	protected WidgetWindow() => Opened += (_, _) => ApplyStyle();

	public event Action<string>? AgentChosen;

	public event Action<Session>? SessionChosen;

	/// <summary>Видимая часть: окно не бывает ниже 39 точек, а полоска — 4; мышь над прозрачным остатком — над вкладками
	/// браузера, а не над виджетом. Спрятанное окно мышь не раскрывает.</summary>
	public PixelRect Area => IsVisible && Hit.TranslatePoint(default, this) is { } at
		? new(Position + PixelPoint.FromPoint(at, RenderScaling), PixelSize.FromSize(Hit.Bounds.Size, RenderScaling))
		: default;

	/// <summary>Что сейчас видно: над этим мышь раскрывает виджет, это обходят тосты.</summary>
	protected abstract Visual Hit { get; }

	public void Open(PixelRect anchor)
	{
		_anchor = anchor;
		_open = true;
		ShowAt(anchor);

		if (OperatingSystem.IsWindows() && TryGetPlatformHandle() is { } handle)
		{
			WindowStyle.Topmost(handle.Handle);
		}
	}

	public void Render(WidgetView view) => DataContext = view;

	public void Show(WidgetLook look)
	{
		if (look == _look)
		{
			return;
		}

		_look = look;
		Look(look);

		if (OperatingSystem.IsWindows() && TryGetPlatformHandle() is { } handle)
		{
			WindowStyle.ClickThrough(handle.Handle, look != WidgetLook.Panel);
		}
	}

	public void Private(bool hide)
	{
		_hidden = hide;

		if (OperatingSystem.IsWindows() && TryGetPlatformHandle() is { } handle)
		{
			WindowStyle.Private(handle.Handle, hide);
		}
	}

	public void Tick()
	{
		if (!_open || !OperatingSystem.IsWindows() || TryGetPlatformHandle() is not { } handle)
		{
			return;
		}

		var full = WindowStyle.Fullscreen(handle.Handle);

		if (full && IsVisible)
		{
			Hide();
		}
		else if (!full && !IsVisible)
		{
			ShowAt(_anchor);
			WindowStyle.Topmost(handle.Handle);
		}

		// Панель задач переехала или сменился масштаб: о рабочей области Screens.Changed может промолчать.
		var work = Placement.Work(this, _anchor);

		if (work != _work)
		{
			_work = work;
			Place(_anchor);
		}
	}

	public new void Close()
	{
		_open = false;
		base.Close();
	}

	/// <summary>Показать вид: полоску, плашку или панель.</summary>
	protected abstract void Look(WidgetLook look);

	protected void Wire(WidgetPanel rows)
	{
		rows.AgentChosen += agent => AgentChosen?.Invoke(agent);
		rows.SessionChosen += session => SessionChosen?.Invoke(session);
	}

	private void ApplyStyle()
	{
		if (OperatingSystem.IsWindows() && TryGetPlatformHandle() is { } handle)
		{
			WindowStyle.Quiet(handle.Handle);
			WindowStyle.ClickThrough(handle.Handle, _look != WidgetLook.Panel);
			WindowStyle.Private(handle.Handle, _hidden);
		}
	}
}
