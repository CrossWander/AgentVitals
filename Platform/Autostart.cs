using System;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace AgentVitals;

/// <summary>Автозапуск через HKCU\...\Run: прав администратора не нужно, источник истины — сам реестр.</summary>
[SupportedOSPlatform("windows")]
internal static class Autostart
{
	private const string _runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
	private const string _name = "AgentVitals";

	public static bool IsEnabled()
	{
		using var key = Registry.CurrentUser.OpenSubKey(_runKey);

		return key?.GetValue(_name) is string;
	}

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
