using System.IO;
using Xunit;

namespace AgentVitals.Tests;

public class PriceSourceTests
{
	[Fact]
	public void BadPageKeepsTheSavedCopy() // Anthropic поменяла вёрстку страницы
	{
		using var temp = new TempDirectory();
		var saved = temp.File("prices.md");
		var source = new PriceSource(ClaudePrices.Url, saved, ClaudePrices.Parse);

		Assert.True(source.Accept(PriceListTests.Page));
		Assert.False(source.Accept("<html>редизайн</html>"));

		Assert.Equal(PriceListTests.Page, File.ReadAllText(saved));
		Assert.Equal(6, source.Prices!.Count);
	}

	[Fact]
	public void SavedPricesWorkOffline()
	{
		using var temp = new TempDirectory();
		var saved = temp.File("prices.md");
		File.WriteAllText(saved, PriceListTests.Page);
		var source = new PriceSource(ClaudePrices.Url, saved, ClaudePrices.Parse);

		source.LoadSaved();

		Assert.Equal(6, source.Prices!.Count);
	}

	[Fact]
	public void EachPageHasItsOwnParser() // страница OpenAI — не таблица Anthropic
	{
		using var temp = new TempDirectory();

		Assert.True(new PriceSource(OpenAiPrices.Url, temp.File("prices-openai.md"), OpenAiPrices.Parse).Accept(PriceListTests.OpenAiPage));
		Assert.False(new PriceSource(ClaudePrices.Url, temp.File("prices.md"), ClaudePrices.Parse).Accept(PriceListTests.OpenAiPage));
	}
}
