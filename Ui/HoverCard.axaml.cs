using Avalonia;
using Avalonia.Controls;

namespace AgentVitals;

/// <summary>Карточка по наведению на значок: коротко о квотах, сессиях и статусе. Фокус не забирает.</summary>
internal partial class HoverCard : Window
{
	private PixelRect _anchor;

	public HoverCard()
	{
		InitializeComponent();
		SizeChanged += (_, _) => Placement.Place(this, _anchor);
	}

	public void Render(Vitals vitals) => DataContext = vitals;

	public void Open(PixelRect anchor)
	{
		_anchor = anchor;
		Show();
		Placement.Place(this, anchor);
	}
}
