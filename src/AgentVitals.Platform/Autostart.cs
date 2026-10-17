using System;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace AgentVitals.Platform;

/// <summary>Автозапуск через HKCU\...\Run: прав администратора не нужно, источник истины — сам реестр.</summary>
public static class Autostart
{
	private const string _runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
	private const string _name = "AgentVitals";

	// Включён, только если запись ведёт к этому exe: портабельную программу переносят, а старая запись остаётся.
	[SupportedOSPlatform("windows")]
	public static bool IsEnabled()
	{
		using var key = Registry.CurrentUser.OpenSubKey(_runKey);

		return Launches(key?.GetValue(_name), Environment.ProcessPath ?? "");
	}

	internal static bool Launches(object? value, string exe) =>
		value is string command && string.Equals(command.Trim().Trim('"'), exe, StringComparison.OrdinalIgnoreCase);

	[SupportedOSPlatform("windows")]
	public static void Set(bool enabled)
	{
		using var key = Registry.CurrentUser.CreateSubKey(_runKey);

		if (enabled)
		{
			key.SetValue(_name, $"\"{Environment.ProcessPath}\"");
		}
		else
		{
			key.DeleteValue(_name, throwOnMissingValue: false);
		}
	}
}
