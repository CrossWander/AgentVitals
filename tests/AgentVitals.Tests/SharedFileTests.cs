using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using Xunit;

namespace AgentVitals.Tests;

public class SharedFileTests
{
	private static JsonNode Stop => JsonNode.Parse("""{ "session_id": "s1", "hook_event_name": "Stop", "cwd": "C:\\x" }""")!;

	[Fact]
	public void ReadingDoesNotBlockWriteInPlace() // так пишет свои файлы Claude Code (Node)
	{
		using var temp = new TempDirectory();
		var file = temp.File("s1.json");
		File.WriteAllText(file, "old");

		using (SharedFile.Open(file))
		{
			File.WriteAllText(file, "new");
		}

		Assert.Equal("new", SharedFile.ReadAllText(file));
	}

	[Fact]
	public void ReadingDoesNotBlockDelete() // SessionEnd удаляет файл сессии, пока трей его читает
	{
		using var temp = new TempDirectory();
		var file = temp.File("s1.json");
		File.WriteAllText(file, "old");

		using (SharedFile.Open(file))
		{
			File.Delete(file);
		}

		Assert.False(File.Exists(file));
	}

	[Fact]
	public void HookWaitsOutAReader() // подмену переименованием Windows не пускает, пока файл открыт
	{
		using var temp = new TempDirectory();
		var file = temp.File("s1.json");
		HookWriter.Apply(Stop, temp.Path, at: 1);

		// Читателя закрывает свой поток, а не продолжение после await: тому нужен свободный поток xUnit, а их
		// могут занять соседние тесты — на CI так и было, и hook исчерпывал попытки раньше, чем файл освобождался.
		var reader = SharedFile.Open(file);
		var release = new Thread(() =>
		{
			Thread.Sleep(30);
			reader.Dispose();
		});

		release.Start();
		HookWriter.Apply(Stop, temp.Path, at: 2);
		release.Join();

		Assert.Equal(2, HookWriter.Read(file)!.Utc);
	}

	[Fact]
	public void HookLeavesNoTempFileWhenReplaceFails()
	{
		using var temp = new TempDirectory();
		Directory.CreateDirectory(temp.File("s1.json")); // на месте файла — папка: подмена не удастся

		Assert.Throws<UnauthorizedAccessException>(() => HookWriter.Apply(Stop, temp.Path, at: 1));

		Assert.Empty(Directory.EnumerateFiles(temp.Path, "*.tmp"));
	}
}
