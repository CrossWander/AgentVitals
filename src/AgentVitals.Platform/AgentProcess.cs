using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace AgentVitals.Platform;

/// <summary>Процесс агента (Claude Code, Codex), запустивший hook, и проверка, что он ещё жив.</summary>
public static unsafe partial class AgentProcess
{
	private const uint _snapProcess = 0x2;

	/// <summary>Ближайший предок-агент процесса hook; null — hook запущен не им или Windows не дала снимок.</summary>
	/// <param name="names">Имена exe агента — HookTarget.Processes.</param>
	public static int? Find(IReadOnlySet<string> names)
	{
		// Снимок процессов — вызов Windows: в другой системе его нет, и hook упал бы, не записав состояние.
		// ponytail: там сессия без PID уходит по прежним срокам; понадобится — родитель из /proc.
		if (!OperatingSystem.IsWindows())
		{
			return null;
		}

		try
		{
			return Ancestor(Snapshot(), Environment.ProcessId, names);
		}
		catch (Exception exception) when (exception is Win32Exception or ExternalException)
		{
			return null;
		}
	}

	internal static int? Ancestor(IReadOnlyDictionary<int, (int Parent, string Name)> processes, int child, IReadOnlySet<string> names) =>
		Lineage(processes, child).Skip(1).Where(id => names.Contains(processes[id].Name)).Cast<int?>().FirstOrDefault();

	/// <summary>Процесс и его родители по порядку, пока они есть в снимке.</summary>
	internal static IEnumerable<int> Lineage(IReadOnlyDictionary<int, (int Parent, string Name)> processes, int pid)
	{
		// Цепочка родителей может замкнуться: номер умершего родителя Windows отдаёт новому процессу.
		for (var step = 0; step <= 16 && processes.TryGetValue(pid, out var process); step++)
		{
			yield return pid;
			pid = process.Parent;
		}
	}

	/// <summary>Процесс работает и это тот самый: занявший номер позже запущен после записи hook.</summary>
	/// <param name="since">Время события hook — Claude Code, запустивший его, стартовал раньше.</param>
	public static bool Alive(int pid, DateTime since)
	{
		try
		{
			using var process = Process.GetProcessById(pid);

			return !process.HasExited && process.StartTime.ToUniversalTime() <= since;
		}
		catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
		{
			return false;
		}
		catch (Win32Exception)
		{
			// Нет прав спросить — сессию наугад не хороним, её уберут прежние сроки.
			return true;
		}
	}

	internal static Dictionary<int, (int Parent, string Name)> Snapshot()
	{
		var snapshot = CreateToolhelp32Snapshot(_snapProcess, 0);

		if (snapshot == -1)
		{
			return [];
		}

		try
		{
			Dictionary<int, (int Parent, string Name)> processes = [];
			var entry = new PROCESSENTRY32W { dwSize = (uint)sizeof(PROCESSENTRY32W) };

			for (var more = Process32FirstW(snapshot, &entry); more; more = Process32NextW(snapshot, &entry))
			{
				processes[(int)entry.th32ProcessID] = ((int)entry.th32ParentProcessID, new string(entry.szExeFile));
			}

			return processes;
		}
		finally
		{
			CloseHandle(snapshot);
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct PROCESSENTRY32W
	{
		public uint dwSize;
		public uint cntUsage;
		public uint th32ProcessID;
		public nuint th32DefaultHeapID;
		public uint th32ModuleID;
		public uint cntThreads;
		public uint th32ParentProcessID;
		public int pcPriClassBase;
		public uint dwFlags;
		public fixed char szExeFile[260];
	}

	[LibraryImport("kernel32.dll")]
	private static partial IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

	[LibraryImport("kernel32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool Process32FirstW(IntPtr snapshot, PROCESSENTRY32W* entry);

	[LibraryImport("kernel32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool Process32NextW(IntPtr snapshot, PROCESSENTRY32W* entry);

	[LibraryImport("kernel32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool CloseHandle(IntPtr handle);
}
