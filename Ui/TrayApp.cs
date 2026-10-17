using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
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

	// Суточная проверка обновлений: таймер раз в час спрашивает Updates.Due, а не ждёт ровно сутки — после сна тоже.
	private readonly DispatcherTimer _updates = new()
	{
		Interval = TimeSpan.FromHours(1),
	};

	private readonly DispatcherTimer _frames = new()
	{
		Interval = TimeSpan.FromMilliseconds(120),
	};

	private Tray? _tray;
	private IReadOnlyList<Session> _sessions = [];
	private bool _darkTaskbar = true;
	private int _tick;
	private readonly Dictionary<string, string> _indicators = [];

	public void Start()
	{
		_poller.Changed += OnPolled;
		_popover.RefreshRequested += Refresh;
		_popover.PrivateChanged += Render;
		_popover.SessionChosen += session =>
		{
			if (Raise(session))
			{
				_popover.Hide();
			}
		};
		_popover.Settings.Changed += Render;
		_popover.Settings.ExitRequested += Exit;
		_menu.RefreshRequested += Refresh;
		_menu.SettingsRequested += () => OpenPopover(settings: true);
		_menu.ExitRequested += Exit;
		_hover.Tick += (_, _) => OnHoverTick();
		_watch.Tick += (_, _) => OnWatchTick();
		_sessionTimer.Tick += (_, _) => ReadSessions();
		_updates.Tick += (_, _) => CheckUpdatesIfDue();
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

		// Опрос первым: он вспоминает последний ответ, и первая же отрисовка показывает его, а не пустое окно.
		_poller.Start();
		ReadSessions();
		Render();
		_sessionTimer.Start();

		AnnounceUpgrade();
		_updates.Start();

		if (Store.Current.AutoUpdate)
		{
			_ = CheckUpdatesAsync();
		}
	}

	private void CheckUpdatesIfDue()
	{
		if (Updates.Due(Store.Current.AutoUpdate, Store.Current.UpdateChecked, DateTime.UtcNow))
		{
			_ = CheckUpdatesAsync();
		}
	}

	/// <summary>Автопроверка: новая версия — в настройки и тостом, один раз на версию; без сети — молча.</summary>
	private async Task CheckUpdatesAsync()
	{
		var release = await Updates.CheckAsync();

		// Без exe в релизе объявлять нечего: кнопка ответила бы «файла нет». Часовой таймер спросит снова.
		if (release.State == UpdateState.Failed || release.Pending)
		{
			return;
		}

		_popover.Settings.Show(release);

		if (release is { State: UpdateState.Newer, Version: { } version } && Store.Current.UpdateAnnounced != version.ToString())
		{
			Store.Current.UpdateAnnounced = version.ToString();
			Store.Save();
			_toast.Say(Localization.Get("langToastUpdateText"), Anchor(), Localization.Format("langToastUpdate", version), life: Toast.Long,
				click: () => OpenPopover(settings: true));
			Journal.Add($"toast: update {version} available");
		}
	}

	/// <summary>Запустились новой версией — сказать об этом; щелчок открывает «что нового» этого релиза.</summary>
	private void AnnounceUpgrade()
	{
		var current = Updates.Current.ToString();

		if (Updates.Upgraded(Store.Current.LastVersion, Updates.Current))
		{
			_toast.Say(Localization.Get("langToastUpdatedText"), Anchor(), Localization.Format("langToastUpdated", current), life: Toast.Long,
				click: () => _ = _toast.Launcher.LaunchUriAsync(new Uri($"{Updates.Releases}/tag/v{current}")));
			Journal.Add($"updated to {current}");
		}

		if (Store.Current.LastVersion != current)
		{
			Store.Current.LastVersion = current;
			Store.Save();
		}
	}

	private void OnPolled()
	{
		Render();
		Notify();
	}

	private void Render()
	{
		var vitals = Gather();
		_popover.Render(vitals);
		_card.Render(vitals);
		ShowIcon();
	}

	/// <summary>Свежий снимок: «обновлено N мин назад» и «сброс через» сдвигаются со временем.</summary>
	private Vitals Gather() =>
		Views.Build(
			[.. _poller.Installed],
			HookTarget.All.ToDictionary(target => target.Agent, target => HookInstaller.Link(target, out _)),
			_sessions,
			DateTime.UtcNow,
			Store.Current.Thresholds,
			Store.Current.Private);

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

	/// <summary>Тост, когда сессия или неделя дошли до порога, обгоняют темп или сбросились, или сменился статус сервиса агента.</summary>
	/// <remarks>Опрос тикает раз в минуту даже без запроса к серверу: о сбросе тост — не позже чем через минуту.</remarks>
	private void Notify()
	{
		var now = DateTime.UtcNow;

		if (_alarm.Check(_poller.Installed.Select(state => (state.Provider.Name, state.Windows(now))), Store.Current.Thresholds, now) is { } alert)
		{
			var (title, message) = Text.QuotaToast(alert, now);
			var brush = alert.Level switch
			{
				QuotaLevel.Reset => "Good",
				QuotaLevel.Pace or QuotaLevel.Important => "Warn",
				QuotaLevel.Critical => "Bad",
			};

			_toast.Say(message, Anchor(), title, Views.Brush(brush), Toast.Long, () => OpenPopover(settings: false));

			// Тост живёт секунды: по журналу видно, был ли он, если человек отошёл от экрана.
			Journal.Add($"toast: {title}: {message}");
		}

		// У каждого агента своя страница: тост — о той, что сменила индикатор.
		foreach (var state in _poller.Installed)
		{
			if (state.Status is not { } status)
			{
				continue;
			}

			if (_indicators.TryGetValue(state.Provider.Name, out var previous) && previous != status.Indicator)
			{
				_toast.Say(Text.StatusToast(status), Anchor(), click: () => OpenPopover(settings: false));
			}

			_indicators[state.Provider.Name] = status.Indicator;
		}
	}

	/// <summary>Тост, когда агент закончил долгий ход или ждёт разрешения, — пока человек в другом окне.</summary>
	private void NotifySessions(IReadOnlyList<Session> before, IReadOnlyList<Session> after)
	{
		if (!Store.Current.SessionToasts)
		{
			return;
		}

		if (SessionAlarm.Check(before, after, TimeSpan.FromMinutes(Store.Current.TurnMinutes)) is { } alert)
		{
			var (title, message) = Text.SessionToast(alert);
			_toast.Say(message, Anchor(), title, Views.Brush(alert.Waiting ? "Blue" : "Good"), Toast.Long, () => GoTo(alert.Session));
			Journal.Add($"toast: {title}: {message}");

			if (SessionAlarm.Chimes(alert, Store.Current.WaitingSound))
			{
				Sound.Play();
			}
		}
	}

	/// <summary>К окну, где идёт сессия, — ответить сразу; не нашлось — окно AgentVitals со списком сессий.</summary>
	/// <summary>Щелчок по тосту сессии: её окно, а нет его — окно AgentVitals, чтобы щелчок не ушёл в пустоту.</summary>
	private void GoTo(Session session)
	{
		if (!Raise(session))
		{
			OpenPopover(settings: false);
		}
	}

	/// <summary>Поднять окно программы, где идёт сессия (VS Code, терминал…); false — такого окна нет.</summary>
	private static bool Raise(Session session)
	{
		if (session is { Pid: { } pid, Cwd: { } cwd } && HostWindow.Activate(pid, cwd))
		{
			return true;
		}

		Journal.Add($"session: no window for {session.Folder}");

		return false;
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
		TrayGlyph.Get(Poller.Health(_poller.Installed, DateTime.UtcNow, Store.Current.Thresholds), SessionWatcher.Overall(_sessions), _tick, _darkTaskbar, Tray.IconSize);

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
			_card.Render(Gather());
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
		_popover.Render(Gather());
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
