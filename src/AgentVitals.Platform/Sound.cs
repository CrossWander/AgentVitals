using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace AgentVitals.Platform;

/// <summary>Два мягких звука, встроенных в exe (файлов рядом нет): стекло вверх — агент ждёт разрешения,
/// дерево вниз — квота почти исчерпана. Разные, чтобы различать на слух.</summary>
public static partial class Sound
{
	// SND_MEMORY — звук из массива; SND_NODEFAULT — не вышло, так тишина, а не системное «динь».
	private const uint _memory = 0x0004;
	private const uint _noDefault = 0x0002;

	/// <summary>Две ноты вверх: агент ждёт разрешения.</summary>
	public static byte[] Chime { get; } = Read("chime.wav");

	/// <summary>Деревянный молоточек, две ноты вниз: квота почти исчерпана.</summary>
	public static byte[] Low { get; } = Read("low.wav");

	/// <summary>Сыграть, не держа поток окна: без SND_ASYNC PlaySound ждёт конца звука, а массив закреплён лишь на время вызова.</summary>
	public static void Play(byte[] wave)
	{
		if (OperatingSystem.IsWindows())
		{
			_ = Task.Run(() => PlaySound(wave, 0, _memory | _noDefault));
		}
	}

	private static byte[] Read(string name)
	{
		using var stream = typeof(Sound).Assembly.GetManifestResourceStream(name)!;
		using var copy = new MemoryStream();
		stream.CopyTo(copy);

		return copy.ToArray();
	}

	[LibraryImport("winmm.dll", EntryPoint = "PlaySoundW")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool PlaySound(byte[] sound, nint module, uint flags);
}
