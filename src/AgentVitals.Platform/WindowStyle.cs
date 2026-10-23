using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AgentVitals.Platform;

/// <summary>
/// Win32 для окон виджета: без фокуса и Alt+Tab, щелчки насквозь, не на снимках экрана, под всеми окнами. Avalonia
/// этого не умеет — окну передаётся его HWND.
/// </summary>
[SupportedOSPlatform("windows")]
public static unsafe partial class WindowStyle
{
	private const int _exStyle = -20;                          // GWL_EXSTYLE
	private const nint _transparent = 0x20;                    // WS_EX_TRANSPARENT: мышь проходит насквозь…
	private const nint _layered = 0x80000;                     // WS_EX_LAYERED: …но только у layered-окна
	private const nint _toolWindow = 0x80;                     // WS_EX_TOOLWINDOW: нет в Alt+Tab и на панели задач
	private const nint _noActivate = 0x08000000;               // WS_EX_NOACTIVATE: щелчок не забирает фокус
	private const uint _alpha = 0x2;                           // LWA_ALPHA
	private const uint _excludeFromCapture = 0x11;             // WDA_EXCLUDEFROMCAPTURE, Windows 10 2004+
	private const uint _keepPlaceAndFocus = 0x1 | 0x2 | 0x10;  // SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE
	private const uint _nearest = 2;                           // MONITOR_DEFAULTTONEAREST

	private static readonly nint _topmost = -1;
	private static readonly nint _notTopmost = -2;
	private static readonly nint _bottom = 1;

	/// <summary>Окно не берёт фокус и не видно в Alt+Tab и на панели задач.</summary>
	public static void Quiet(nint window) => Set(window, Get(window) | _toolWindow | _noActivate);

	/// <summary>Щелчки проходят к окну под виджетом (true) или достаются ему (false).</summary>
	/// <remarks>Layered включается один раз и остаётся: снимать его с окна, которое рисует Avalonia, незачем.</remarks>
	public static void ClickThrough(nint window, bool on)
	{
		var style = Get(window);

		if (on && (style & _layered) == 0)
		{
			Set(window, style | _layered);
			SetLayeredWindowAttributes(window, 0, 255, _alpha);
			style = Get(window);
		}

		Set(window, on ? style | _transparent : style & ~_transparent);
	}

	/// <summary>Глаз: окно не попадает на снимки и в демонстрацию экрана. На Windows старше 10 2004 вызов не проходит —
	/// тогда остаётся то, что глаз прячет в самом снимке.</summary>
	public static bool Private(nint window, bool hide) => SetWindowDisplayAffinity(window, hide ? _excludeFromCapture : 0);

	/// <summary>Поверх всех окон, без фокуса: виджет и тост.</summary>
	public static void Topmost(nint window) => SetWindowPos(window, _topmost, 0, 0, 0, 0, _keepPlaceAndFocus);

	/// <summary>Под всеми окнами: виджет на рабочем столе.</summary>
	public static void Bottom(nint window)
	{
		SetWindowPos(window, _notTopmost, 0, 0, 0, 0, _keepPlaceAndFocus);
		SetWindowPos(window, _bottom, 0, 0, 0, 0, _keepPlaceAndFocus);
	}

	/// <summary>На мониторе окна идёт полноэкранное приложение: игра, видео, презентация, браузер по F11.</summary>
	/// <remarks>Оболочка решает, что приложение полноэкранное, сама — по тому же признаку она молчит уведомлениями.
	/// Монитор — переднего окна: видео на втором мониторе плашку на первом не прячет.</remarks>
	public static bool Fullscreen(nint window)
	{
		// QUNS_BUSY, QUNS_RUNNING_D3D_FULL_SCREEN, QUNS_PRESENTATION_MODE
		if (SHQueryUserNotificationState(out var state) != 0 || state is not (2 or 3 or 4))
		{
			return false;
		}

		var front = GetForegroundWindow();

		return front != 0 && MonitorFromWindow(front, _nearest) == MonitorFromWindow(window, _nearest);
	}

	/// <summary>Впереди рабочий стол: Win+D или щелчок по нему.</summary>
	public static bool DesktopInFront()
	{
		var front = GetForegroundWindow();

		if (front == 0)
		{
			return false;
		}

		var name = stackalloc char[32];
		var length = GetClassNameW(front, name, 32);

		return new string(name, 0, length) is "WorkerW" or "Progman";
	}

	private static nint Get(nint window) => GetWindowLongPtrW(window, _exStyle);

	private static void Set(nint window, nint style) => SetWindowLongPtrW(window, _exStyle, style);

	[LibraryImport("user32.dll")]
	private static partial nint GetWindowLongPtrW(nint window, int index);

	[LibraryImport("user32.dll")]
	private static partial nint SetWindowLongPtrW(nint window, int index, nint value);

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool SetLayeredWindowAttributes(nint window, uint key, byte alpha, uint flags);

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool SetWindowDisplayAffinity(nint window, uint affinity);

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);

	[LibraryImport("shell32.dll")]
	private static partial int SHQueryUserNotificationState(out int state);

	[LibraryImport("user32.dll")]
	private static partial nint GetForegroundWindow();

	[LibraryImport("user32.dll")]
	private static partial nint MonitorFromWindow(nint window, uint flags);

	[LibraryImport("user32.dll")]
	private static partial int GetClassNameW(nint window, char* name, int max);
}
