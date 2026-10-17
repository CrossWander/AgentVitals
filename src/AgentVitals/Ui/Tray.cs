using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Microsoft.Win32;
using AgentVitals.Platform;

namespace AgentVitals;

/// <summary>
/// Значок в трее Windows через Shell_NotifyIcon. Свой, а не TrayIcon из Avalonia: тот не отдаёт ни
/// наведения, ни правого щелчка без системного меню, а системного в программе быть не должно.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe partial class Tray : IDisposable
{
	private const uint _callback = 0x8001;        // WM_APP + 1: так значок сообщает о мыши
	private const uint _id = 1;
	private const uint _add = 0x0;                // NIM_ADD
	private const uint _modify = 0x1;             // NIM_MODIFY
	private const uint _delete = 0x2;             // NIM_DELETE
	private const uint _setVersion = 0x4;         // NIM_SETVERSION
	private const uint _flags = 0x1 | 0x2;        // NIF_MESSAGE | NIF_ICON; без NIF_TIP — системной подсказки нет
	private const int _mouseMove = 0x0200;
	private const int _leftUp = 0x0202;
	private const int _contextMenu = 0x007B;      // правый щелчок или Shift+F10 в версии 4
	private const int _keySelect = 0x0401;        // NIN_KEYSELECT: Win+B, стрелки, Enter или пробел
	private const uint _power = 0x0218;           // WM_POWERBROADCAST
	private const int _resumed = 0x0012;          // PBT_APMRESUMEAUTOMATIC: приходит при любом пробуждении
	private const uint _popupStyle = 0x80000000;  // WS_POPUP; окно никогда не показывается
	private const int _smallIcon = 49;            // SM_CXSMICON

	// WndProc статический — это колбэк из неуправляемого кода; значок у программы один.
	private static Tray? _instance;

	private readonly IntPtr _window;
	private readonly uint _taskbarCreated;
	private IntPtr _icon;

	public Tray(IntPtr icon)
	{
		_instance = this;
		_icon = icon;

		// Строка класса нужна, пока живёт окно, то есть до конца процесса: не освобождаем.
		var className = Marshal.StringToHGlobalUni("AgentVitalsTray");
		var windowClass = new WNDCLASSEXW
		{
			cbSize = (uint)sizeof(WNDCLASSEXW),
			lpfnWndProc = (IntPtr)(delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, IntPtr>)&WndProc,
			hInstance = GetModuleHandleW(IntPtr.Zero),
			lpszClassName = className,
		};

		RegisterClassExW(ref windowClass);

		// Обычное скрытое окно, а не message-only: широковещательное TaskbarCreated приходит только таким.
		_window = CreateWindowExW(0, className, IntPtr.Zero, _popupStyle, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, windowClass.hInstance, IntPtr.Zero);
		_taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
		Add();
	}

	public event Action? Moved;

	public event Action? Clicked;

	public event Action? RightClicked;

	/// <summary>Значок выбрали с клавиатуры. Enter оболочка шлёт дважды — поэтому открыть, а не переключить.</summary>
	public event Action? KeySelected;

	/// <summary>Компьютер проснулся после сна или гибернации.</summary>
	public event Action? Woke;

	/// <summary>Сторона значка трея для текущего масштаба экрана, в точках.</summary>
	public static int IconSize => GetSystemMetricsForDpi(_smallIcon, TaskbarDpi());

	/// <summary>Панель задач тёмная: от этого зависит, какие оттенки на ней читаются.</summary>
	public static bool DarkTaskbar =>
		Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0) is not 1;

	// Масштаб монитора с панелью задач, а не системный: тот запоминается при входе в Windows,
	// и после смены масштаба или переноса панели на другой монитор значок рисовался бы не в размер.
	private static uint TaskbarDpi()
	{
		const uint nearest = 2;      // MONITOR_DEFAULTTONEAREST
		const int effective = 0;     // MDT_EFFECTIVE_DPI

		var taskbar = FindWindowW("Shell_TrayWnd", null);

		return taskbar != IntPtr.Zero && GetDpiForMonitor(MonitorFromWindow(taskbar, nearest), effective, out var dpi, out _) == 0
			? dpi
			: GetDpiForSystem();
	}

	public static PixelPoint Cursor() => GetCursorPos(out var point) ? new(point.X, point.Y) : default;

	/// <summary>
	/// Отдать окну фокус. Щелчок по значку даёт программе право выйти на передний план, но без явного
	/// SetForegroundWindow окно может остаться неактивным — и не закроется щелчком мимо.
	/// </summary>
	public static void Focus(Window window)
	{
		if (window.TryGetPlatformHandle() is { } handle)
		{
			SetForegroundWindow(handle.Handle);
		}
	}

	public void SetIcon(IntPtr icon)
	{
		if (icon == _icon)
		{
			return;
		}

		_icon = icon;
		var data = Data();
		Shell_NotifyIconW(_modify, ref data);
	}

	/// <summary>Где значок на экране, в точках; null — оболочка не знает (панель задач перезапускается).</summary>
	public PixelRect? Bounds()
	{
		var id = new NOTIFYICONIDENTIFIER
		{
			cbSize = (uint)sizeof(NOTIFYICONIDENTIFIER),
			hWnd = _window,
			uID = _id,
		};

		return Shell_NotifyIconGetRect(ref id, out var rect) == 0
			? new PixelRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top)
			: null;
	}

	public void Dispose()
	{
		var data = Data();
		Shell_NotifyIconW(_delete, ref data);
		DestroyWindow(_window);
		_instance = null;
	}

	private void Add()
	{
		var data = Data();
		Shell_NotifyIconW(_add, ref data);

		// Версия 4: координаты в событиях и WM_CONTEXTMENU на правый щелчок.
		data.uVersion = 4;
		Shell_NotifyIconW(_setVersion, ref data);
	}

	private NOTIFYICONDATAW Data() => new()
	{
		cbSize = (uint)sizeof(NOTIFYICONDATAW),
		hWnd = _window,
		uID = _id,
		uFlags = _flags,
		uCallbackMessage = _callback,
		hIcon = _icon,
	};

	[UnmanagedCallersOnly]
	private static IntPtr WndProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
	{
		// Исключение, вылетевшее в неуправляемый колбэк, роняет процесс целиком.
		try
		{
			if (_instance is { } tray)
			{
				if (message == _callback)
				{
					tray.OnTray((int)(lParam.ToInt64() & 0xFFFF));

					return IntPtr.Zero;
				}

				// Проводник перезапустился — новая панель задач о значке не знает.
				if (message == tray._taskbarCreated)
				{
					tray.Add();
				}

				// Окно скрытое, но верхнего уровня — такие получают и сообщения о питании.
				if (message == _power && wParam == _resumed)
				{
					tray.Woke?.Invoke();
				}
			}
		}
		catch (Exception exception)
		{
			Journal.Add($"tray: {exception}");
		}

		return DefWindowProcW(window, message, wParam, lParam);
	}

	private void OnTray(int message)
	{
		switch (message)
		{
			case _mouseMove:
				Moved?.Invoke();
				break;

			case _leftUp:
				Clicked?.Invoke();
				break;

			case _contextMenu:
				RightClicked?.Invoke();
				break;

			case _keySelect:
				KeySelected?.Invoke();
				break;
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct WNDCLASSEXW
	{
		public uint cbSize;
		public uint style;
		public IntPtr lpfnWndProc;
		public int cbClsExtra;
		public int cbWndExtra;
		public IntPtr hInstance;
		public IntPtr hIcon;
		public IntPtr hCursor;
		public IntPtr hbrBackground;
		public IntPtr lpszMenuName;
		public IntPtr lpszClassName;
		public IntPtr hIconSm;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct NOTIFYICONDATAW
	{
		public uint cbSize;
		public IntPtr hWnd;
		public uint uID;
		public uint uFlags;
		public uint uCallbackMessage;
		public IntPtr hIcon;
		public fixed char szTip[128];
		public uint dwState;
		public uint dwStateMask;
		public fixed char szInfo[256];
		public uint uVersion;
		public fixed char szInfoTitle[64];
		public uint dwInfoFlags;
		public Guid guidItem;
		public IntPtr hBalloonIcon;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct NOTIFYICONIDENTIFIER
	{
		public uint cbSize;
		public IntPtr hWnd;
		public uint uID;
		public Guid guidItem;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct RECT
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct POINT
	{
		public int X;
		public int Y;
	}

	[LibraryImport("user32.dll")]
	private static partial ushort RegisterClassExW(ref WNDCLASSEXW windowClass);

	[LibraryImport("user32.dll")]
	private static partial IntPtr CreateWindowExW(uint extendedStyle, IntPtr className, IntPtr name, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool DestroyWindow(IntPtr window);

	[LibraryImport("user32.dll")]
	private static partial IntPtr DefWindowProcW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

	[LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
	private static partial uint RegisterWindowMessageW(string name);

	[LibraryImport("kernel32.dll")]
	private static partial IntPtr GetModuleHandleW(IntPtr name);

	[LibraryImport("user32.dll")]
	private static partial uint GetDpiForSystem();

	[LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
	private static partial IntPtr FindWindowW(string className, string? windowName);

	[LibraryImport("user32.dll")]
	private static partial IntPtr MonitorFromWindow(IntPtr window, uint flags);

	[LibraryImport("shcore.dll")]
	private static partial int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

	[LibraryImport("user32.dll")]
	private static partial int GetSystemMetricsForDpi(int index, uint dpi);

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool GetCursorPos(out POINT point);

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool SetForegroundWindow(IntPtr window);

	[LibraryImport("shell32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATAW data);

	[LibraryImport("shell32.dll")]
	private static partial int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER id, out RECT rect);
}
