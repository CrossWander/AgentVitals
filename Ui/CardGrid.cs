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
	public static readonly StyledProperty<bool> WideProperty = AvaloniaProperty.Register<CardGrid, bool>(nameof(Wide));

	static CardGrid() => AffectsMeasure<CardGrid>(WideProperty);

	/// <summary>Каждая карточка в своём ряду во всю ширину: на вкладке только квоты, и высоту беречь незачем.
	/// Пары — там, где ниже статистика и сессии.</summary>
	public bool Wide
	{
		get => GetValue(WideProperty);
		set => SetValue(WideProperty, value);
	}

	/// <summary>Место карточки: ряд, колонка и сколько колонок она занимает.</summary>
	internal static (int Row, int Column, int Span) Cell(int index, int count, bool wide) =>
		wide ? (index, 0, 2) : (index / 2, index % 2, index == count - 1 && count % 2 == 1 ? 2 : 1);

	protected override Size MeasureOverride(Size availableSize)
	{
		var column = availableSize.Width / 2;
		double height = 0;
		double width = 0;

		for (var index = 0; index < Children.Count; index++)
		{
			var span = Cell(index, Children.Count, Wide).Span;
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
			var (row, left, span) = Cell(index, Children.Count, Wide);
			Children[index].Arrange(new Rect(left * column, row * height, column * span, height));
		}

		return finalSize;
	}

	private int Rows() => Children.Count == 0 ? 0 : Cell(Children.Count - 1, Children.Count, Wide).Row + 1;
}
