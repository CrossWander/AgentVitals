using Xunit;

namespace AgentVitals.Tests;

// Без запущенного приложения Localization отдаёт ключ строки — по нему и видно, какая подпись выбрана.
public class TextTests
{
	[Theory]
	[InlineData(100, "langHealthExhausted")]
	[InlineData(130, "langHealthExhausted")]
	[InlineData(99.6, "langHealthExhausted")] // на карточке уже «100%»
	[InlineData(99.4, "langHealthCritical")]
	[InlineData(60, "langHealthWarning")]
	[InlineData(10, "langHealthGood")]
	public void BadgeSaysUsedUpAtHundred(double used, string key) => Assert.Equal(key, Text.Badge(used));
}
