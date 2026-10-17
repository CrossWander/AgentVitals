using System;
using System.IO;

namespace AgentVitals.Platform;

/// <summary>Где лежит то, что программа читает и пишет.</summary>
public static class Paths
{
	public static string Home { get; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

	/// <summary>Настройки, журнал и сессии: рядом с exe — программа портабельная; туда нельзя (Program Files) — в профиль.</summary>
	public static string Data { get; } = DataFolder(
		AppContext.BaseDirectory,
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentVitals"));

	/// <summary>Состояния сессий Claude Code: их пишет режим hook, читает трей.</summary>
	public static string Sessions { get; } = Path.Combine(Data, "sessions");

	internal static string DataFolder(string beside, string profile)
	{
		try
		{
			// Права на папку проще проверить делом, чем разбирать ACL: файл исчезнет сам при закрытии.
			using (File.Create(Path.Combine(beside, Path.GetRandomFileName()), 1, FileOptions.DeleteOnClose))
			{
			}

			return beside;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			Directory.CreateDirectory(profile);

			return profile;
		}
	}
}
