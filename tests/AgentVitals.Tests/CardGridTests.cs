using Xunit;

namespace AgentVitals.Tests;

public class CardGridTests
{
	[Theory]
	[InlineData(1, 0, 0, 0, 2)] // одна карточка (у Codex только «Месяц») — во всю ширину
	[InlineData(2, 1, 0, 1, 1)]
	[InlineData(3, 1, 0, 1, 1)]
	[InlineData(3, 2, 1, 0, 2)] // третья одна в своём ряду — во всю ширину
	[InlineData(4, 3, 1, 1, 1)]
	public void LoneCardInTheLastRowTakesTheWholeWidth(int count, int index, int row, int column, int span) =>
		Assert.Equal((row, column, span), CardGrid.Cell(index, count, wide: false));

	[Theory]
	[InlineData(2, 0, 0)] // у Copilot — чат и подсказки: на вкладке только квоты, каждая в своём ряду
	[InlineData(2, 1, 1)]
	[InlineData(4, 3, 3)] // у Cursor — месяц, Auto, API и расход сверх плана
	public void OnlyQuotasOnTheTabTakeTheWholeWidth(int count, int index, int row) =>
		Assert.Equal((row, 0, 2), CardGrid.Cell(index, count, wide: true));
}
