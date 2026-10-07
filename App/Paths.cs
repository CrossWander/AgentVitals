using System;
using System.IO;

namespace AgentVitals;

/// <summary>Где лежит то, что программа читает и пишет.</summary>
internal static class Paths
{
	public static string Home { get; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

	/// <summary>Состояния сессий Claude Code: их пишет режим hook, читает трей. Рядом с exe — программа портабельная.</summary>
	public static string Sessions { get; } = Path.Combine(AppContext.BaseDirectory, "sessions");
}
