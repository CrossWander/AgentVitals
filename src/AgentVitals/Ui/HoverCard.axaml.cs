using Avalonia;
using Avalonia.Controls;

namespace AgentVitals;

/// <summary>Карточка по наведению на значок: коротко о квотах, сессиях и статусе. Фокус не забирает.</summary>
internal partial class HoverCard : TrayWindow
{
	public HoverCard() => InitializeComponent();

	public void Render(Vitals vitals) => DataContext = vitals;

	public void Open(PixelRect anchor) => ShowAt(anchor);
}
