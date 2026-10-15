using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using Xunit;

namespace AgentVitals.Tests;

public class CodexLogTests
{
	private static readonly DateTime _now = DateTime.UtcNow;

	// Строки журнала Codex в одну строку, как он их пишет: модель хода и расход одного ответа.
	private static string Turn(string turn, string model) => new JsonObject
	{
		["timestamp"] = _now.ToString("o", CultureInfo.InvariantCulture),
		["type"] = "turn_context",
		["payload"] = new JsonObject { ["turn_id"] = turn, ["cwd"] = @"C:\x", ["model"] = model },
	}.ToJsonString();

	private static string Answer(string response, string turn, DateTime at, long input = 13_390, long cached = 11_008, long written = 0, long output = 81) => new JsonObject
	{
		["timestamp"] = at.ToString("o", CultureInfo.InvariantCulture),
		["type"] = "token_usage_record",
		["payload"] = new JsonObject
		{
			["turn_id"] = turn,
			["response_id"] = response,
			["usage"] = new JsonObject
			{
				["input_tokens"] = input,
				["cached_input_tokens"] = cached,
				["cache_write_input_tokens"] = written,
				["output_tokens"] = output,
				["reasoning_output_tokens"] = 0,
				["total_tokens"] = input + output,
			},
		},
	}.ToJsonString();

	private static string Write(string home, string relative, params string[] lines)
	{
		var file = Path.Combine(home, relative);
		Directory.CreateDirectory(Path.GetDirectoryName(file)!);
		File.WriteAllText(file, string.Join("\n", lines) + "\n");

		return file;
	}

	private static CodexLog Log(TempDirectory temp) => new(temp.Path, temp.File("prices-openai.md"));

	[Fact]
	public void AnswerBecomesARecord() // прочитанное из кэша и записанное в кэш входят в input_tokens
	{
		using var temp = new TempDirectory();
		Write(temp.Path, @"sessions\2026\10\15\rollout-a.jsonl", Turn("t1", "gpt-6-luna"), Answer("r1", "t1", _now, input: 13_390, cached: 11_008, written: 100, output: 81));
		var log = Log(temp);

		log.Scan(_now);

		var record = Assert.Single(log.Records);
		Assert.Equal(("gpt-6-luna", 2_282L, 81L, 100L, 0L, 11_008L), (record.Model, record.Input, record.Output, record.CacheWrite, record.CacheWrite1h, record.CacheRead));
	}

	[Fact]
	public void ModelOfTheTurnSurvivesBetweenReads() // turn_context дописан в прошлый раз, ответ — сейчас
	{
		using var temp = new TempDirectory();
		var file = Write(temp.Path, @"sessions\2026\10\15\rollout-a.jsonl", Turn("t1", "gpt-6-luna"));
		var log = Log(temp);
		log.Scan(_now);

		File.AppendAllText(file, Answer("r1", "t1", _now) + "\n");
		log.Scan(_now);

		Assert.Equal("gpt-6-luna", Assert.Single(log.Records).Model);
	}

	[Fact]
	public void ArchivedSessionsCount()
	{
		using var temp = new TempDirectory();
		Write(temp.Path, @"sessions\2026\10\15\rollout-a.jsonl", Turn("t1", "gpt-6-luna"), Answer("r1", "t1", _now));
		Write(temp.Path, @"archived_sessions\rollout-b.jsonl", Turn("t2", "gpt-6-sol"), Answer("r2", "t2", _now));
		var log = Log(temp);

		log.Scan(_now);

		Assert.Equal(2, log.Records.Count);
	}

	[Fact]
	public void ReadingAFileAgainDoesNotDouble()
	{
		using var temp = new TempDirectory();
		var file = Write(temp.Path, @"sessions\2026\10\15\rollout-a.jsonl", Turn("t1", "gpt-6-luna"), Answer("r1", "t1", _now));
		var log = Log(temp);
		log.Scan(_now);

		File.SetLastWriteTimeUtc(file, _now.AddDays(-40)); // выпал из окна — забыт
		log.Scan(_now);
		File.SetLastWriteTimeUtc(file, _now); // вернулся — читается с начала
		log.Scan(_now);

		Assert.Single(log.Records);
	}

	[Fact]
	public void TurnWithoutContextHasNoModel() // без модели — без цены, но токены считаются
	{
		using var temp = new TempDirectory();
		Write(temp.Path, @"sessions\2026\10\15\rollout-a.jsonl", Answer("r1", "t9", _now));
		var log = Log(temp);

		log.Scan(_now);

		Assert.Equal("", Assert.Single(log.Records).Model);
	}

	[Fact]
	public void SkipsLinesThatAreNotAnswers()
	{
		Dictionary<string, string> models = [];

		Assert.Null(CodexLog.Parse(JsonNode.Parse("""{"timestamp":"2026-10-15T05:24:51.852Z","type":"token_usage_record","payload":{"turn_id":"t1","usage":{"input_tokens":5}}}"""), models)); // без response_id
		Assert.Null(CodexLog.Parse(JsonNode.Parse("""{"timestamp":"вчера","type":"token_usage_record","payload":{"turn_id":"t1","response_id":"r1","usage":{"input_tokens":5}}}"""), models));
		Assert.Null(CodexLog.Parse(JsonNode.Parse("""{"timestamp":"2026-10-15T05:24:51.852Z","type":"token_usage_record","payload":{"turn_id":"t1","response_id":"r1","usage":{"input_tokens":0,"output_tokens":0}}}"""), models));
	}

	[Fact]
	public void NoCodexFolderIsNoAnswers()
	{
		using var temp = new TempDirectory();
		var log = new CodexLog(temp.File("missing"), temp.File("prices-openai.md"));

		log.Scan(_now);

		Assert.Empty(log.Records);
	}
}
