using System;
using Avalonia;
using Avalonia.Media.Transformation;

namespace AgentVitals;

/// <summary>Значок над треем: в углу у трея, в полоску не сжимается; щелчком над ним выезжает панель. Агенты — все сразу,
/// выбранный или по очереди, как в настройках.</summary>
internal partial class ChipWindow : WidgetWindow
{
	// С полем под тень и ореол внутри окна (20 точек) — те же 12 точек от угла, что на макете.
	private const double _margin = -8;

	private readonly Glow _glow;

	private bool _open;

	public ChipWindow()
	{
		InitializeComponent();
		_glow = new Glow(Halo, Wash, Glint, () => Lights.Bounds.Width);
		Wire(Rows);
		Opens(Face);
	}

	// Закрытая панель прозрачна, но место в окне занимает: мышь над ним — не над значком.
	protected override Visual Hit => _open ? Body : Face;

	public override void Tick()
	{
		base.Tick();
		ShowAgents();
	}

	protected override void Rendered() => ShowAgents();

	protected override void Place(PixelRect anchor) => Placement.PlaceChip(this, anchor, _margin);

	protected override void Look(WidgetLook look)
	{
		_open = look == WidgetLook.Panel;
		Drawer.Opacity = _open ? 1 : 0;
		Drawer.IsHitTestVisible = _open;
		Drawer.RenderTransform = TransformOperations.Parse(_open ? "translateY(0px)" : "translateY(8px)");
	}

	/// <summary>Все агенты в ряд или один — выбранный или по очереди; «ждёт» и «закончил» — всегда надпись.</summary>
	private void ShowAgents()
	{
		if (DataContext is not WidgetView view)
		{
			return;
		}

		var choice = Store.Current.ChipAgent;
		var shown = view.Pick(choice, DateTime.UtcNow);
		Row.IsVisible = view.ShowsAgents && choice == WidgetView.All;
		Single.IsVisible = !Row.IsVisible;
		ShowFace(Single, shown);
		_glow.Show(Store.Current.Glow, shown);

		// Работает агент — от его кольца (в ряду — от кольца каждого работающего) расходится рябь; без кольца — от точки.
		Face.Classes.Set("moving", Store.Current.WorkMotion);
		Ripple.Classes.Set("on", shown.Busy && Single.IsVisible && shown.HasValue);
		DotRipple.Classes.Set("on", shown.Busy && Single.IsVisible && !shown.HasValue);
	}
}
