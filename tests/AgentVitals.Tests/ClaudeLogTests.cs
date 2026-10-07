using System;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using Xunit;

namespace AgentVitals.Tests;

public class ClaudeLogTests
{
	private static readonly DateTime _now = DateTime.UtcNow;

	// Строка лога Claude Code в одну строку, как он её пишет.
	private static string Answer(string id, DateTime at, int output, string type = "assistant") =>
		new JsonObject
		{
			["type"] = type,
			["timestamp"] = at.ToString("o", CultureInfo.InvariantCulture),
			["message"] = new JsonObject
			{
				["id"] = id,
				["model"] = "claude-opus-5-5",
				["usage"] = new JsonObject
				{
					["input_tokens"] = 1,
					["output_tokens"] = output,
					["cache_creation_input_tokens"] = 0,
					["cache_read_input_tokens"] = 0,
				},
			},
		}.ToJsonString();

	private static string Write(string folder, string relative, params string[] lines)
	{
		var file = Path.Combine(folder, relative);
		Directory.CreateDirectory(Path.GetDirectoryName(file)!);
		File.WriteAllText(file, string.Join("\n", lines) + "\n");

		return file;
	}

	[Fact]
	public void LastCopyOfAnAnswerWins() // пока ответ идёт, Claude Code пишет его снова с растущим output
	{
		using var temp = new TempDirectory();
		Write(temp.Path, @"p\s.jsonl", Answer("m1", _now, 5), Answer("m1", _now.AddSeconds(3), 40));
		var log = new ClaudeLog(temp.Path, temp.File("prices.md"));

		log.Scan(_now);

		Assert.Equal(40, Assert.Single(log.Records).Output);
	}

	[Fact]
	public void SubagentLogsCount() // их расход в основной лог не попадает
	{
		using var temp = new TempDirectory();
		Write(temp.Path, @"p\s.jsonl", Answer("m1", _now, 5));
		Write(temp.Path, @"p\s\subagents\agent-1.jsonl", Answer("m2", _now, 7));
		var log = new ClaudeLog(temp.Path, temp.File("prices.md"));

		log.Scan(_now);

		Assert.Equal(2, log.Records.Count);
	}

	[Fact]
	public void SkipsBrokenLines()
	{
		Assert.Null(ClaudeLog.Parse(Answer("m1", _now, 5, type: "user")));
		Assert.Null(ClaudeLog.Parse("""{"type":"assistant","timestamp":"2026-10-07T10:00:00.000Z","message":{"usage":{"output_tokens":5}}}""")); // без id
		Assert.Null(ClaudeLog.Parse("""{"type":"assistant","timestamp":"вчера","message":{"id":"m1","usage":{"output_tokens":5}}}"""));
		Assert.Null(ClaudeLog.Parse("""{"type":"assistant", обрыв"""));
		Assert.Null(ClaudeLog.Parse("""{"type":"assistant","timestamp":"2026-10-07T10:00:00.000Z","message":{"id":"m1","model":"<synthetic>","usage":{"input_tokens":0,"output_tokens":0}}}"""));
	}

	[Fact]
	public void ReadsTheHourCacheApart()
	{
		var record = ClaudeLog.Parse("""{"type":"assistant","timestamp":"2026-10-07T10:00:00.000Z","message":{"id":"m1","model":"claude-opus-5-5","usage":{"input_tokens":1,"output_tokens":2,"cache_creation_input_tokens":30,"cache_read_input_tokens":4,"cache_creation":{"ephemeral_5m_input_tokens":10,"ephemeral_1h_input_tokens":20}}}}""");

		Assert.Equal(new UsageRecord("m1", new DateTime(2026, 10, 7, 10, 0, 0, DateTimeKind.Utc), "claude-opus-5-5", 1, 2, 30, 20, 4), record);
	}

	[Fact]
	public void ReadingAFileAgainDoesNotDouble()
	{
		using var temp = new TempDirectory();
		var file = Write(temp.Path, @"p\s.jsonl", Answer("m1", _now, 5));
		var log = new ClaudeLog(temp.Path, temp.File("prices.md"));
		log.Scan(_now);

		File.SetLastWriteTimeUtc(file, _now.AddDays(-40)); // выпал из окна — забыт
		log.Scan(_now);
		File.SetLastWriteTimeUtc(file, _now); // вернулся — читается с начала
		log.Scan(_now);

		Assert.Equal(5, Assert.Single(log.Records).Output);
	}

	[Fact]
	public void OldFilesAreNotRead()
	{
		using var temp = new TempDirectory();
		var file = Write(temp.Path, @"p\s.jsonl", Answer("m1", _now, 5));
		File.SetLastWriteTimeUtc(file, _now.AddDays(-40));
		var log = new ClaudeLog(temp.Path, temp.File("prices.md"));

		log.Scan(_now);

		Assert.Empty(log.Records);
	}

	[Fact]
	public void AnswersOlderThanAMonthAreDropped()
	{
		using var temp = new TempDirectory();
		Write(temp.Path, @"p\s.jsonl", Answer("old", _now.AddDays(-35), 5), Answer("new", _now, 7));
		var log = new ClaudeLog(temp.Path, temp.File("prices.md"));

		log.Scan(_now);

		Assert.Equal("new", Assert.Single(log.Records).Id);
	}

	[Fact]
	public void NoProjectsFolderIsNoAnswers()
	{
		using var temp = new TempDirectory();
		var log = new ClaudeLog(temp.File("missing"), temp.File("prices.md"));

		log.Scan(_now);

		Assert.Empty(log.Records);
	}

	[Fact]
	public void BadPageKeepsTheSavedCopy() // Anthropic поменяла вёрстку страницы
	{
		using var temp = new TempDirectory();
		var saved = temp.File("prices.md");
		var log = new ClaudeLog(temp.Path, saved);

		Assert.True(log.Accept(PriceListTests.Page));
		Assert.False(log.Accept("<html>редизайн</html>"));

		Assert.Equal(PriceListTests.Page, File.ReadAllText(saved));
		Assert.Equal(6, log.Prices!.Count);
	}

	[Fact]
	public void SavedPricesWorkOffline()
	{
		using var temp = new TempDirectory();
		var saved = temp.File("prices.md");
		File.WriteAllText(saved, PriceListTests.Page);
		var log = new ClaudeLog(temp.Path, saved);

		log.LoadSaved();

		Assert.Equal(6, log.Prices!.Count);
	}
}
