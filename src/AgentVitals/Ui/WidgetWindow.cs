using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using AgentVitals.Core.Sessions;
using AgentVitals.Platform;

namespace AgentVitals;

/// <summary>Окно плашки или значка над треем: на экране трея, поверх окон, без фокуса. Щелчки проходят насквозь к окну
/// под ним, пока мышь не задержится над виджетом. Прячется, пока идёт полноэкранное приложение.</summary>
internal abstract class WidgetWindow : TrayWindow, IWidgetWindow
{
	private static readonly TimeSpan _fade = TimeSpan.FromMilliseconds(180);

	private PixelRect _anchor;
	private PixelRect? _work;
	private WidgetLook? _look;
	private bool _clickable;
	private bool _hidden;

	// Открыто хозяином: спрятанное на время полноэкранного приложения возвращается, закрытое — нет.
	private bool _open;

	// Чей снимок в шапке — по заголовку; пока прежний гаснет, свежий ждёт в _next.
	private string? _face;
	private WidgetView? _next;
	private bool _fading;

	protected WidgetWindow() => Opened += (_, _) => ApplyStyle();

	public event Action<string>? AgentChosen;

	public event Action<Session>? SessionChosen;

	public event Action? Clicked;

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

	public void Render(WidgetView view)
	{
		DataContext = view;
		Rendered();
	}

	public void Show(WidgetLook look, bool clickable)
	{
		if (look != _look)
		{
			_look = look;
			Look(look);
		}

		if (clickable != _clickable)
		{
			_clickable = clickable;

			if (OperatingSystem.IsWindows() && TryGetPlatformHandle() is { } handle)
			{
				WindowStyle.ClickThrough(handle.Handle, !clickable);
			}
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

	public virtual void Tick()
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

	/// <summary>Пришёл снимок — шапка выбирает, кого показать. Не из DataContextChanged: внутри него снимок, который шапка
	/// и так наследует («ждёт» показывается как есть), привязки её строк не будит — шапка оставалась пустой.</summary>
	protected abstract void Rendered();

	/// <summary>Щелчок левой кнопкой по этому открывает и закрывает панель.</summary>
	protected void Opens(Control face) => face.PointerReleased += (_, e) =>
	{
		if (e.InitialPressMouseButton == MouseButton.Left)
		{
			Clicked?.Invoke();
		}
	};

	/// <summary>Снимок в шапке: тот же агент — свежие цифры сразу; другой — прежний гаснет, новый проявляется. Переход
	/// Opacity — у части в разметке.</summary>
	protected void ShowFace(Control face, WidgetView view)
	{
		var other = _face is not null && view.Title != _face;
		_face = view.Title;
		_next = view;

		if (_fading)
		{
			return;
		}

		if (!other)
		{
			face.DataContext = view;

			return;
		}

		_fading = true;
		face.Opacity = 0;

		DispatcherTimer.RunOnce(() =>
		{
			_fading = false;
			face.DataContext = _next;
			face.Opacity = 1;
		}, _fade);
	}

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
			WindowStyle.ClickThrough(handle.Handle, !_clickable);
			WindowStyle.Private(handle.Handle, _hidden);
		}
	}
}
