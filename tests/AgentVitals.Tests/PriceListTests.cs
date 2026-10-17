using System;
using Xunit;

namespace AgentVitals.Tests;

public class PriceListTests
{
	// Кусок настоящей страницы platform.claude.com/docs/en/about-claude/pricing.md: ссылки и сноски в ячейках.
	internal const string Page = """
		# Pricing

		## Model pricing

		The following table shows pricing for all Claude models:

		| Model | Base input tokens | 5m cache writes | 1h cache writes | Cache hits and refreshes | Output tokens |
		| --- | --- | --- | --- | --- | --- |
		| Claude Fable 5.1 | $10 / MTok | $12.50 / MTok | $20 / MTok | $0.25 / MTok<sup>1</sup> | $50 / MTok |
		| Claude Mythos 5.1 ([limited availability](https://support.claude.com/en/articles/14604842)) | $10 / MTok | $12.50 / MTok | $20 / MTok | $0.25 / MTok<sup>1</sup> | $50 / MTok |
		| Claude Opus 5.5 | $4 / MTok | $5 / MTok | $8 / MTok | $0.20 / MTok<sup>2</sup> | $20 / MTok |
		| Claude Opus 5 | $5 / MTok | $6.25 / MTok | $10 / MTok | $0.50 / MTok | $25 / MTok |
		| Claude Sonnet 5 | $2 / MTok<sup>3</sup> | $2.50 / MTok | $4 / MTok | $0.20 / MTok | $10 / MTok<sup>3</sup> |
		| Claude Haiku 4.5 | $1 / MTok | $1.25 / MTok | $2 / MTok | $0.10 / MTok | $5 / MTok |

		## Cloud platform pricing

		| Model | Base input tokens | 5m cache writes | 1h cache writes | Cache hits and refreshes | Output tokens |
		| --- | --- | --- | --- | --- | --- |
		| Claude Opus 5.5 | $99 / MTok | $99 / MTok | $99 / MTok | $99 / MTok | $99 / MTok |
		""";

	[Fact]
	public void ReadsTheModelTable()
	{
		var prices = ClaudePrices.Parse(Page);

		Assert.Equal(6, prices.Count); // вторая таблица — другой раздел, её цены не в счёт
		Assert.Equal(new Price(4, 5, 8, 0.20m, 20), prices.Find("claude-opus-5-5"));
	}

	[Fact]
	public void StripsLinksAndFootnotes()
	{
		var prices = ClaudePrices.Parse(Page);

		Assert.NotNull(prices.Find("claude-mythos-5-1"));
		Assert.Equal(new Price(2, 2.50m, 4, 0.20m, 10), prices.Find("claude-sonnet-5"));
	}

	[Fact]
	public void DatedIdTakesItsModelsPrice() =>
		Assert.Equal(new Price(1, 1.25m, 2, 0.10m, 5), ClaudePrices.Parse(Page).Find("claude-haiku-4-5-20251001"));

	[Fact]
	public void NewerModelGetsNoPrice()
	{
		var prices = ClaudePrices.Parse(Page);

		Assert.Null(prices.Find("claude-opus-5-6"));
		Assert.Equal(5, prices.Find("claude-opus-5")!.Input); // не цена opus-5-5, хоть имя и короче
	}

	[Fact]
	public void FindsColumnsByName() // Anthropic переставила колонки и добавила новую
	{
		const string page = """
			## Model pricing

			| Model | Output tokens | Batch | Base input tokens | 5m cache writes | 1h cache writes | Cache hits |
			|---|---|---|---|---|---|---|
			| Claude Opus 5.5 | $20 / MTok | $2 / MTok | $4 / MTok | $5 / MTok | $8 / MTok | $0.20 / MTok |
			""";

		Assert.Equal(new Price(4, 5, 8, 0.20m, 20), ClaudePrices.Parse(page).Find("claude-opus-5-5"));
	}

	[Fact]
	public void PageWithoutTheTableIsEmpty() => Assert.Equal(0, ClaudePrices.Parse("# Pricing\n\nSee the website.").Count);

