using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace AgentVitals;

/// <summary>Журнал рядом с программой: что не получилось и что человек поменял.</summary>
internal static class Journal
{
	private const string _timeFormat = "yyyy-MM-dd HH:mm:ss";

	// Журнал отвечает на вопрос «почему ночью квоты не грузились», а не «что было месяц назад».
	private const int _keepDays = 2;
	private const int _keepLines = 500;
	private const long _sizeLimit = 1_000_000;

	private static readonly string _path = Path.Combine(Paths.Data, "log.txt");

	public static void Add(string message)
	{
		try
		{
			if (File.Exists(_path) && new FileInfo(_path).Length > _sizeLimit)
			{
				Trim();
			}

			var line = $"{DateTime.Now.ToString(_timeFormat, CultureInfo.InvariantCulture)}\t{message.ReplaceLineEndings(" ")}";
			File.AppendAllText(_path, line + Environment.NewLine, new UTF8Encoding(true));
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			// журнал — не повод падать, а папка может быть доступна только на чтение
		}
	}

	/// <summary>Выбросить старое: зовётся при запуске.</summary>
	public static void Trim()
	{
		try
		{
			if (!File.Exists(_path))
			{
				return;
			}

			var since = DateTime.Now.AddDays(-_keepDays);
			var lines = File.ReadAllLines(_path);
			var kept = lines
				.Where(line => line.Length >= _timeFormat.Length
					&& DateTime.TryParseExact(line[.._timeFormat.Length].TrimStart('﻿'), _timeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
					&& time >= since)
				.TakeLast(_keepLines)
				.ToList();

			if (kept.Count != lines.Length)
			{
				File.WriteAllLines(_path, kept, new UTF8Encoding(true));
			}
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			// журнал — не повод падать
		}
	}
}
