using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;

namespace AgentVitals;

/// <summary>Всё вокруг значка в трее: опрос, сессии, сам значок и окна, которые от него открываются.</summary>
internal sealed class TrayApp
{
	// Мышь идёт через трей к часам и к чужим значкам — такой проход короче трети секунды.
	private static readonly TimeSpan _hoverDelay = TimeSpan.FromMilliseconds(350);

	private readonly Poller _poller = new();
	private readonly Popover _popover = new();
	private readonly HoverCard _card = new();
	private readonly TrayMenu _menu = new();
	private readonly Toast _toast = new();
	private readonly QuotaAlarm _alarm = new();

	private readonly DispatcherTimer _hover = new()
	{
		Interval = _hoverDelay,
	};

	// Значок не сообщает, что мышь ушла, — пока карточка открыта, следим за курсором сами.
	private readonly DispatcherTimer _watch = new()
	{
		Interval = TimeSpan.FromMilliseconds(150),
	};

	private readonly DispatcherTimer _sessionTimer = new()
	{
		Interval = TimeSpan.FromSeconds(1),
	};

	private readonly DispatcherTimer _frames = new()
	{
		Interval = TimeSpan.FromMilliseconds(120),
	};

	private Tray? _tray;
	private IReadOnlyList<LiveSession> _sessions = [];
	private bool _darkTaskbar = true;
	private int _tick;
	private string? _lastIndicator;

	public void Start()
	{
		_poller.Changed += OnPolled;
		_popover.RefreshRequested += Refresh;
		_popover.Changed += Render;
		_popover.ExitRequested += Exit;
		_menu.RefreshRequested += Refresh;
		_menu.SettingsRequested += () => OpenPopover(settings: true);
		_menu.ExitRequested += Exit;
		_toast.Clicked += () => OpenPopover(settings: false);
		_hover.Tick += (_, _) => OnHoverTick();
		_watch.Tick += (_, _) => OnWatchTick();
		_sessionTimer.Tick += (_, _) => ReadSessions();
		_frames.Tick += (_, _) => NextFrame();
		Application.Current!.ActualThemeVariantChanged += (_, _) => Render();

		if (OperatingSystem.IsWindows())
		{
			_darkTaskbar = Tray.DarkTaskbar;
			_tray = new Tray(Glyph());
			_tray.Moved += OnTrayMove;
			_tray.Clicked += TogglePopover;
			_tray.RightClicked += ShowMenu;
			_tray.KeySelected += () => OpenPopover(settings: false);
			_tray.Woke += () =>
			{
				Journal.Add("woke");
				_ = _poller.WokeAsync();
			};
			Journal.Add($"start, tray icon at {_tray.Bounds()}, size {Tray.IconSize}");
		}
		else
		{
			// На других системах — стандартный значок Avalonia: наведения и своего меню там нет.
			var icon = new TrayIcon
			{
				Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://AgentVitals/app.png"))),
				ToolTipText = "AgentVitals",
			};

			icon.Clicked += (_, _) => TogglePopover();
			TrayIcon.SetIcons(Application.Current, new TrayIcons { icon });
		}

		ReadSessions();
		Render();
		_sessionTimer.Start();
		_poller.Start();
	}

	private void OnPolled()
	{
		Render();
		Notify();
	}

	private void Render()
	{
		var vitals = Views.Build(_poller, _sessions, DateTime.UtcNow);
		_popover.Render(vitals);
		_card.Render(vitals);
		ShowIcon();
	}

	private void ReadSessions()
	{
		if (OperatingSystem.IsWindows())
		{
			// Тему панели задач меняют на ходу; реестр раз в секунду — дёшево, кадры значка в кэше.
			_darkTaskbar = Tray.DarkTaskbar;
		}

		var sessions = SessionWatcher.Read(Paths.Sessions, DateTime.UtcNow);

		if (!sessions.SequenceEqual(_sessions))
		{
			NotifySessions(_sessions, sessions);
			_sessions = sessions;

			if (SessionWatcher.Overall(sessions) == SessionState.Idle)
			{
				_frames.Stop();
			}
			else
			{
				_frames.Start();
			}

			Render();
		}

		ShowIcon();
	}

	private void NextFrame()
	{
		_tick++;
		ShowIcon();
	}

