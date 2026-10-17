using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AgentVitals.Platform;

namespace AgentVitals.Core.Usage;

/// <summary>Дочитывает растущие файлы строк: для каждого помнит, до какого байта он разобран.</summary>
public sealed class LogTail
{
	private readonly Dictionary<string, long> _read = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Полные строки, дописанные с прошлого раза, — только те, где есть marker.</summary>
	/// <param name="path">Файл.</param>
	/// <param name="marker">Байты, без которых строка не нужна: её незачем и декодировать.</param>
	/// <returns>Строки без перевода строки.</returns>
	public IReadOnlyList<string> ReadNew(string path, byte[] marker)
	{
		var offset = _read.GetValueOrDefault(path);

		// Размер — из каталога, без открытия: неизменные файлы — почти все.
		if (new FileInfo(path).Length == offset)
		{
			return [];
		}

		using var stream = SharedFile.Open(path);

		// Короче, чем уже прочитано, — файл переписали: читаем заново.
		if (stream.Length < offset)
		{
			offset = 0;
		}

		stream.Position = offset;
		var bytes = new byte[stream.Length - offset];
		stream.ReadExactly(bytes);

		// Строку без перевода строки ещё дописывают: её место — до следующего раза.
		var end = Array.LastIndexOf(bytes, (byte)'\n');
		_read[path] = offset + end + 1;

		List<string> lines = [];
		Span<byte> rest = bytes.AsSpan(0, end + 1);

		while (rest.Length > 0)
		{
			var stop = rest.IndexOf((byte)'\n');
			var line = rest[..stop].TrimEnd((byte)'\r');

			if (line.IndexOf(marker) >= 0)
			{
				lines.Add(Encoding.UTF8.GetString(line));
			}

			rest = rest[(stop + 1)..];
		}

		return lines;
	}

	/// <summary>Забыть файлы, которых больше нет среди читаемых; вернутся — прочитаются с начала.</summary>
	public void Keep(IReadOnlyCollection<string> paths)
	{
		var keep = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);

		foreach (var gone in _read.Keys.Where(path => !keep.Contains(path)).ToList())
		{
			_read.Remove(gone);
		}
	}
}
