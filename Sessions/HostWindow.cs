using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;

namespace AgentVitals;

/// <summary>
/// Окно программы, в которой идёт сессия Claude Code: терминал, VS Code, JetBrains. Щелчок по тосту о сессии
/// выводит его вперёд, чтобы сразу ответить. Активируется окно, а не вкладка: её Windows снаружи не выбрать.
/// </summary>
internal static unsafe partial class HostWindow
{
	private const uint _owner = 4;
	private const int _restore = 9;

	/// <summary>Вывести вперёд окно, где идёт сессия.</summary>
	/// <param name="pid">Процесс Claude Code этой сессии.</param>
	/// <param name="path">Папка сессии: у VS Code все окна в одном процессе, различает их заголовок.</param>
	/// <returns>false — окна не нашлось: процесса уже нет или система не Windows.</returns>
	public static bool Activate(int pid, string path)
	{
		if (!OperatingSystem.IsWindows() || Find(pid, path) is not { } window)
		{
			return false;
		}

		// Свёрнутое окно SetForegroundWindow делает активным, но не разворачивает.
		if (IsIconic(window))
		{
			ShowWindow(window, _restore);
		}

		// Право выйти вперёд у программы есть: человек только что щёлкнул по её тосту.
		return SetForegroundWindow(window);
	}

	/// <summary>Окно, где идёт сессия; null — не нашлось.</summary>
	internal static nint? Find(int pid, string path)
	{
		try
		{
			var processes = ClaudeProcess.Snapshot();
			var windows = Windows();

			return Pick(
				ClaudeProcess.Lineage(processes, pid).Select(id => (processes[id].Name, windows.TryGetValue(id, out var own) ? own : (IReadOnlyList<(nint, string)>)[])),
				path);
		}
		catch (Exception exception) when (exception is Win32Exception or ExternalException)
		{
			return null;
		}
	}

	/// <summary>Окно ближайшего процесса в цепочке, у которого окна есть; среди них — с папкой сессии в заголовке, иначе верхнее.</summary>
	/// <param name="lineage">Процесс Claude Code и его родители по порядку, с видимыми окнами каждого сверху вниз.</param>
	/// <param name="path">Папка сессии.</param>
	internal static nint? Pick(IEnumerable<(string Process, IReadOnlyList<(nint Handle, string Title)> Windows)> lineage, string path)
	{
		// Сессию начинают и в подпапке проекта, а заголовок VS Code называет его корень: от самой глубокой папки вверх.
		var folders = path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).Reverse().ToList();

		foreach (var (process, windows) in lineage)
		{
			// Выше — рабочий стол и панель задач: окна сессии там уже нет.
			if (process.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}

			if (windows.Count > 0)
			{
				return folders
					.SelectMany(folder => windows.Where(window => window.Title.Contains(folder, StringComparison.OrdinalIgnoreCase)))
					.Select(window => (nint?)window.Handle)
					.FirstOrDefault() ?? windows[0].Handle;
			}
		}

		return null;
	}

	/// <summary>Видимые окна верхнего уровня с заголовком по процессам, сверху вниз.</summary>
	private static Dictionary<int, List<(nint, string)>> Windows()
	{
		Dictionary<int, List<(nint, string)>> windows = [];
		var handle = GCHandle.Alloc(windows);

		try
		{
			EnumWindows(&Collect, GCHandle.ToIntPtr(handle));
		}
		finally
		{
			handle.Free();
		}

		return windows;
	}

	[UnmanagedCallersOnly]
	private static int Collect(nint window, nint state)
	{
		// Окна с владельцем — диалоги и всплывающие панели; без заголовка — служебные.
		if (!IsWindowVisible(window) || GetWindow(window, _owner) != 0)
		{
			return 1;
		}

		var text = stackalloc char[256];
		var length = GetWindowTextW(window, text, 256);

		if (length > 0)
		{
			uint pid;
			GetWindowThreadProcessId(window, &pid);

			var windows = (Dictionary<int, List<(nint, string)>>)GCHandle.FromIntPtr(state).Target!;

			if (!windows.TryGetValue((int)pid, out var own))
			{
				windows[(int)pid] = own = [];
			}

			own.Add((window, new string(text, 0, length)));
		}

		return 1;
	}

	[LibraryImport("user32.dll")]
	private static partial int EnumWindows(delegate* unmanaged<nint, nint, int> callback, nint state);

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool IsWindowVisible(nint window);

	[LibraryImport("user32.dll")]
	private static partial nint GetWindow(nint window, uint command);

	[LibraryImport("user32.dll")]
	private static partial int GetWindowTextW(nint window, char* text, int max);

	[LibraryImport("user32.dll")]
	private static partial uint GetWindowThreadProcessId(nint window, uint* pid);

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool IsIconic(nint window);

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool ShowWindow(nint window, int command);

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool SetForegroundWindow(nint window);
}
