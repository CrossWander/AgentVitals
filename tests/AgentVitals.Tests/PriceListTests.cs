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
		var prices = PriceList.Parse(Page);

		Assert.Equal(6, prices.Count); // вторая таблица — другой раздел, её цены не в счёт
		Assert.Equal(new Price(4, 5, 8, 0.20m, 20), prices.Find("claude-opus-5-5"));
	}

	[Fact]
	public void StripsLinksAndFootnotes()
	{
		var prices = PriceList.Parse(Page);

		Assert.NotNull(prices.Find("claude-mythos-5-1"));
		Assert.Equal(new Price(2, 2.50m, 4, 0.20m, 10), prices.Find("claude-sonnet-5"));
	}

	[Fact]
	public void DatedIdTakesItsModelsPrice() =>
		Assert.Equal(new Price(1, 1.25m, 2, 0.10m, 5), PriceList.Parse(Page).Find("claude-haiku-4-5-20251001"));

	[Fact]
	public void NewerModelGetsNoPrice()
	{
		var prices = PriceList.Parse(Page);

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

		Assert.Equal(new Price(4, 5, 8, 0.20m, 20), PriceList.Parse(page).Find("claude-opus-5-5"));
	}

	[Fact]
	public void PageWithoutTheTableIsEmpty() => Assert.Equal(0, PriceList.Parse("# Pricing\n\nSee the website.").Count);

	[Fact]
	public void HourCacheCostsMoreThanFiveMinutes()
	{
		var record = new UsageRecord("a", DateTime.UtcNow, "claude-opus-5-5", 1_000_000, 1_000_000, 2_000_000, 1_000_000, 10_000_000);

		// вход 4 + выход 20 + запись 5 мин 5 + запись 1 ч 8 + чтение 10 × 0.20
		Assert.Equal(39m, PriceList.Cost(record, new Price(4, 5, 8, 0.20m, 20)));
	}
}
