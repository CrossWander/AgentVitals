using System;
using Avalonia;
using Avalonia.Media.Transformation;

namespace AgentVitals;

/// <summary>Значок над треем: в углу у трея, в полоску не сжимается; щелчком над ним выезжает панель. Агенты — все сразу
/// или по очереди, как в настройках.</summary>
internal partial class ChipWindow : WidgetWindow
{
	// С полем под тень внутри окна — те же 12 точек от угла, что на макете.
	private const double _margin = 4;

	private bool _open;

	public ChipWindow()
	{
		InitializeComponent();
		Wire(Rows);
		Opens(Face);
		DataContextChanged += (_, _) => ShowAgents();
	}

	// Закрытая панель прозрачна, но место в окне занимает: мышь над ним — не над значком.
	protected override Visual Hit => _open ? Body : Face;

	public override void Tick()
	{
		base.Tick();
		ShowAgents();
	}

	protected override void Place(PixelRect anchor) => Placement.PlaceChip(this, anchor, _margin);

	protected override void Look(WidgetLook look)
	{
		_open = look == WidgetLook.Panel;
		Drawer.Opacity = _open ? 1 : 0;
		Drawer.IsHitTestVisible = _open;
		Drawer.RenderTransform = TransformOperations.Parse(_open ? "translateY(0px)" : "translateY(8px)");
	}

	/// <summary>Все агенты в ряд или один — по очереди; «ждёт» и «закончил» — всегда надпись.</summary>
	private void ShowAgents()
	{
		if (DataContext is not WidgetView view)
		{
			return;
		}

		var turns = Store.Current.ChipTurns;
		Row.IsVisible = view.ShowsAgents && !turns;
		Single.IsVisible = !Row.IsVisible;
		ShowFace(Single, turns ? view.Turned(DateTime.UtcNow) : view);
	}
}