	/// <summary>Тост, когда сессия или неделя дошли до 75% или 90% или сменился статус Anthropic.</summary>
	private void Notify()
	{
		var alerts = _poller.Available
			.Where(state => state.Snapshot is not null)
			.SelectMany(state => _alarm.Check(state.Provider.Name, state.Windows(DateTime.UtcNow), Store.Current.Thresholds).Select(alert => (state.Provider.Name, alert)))
			.ToList();

		// Оба окна выше порога бывают разве что при запуске — тост одного, самого серьёзного.
		if (alerts.Count > 0)
		{
			var (provider, alert) = alerts.MaxBy(item => (item.alert.Critical, item.alert.Window.Used));
			var (title, message) = Text.Alert(provider, alert, DateTime.UtcNow);
			_toast.Say(message, Anchor(), title, Views.Brush(alert.Critical ? "Bad" : "Warn"), Toast.Long);

			// Тост живёт секунды: по журналу видно, был ли он, если человек отошёл от экрана.
			Journal.Add($"toast: {title}: {message}");
		}

		if (_poller.Status is { } status)
		{
			if (_lastIndicator is { } previous && previous != status.Indicator)
			{
				_toast.Say(Text.StatusToast(status), Anchor());
			}

			_lastIndicator = status.Indicator;
		}
	}

	/// <summary>Тост, когда Claude закончил долгий ход или ждёт разрешения, — пока человек в другом окне.</summary>
	private void NotifySessions(IReadOnlyList<LiveSession> before, IReadOnlyList<LiveSession> after)
	{
		if (!Store.Current.SessionToasts)
		{
			return;
		}

		// Несколько разом бывает редко — сказать о главном: вопрос о разрешении держит Claude, пока не ответят.
		if (SessionAlarm.Check(before, after, TimeSpan.FromMinutes(Store.Current.TurnMinutes)).MaxBy(alert => alert.Waiting) is { } alert)
		{
			var (title, message) = Text.SessionToast(alert);
			_toast.Say(message, Anchor(), title, Views.Brush(alert.Waiting ? "Blue" : "Good"));
			Journal.Add($"toast: {title}: {message}");
		}
	}

	private void ShowIcon()
	{
		if (OperatingSystem.IsWindows())
		{
			_tray?.SetIcon(Glyph());
		}
	}

	[SupportedOSPlatform("windows")]
	private IntPtr Glyph() =>
		TrayGlyph.Get(_poller.Health(DateTime.UtcNow, Store.Current.Thresholds),SessionWatcher.Overall(_sessions), _tick, _darkTaskbar, Tray.IconSize);

	private void OnTrayMove()
	{
		if (_popover.IsVisible || _menu.IsVisible || _card.IsVisible)
		{
			return;
		}

		_hover.Stop();
		_hover.Start();
	}

	private void OnHoverTick()
	{
		_hover.Stop();

		if (OverIcon() && !_popover.IsVisible && !_menu.IsVisible)
		{
			// Свежий снимок: «обновлено N мин назад» и «сброс через» сдвигаются со временем.
			_card.Render(Views.Build(_poller, _sessions, DateTime.UtcNow));
			_card.Open(Anchor());
			_watch.Start();
		}
	}

	private void OnWatchTick()
	{
		if (!_card.IsVisible || (!OverIcon() && !_card.IsPointerOver))
		{
			_watch.Stop();
			_card.Hide();
		}
	}

	private bool OverIcon() =>
		OperatingSystem.IsWindows()
		&& _tray?.Bounds() is { } icon
		&& new PixelRect(icon.X - 4, icon.Y - 4, icon.Width + 8, icon.Height + 8).Contains(Tray.Cursor());

	private PixelRect Anchor()
	{
		if (OperatingSystem.IsWindows() && _tray?.Bounds() is { } icon)
		{
			return icon;
		}

		// Без своего значка — правый нижний угол основного экрана, где трей по умолчанию.
		return _popover.Screens.Primary?.WorkingArea is { } work ? new PixelRect(work.Right - 1, work.Bottom, 1, 1) : default;
	}

	private void TogglePopover()
	{
		HideGlance();

		if (_popover.IsVisible)
		{
			_popover.Hide();

			return;
		}

		OpenPopover(settings: false);
	}

	private void OpenPopover(bool settings)
	{
		HideGlance();
		_menu.Hide();
		_popover.Render(Views.Build(_poller, _sessions, DateTime.UtcNow));
		_popover.Open(Anchor(), settings);

		if (OperatingSystem.IsWindows())
		{
			Tray.Focus(_popover);
		}
	}

	private void ShowMenu()
	{
		HideGlance();
		_popover.Hide();
		_menu.Open(Anchor());

		if (OperatingSystem.IsWindows())
		{
			Tray.Focus(_menu);
		}
	}

	private void HideGlance()
	{
		_hover.Stop();
		_watch.Stop();
		_card.Hide();
	}

	private void Refresh() => _ = _poller.RefreshAsync();

	private void Exit()
	{
		// Без удаления значок висел бы в трее до первого наведения мыши.
		if (OperatingSystem.IsWindows())
		{
			_tray?.Dispose();
		}

		(Application.Current!.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
	}
}
