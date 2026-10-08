using System;
using System.IO;
using System.Threading;

namespace AgentVitals;

/// <summary>
/// Сообщает, что файл переписали. Зовёт через секунду тишины: пишут его иногда в несколько приёмов,
/// а полупустой файл входа поставил бы опрос на паузу в пять минут.
/// </summary>
internal sealed class FileNotice : IDisposable
{
	private static readonly TimeSpan _settle = TimeSpan.FromSeconds(1);

	private readonly FileSystemWatcher? _watcher;
	private readonly Timer _timer;

	/// <param name="path">Файл.</param>
	/// <param name="changed">Что сделать; зовётся из пула потоков.</param>
	public FileNotice(string path, Action changed)
	{
		_timer = new(_ => changed());

		// Папки нет — агент не установлен; появится — его подхватит обычный тик опроса.
		if (Path.GetDirectoryName(path) is not { } folder || !Directory.Exists(folder))
		{
			return;
		}

		_watcher = new(folder, Path.GetFileName(path))
		{
			NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
		};

		_watcher.Changed += (_, _) => Restart();
		_watcher.Created += (_, _) => Restart();
		_watcher.Renamed += (_, _) => Restart();
		_watcher.EnableRaisingEvents = true;
	}

	public void Dispose()
	{
		_watcher?.Dispose();
		_timer.Dispose();
	}

	private void Restart() => _timer.Change(_settle, Timeout.InfiniteTimeSpan);
}