	[Fact]
	public void HourCacheCostsMoreThanFiveMinutes()
	{
		var record = new UsageRecord("a", DateTime.UtcNow, "claude-opus-5-5", 1_000_000, 1_000_000, 2_000_000, 1_000_000, 10_000_000);

		// вход 4 + выход 20 + запись 5 мин 5 + запись 1 ч 8 + чтение 10 × 0.20
		Assert.Equal(39m, PriceList.Cost(record, new Price(4, 5, 8, 0.20m, 20)));
	}

	// Кусок настоящей страницы developers.openai.com/api/docs/pricing.md от 2026-10-15: подписи тарифов над таблицами.
	internal const string OpenAiPage = """
		# Pricing

		Standard

		### Standard pricing data

		| Model | Short context input | Short context cached input | Short context cache writes | Short context output | Long context input | Long context cached input | Long context cache writes | Long context output |
		| --- | --- | --- | --- | --- | --- | --- | --- | --- |
		| gpt-6-luna | $0.10 | $0.01 | $0.125 | $0.50 | $0.20 | $0.02 | $0.25 | $0.75 |
		| gpt-5.5 (<272K context length) | $5.00 | $0.50 | - | $30.00 | $10.00 | $1.00 | - | $45.00 |
		| gpt-5.5-pro (<272K context length) | $30.00 | - | - | $180.00 | $60.00 | - | - | $270.00 |

		Batch

		### Batch pricing data

		| Model | Short context input | Short context cached input | Short context cache writes | Short context output | Long context input | Long context cached input | Long context cache writes | Long context output |
		| --- | --- | --- | --- | --- | --- | --- | --- | --- |
		| gpt-6-luna | $0.05 | $0.005 | $0.0625 | $0.25 | $0.10 | $0.01 | $0.125 | $0.375 |
		| gpt-6-batch-only | $1.00 | $0.10 | $1.25 | $5.00 | - | - | - | - |

		Prices per 1M tokens.

		### Grouped Pricing Table data

		| Model | Modality | Input | Cached input | Output |
		| --- | --- | --- | --- | --- |
		| gpt-realtime | Text | $4.00 | $0.40 | $16.00 |

		Standard

		### Grouped Pricing Table data

		| Category | Model | Input | Cached input | Output |
		| --- | --- | --- | --- | --- |
		| Codex | gpt-5.3-codex | $1.75 | $0.175 | $14.00 |
		| Embedding | text-embedding-3-small | $0.02 | - | - |
		| Moderation | omni-moderation-latest | Free | - | - |
		""";

	[Fact]
	public void ReadsOpenAiStandardPrices()
	{
		var prices = OpenAiPrices.Parse(OpenAiPage);

		Assert.Equal(new Price(0.10m, 0.125m, 0.125m, 0.01m, 0.50m), prices.Find("gpt-6-luna")); // Standard, а не Batch
		Assert.Equal(new Price(1.75m, 1.75m, 1.75m, 0.175m, 14m), prices.Find("gpt-5.3-codex")); // таблица с колонкой Category
		Assert.Null(prices.Find("gpt-6-batch-only")); // есть только в Batch
		Assert.Null(prices.Find("gpt-realtime")); // таблица без подписи Standard
		Assert.Equal(4, prices.Count); // без цены выхода (эмбеддинги, Free) — строки пропущены
	}

	[Fact]
	public void DashMeansThePlainInputPrice() // нет цены кэша — он платится как обычный вход
	{
		var prices = OpenAiPrices.Parse(OpenAiPage);

		Assert.Equal(new Price(5m, 5m, 5m, 0.50m, 30m), prices.Find("gpt-5.5")); // приписка «(<272K context length)» снята
		Assert.Equal(new Price(30m, 30m, 30m, 30m, 180m), prices.Find("gpt-5.5-pro"));
	}

	[Fact]
	public void OpenAiPageWithoutStandardTablesIsEmpty() => Assert.Equal(0, OpenAiPrices.Parse("<html>редизайн</html>").Count);
}
