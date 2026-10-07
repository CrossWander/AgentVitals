using System.IO;
using System.Text;
using Xunit;

namespace AgentVitals.Tests;

public class LogTailTests
{
	private static readonly byte[] _marker = Encoding.UTF8.GetBytes("\"assistant\"");

	private const string _first = """{"type":"assistant","n":1}""";
	private const string _second = """{"type":"assistant","n":2}""";

	[Fact]
	public void ReadsOnlyWhatWasAppended()
	{
		using var temp = new TempDirectory();
		var file = temp.File("s.jsonl");
		var tail = new LogTail();
		File.WriteAllText(file, _first + "\n");

		Assert.Equal([_first], tail.ReadNew(file, _marker));

		File.AppendAllText(file, _second + "\n");
		Assert.Equal([_second], tail.ReadNew(file, _marker));
	}

	[Fact]
	public void WaitsForTheLineEnd() // Claude Code ещё дописывает строку
	{
		using var temp = new TempDirectory();
		var file = temp.File("s.jsonl");
		var tail = new LogTail();
		File.WriteAllText(file, _first + "\n" + """{"type":"assistant",""");

		Assert.Equal([_first], tail.ReadNew(file, _marker));

		File.AppendAllText(file, "\"n\":2}\n");
		Assert.Equal([_second], tail.ReadNew(file, _marker));
	}

	[Fact]
	public void CrlfLinesComeClean()
	{
		using var temp = new TempDirectory();
		var file = temp.File("s.jsonl");
		File.WriteAllText(file, _first + "\r\n");

		Assert.Equal([_first], new LogTail().ReadNew(file, _marker));
	}

	[Fact]
	public void SkipsLinesWithoutTheMarker()
	{
		using var temp = new TempDirectory();
		var file = temp.File("s.jsonl");
		File.WriteAllText(file, """{"type":"user"}""" + "\n" + _first + "\n");

		Assert.Equal([_first], new LogTail().ReadNew(file, _marker));
	}

	[Fact]
	public void ShorterFileIsReadAgain() // файл переписали
	{
		using var temp = new TempDirectory();
		var file = temp.File("s.jsonl");
		var tail = new LogTail();
		File.WriteAllText(file, _first + "\n" + _second + "\n");
		tail.ReadNew(file, _marker);

		File.WriteAllText(file, _second + "\n");
		Assert.Equal([_second], tail.ReadNew(file, _marker));
	}

	[Fact]
	public void UnchangedFileIsNotOpened()
	{
		using var temp = new TempDirectory();
		var file = temp.File("s.jsonl");
		var tail = new LogTail();
		File.WriteAllText(file, _first + "\n");
		tail.ReadNew(file, _marker);

		// Открыть файл, занятый вот так, нельзя: исключения нет — значит, и не открывали.
		using var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);
		Assert.Empty(tail.ReadNew(file, _marker));
	}

	[Fact]
	public void ForgottenFileIsReadFromTheStart()
	{
		using var temp = new TempDirectory();
		var file = temp.File("s.jsonl");
		var tail = new LogTail();
		File.WriteAllText(file, _first + "\n");
		tail.ReadNew(file, _marker);

		tail.Keep([]);
		Assert.Equal([_first], tail.ReadNew(file, _marker));
	}
}
