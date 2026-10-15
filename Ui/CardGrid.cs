using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace AgentVitals;

/// <summary>
/// Сетка карточек в две колонки, ячейки одной высоты — как UniformGrid, но карточка, оставшаяся
/// в последнем ряду одна, растягивается во всю ширину, а не стоит половинкой.
/// </summary>
internal sealed class CardGrid : Panel
{
	/// <summary>Место карточки: ряд, колонка и сколько колонок она занимает.</summary>
	internal static (int Row, int Column, int Span) Cell(int index, int count) =>
		(index / 2, index % 2, index == count - 1 && count % 2 == 1 ? 2 : 1);

	protected override Size MeasureOverride(Size availableSize)
	{
		var column = availableSize.Width / 2;
		double height = 0;
		double width = 0;

		for (var index = 0; index < Children.Count; index++)
		{
			var span = Cell(index, Children.Count).Span;
			Children[index].Measure(new Size(column * span, double.PositiveInfinity));
			height = Math.Max(height, Children[index].DesiredSize.Height);
			width = Math.Max(width, Children[index].DesiredSize.Width / span);
		}

		return new(double.IsInfinity(availableSize.Width) ? width * 2 : availableSize.Width, height * Rows());
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		var column = finalSize.Width / 2;
		var height = Children.Select(child => child.DesiredSize.Height).DefaultIfEmpty(0).Max();

		for (var index = 0; index < Children.Count; index++)
		{
			var (row, left, span) = Cell(index, Children.Count);
			Children[index].Arrange(new Rect(left * column, row * height, column * span, height));
		}

		return finalSize;
	}

	private int Rows() => (Children.Count + 1) / 2;
}
