using System;
using Avalonia;
using Avalonia.Media.Transformation;
using Avalonia.Threading;

namespace AgentVitals;

/// <summary>Значок над треем: в углу у трея, в полоску не сжимается; щелчком над ним выезжает панель. Агенты — все сразу
/// или по очереди, как в настройках.</summary>
internal partial class ChipWindow : WidgetWindow
{
	// С полем под тень внутри окна — те же 12 точек от угла, что на макете.
	private const double _margin = 4;

	private static readonly TimeSpan _fade = TimeSpan.FromMilliseconds(180);

	private bool _open;

	// Чей агент сейчас на значке «по очереди»; null — значок не по очереди.
	private string? _turn;

	// Прежний агент гаснет: новый встанет, когда погаснет.
	private bool _fading;

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

	/// <summary>Надпись, агенты в ряд или один по очереди: «ждёт» и «закончил» — всегда надпись.</summary>
	private void ShowAgents()
	{
		if (DataContext is not WidgetView view)
		{
			return;
		}

		var turns = view.ShowsAgents && Store.Current.ChipTurns;
		var agent = turns ? view.Turn(DateTime.UtcNow) : null;

		Row.IsVisible = view.ShowsAgents && !turns;
		Turn.IsVisible = agent is not null;
		Label.IsVisible = !Row.IsVisible && agent is null;

		if (agent is null || _turn is null || agent.Name == _turn)
		{
			// Тот же агент — только свежие цифры; первый — сразу, без смены.
			_turn = agent?.Name;

			if (!_fading)
			{
				Turn.Content = agent;
			}

			return;
		}

		_turn = agent.Name;
		_fading = true;
		Turn.Opacity = 0;

		DispatcherTimer.RunOnce(() =>
		{
			_fading = false;
			Turn.Content = (DataContext as WidgetView)?.Turn(DateTime.UtcNow);
			Turn.Opacity = 1;
		}, _fade);
	}
}
