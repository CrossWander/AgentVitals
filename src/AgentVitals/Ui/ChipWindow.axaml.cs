using Avalonia;

namespace AgentVitals;

/// <summary>Значок над треем: в углу у трея, в полоску не сжимается; после задержки над ним — панель.</summary>
internal partial class ChipWindow : WidgetWindow
{
	// С полем под тень внутри окна — те же 12 точек от угла, что на макете.
	private const double _margin = 4;

	public ChipWindow()
	{
		InitializeComponent();
		Wire(Rows);
	}

	protected override Visual Hit => Body;

	protected override void Place(PixelRect anchor) => Placement.PlaceChip(this, anchor, _margin);

	protected override void Look(WidgetLook look) => Drawer.IsVisible = look == WidgetLook.Panel;
}
