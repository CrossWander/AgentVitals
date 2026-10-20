using System;
using Avalonia;
using Avalonia.Threading;

namespace AgentVitals;

/// <summary>Плашка вверху по центру экрана с треем: полоска, под мышью — плашка, щелчком — панель. Переходы плавные:
/// форма меняет размер и скругление, содержимое проявляется.</summary>
internal partial class NotchWindow : WidgetWindow
{
	private const double _stripWidth = 120;
	private const double _stripHeight = 4;
	private const double _headerHeight = 30;

	// Поля плашки по бокам: содержимое не прижимается к скруглению.
	private const double _sides = 28;

	private WidgetLook _shown = WidgetLook.Rest;

	public NotchWindow()
	{
		InitializeComponent();
		Wire(Rows);
		Opens(Top);
		DataContextChanged += (_, _) => ShowAgent();

		// Другие строки панели или другая надпись в шапке: размеры формы пересчитываются, когда привязки уже применены.
		// Надпись меняется и после затухания — не по тику, а когда сменилась.
		DataContextChanged += (_, _) => Dispatcher.UIThread.Post(Fit, DispatcherPriority.Loaded);
		Top.DataContextChanged += (_, _) => Dispatcher.UIThread.Post(Fit, DispatcherPriority.Loaded);
	}

	protected override Visual Hit => Shape;

	public override void Tick()
	{
		base.Tick();
		ShowAgent();
	}

	/// <summary>Шапка — главный агент или по очереди, как в настройках. Полоска «по очереди» — цвет ближе всех к лимиту:
	/// со сменой агента она не мигает.</summary>
	private void ShowAgent()
	{
		if (DataContext is not WidgetView view)
		{
			return;
		}

		var turns = Store.Current.NotchTurns;
		Strip.Background = turns ? view.LimitMeter ?? view.Meter : view.Meter;
		ShowFace(Top, turns ? view.Turned(DateTime.UtcNow) : view);
	}

	protected override void Place(PixelRect anchor) => Placement.PlaceNotch(this, anchor);

	protected override void Look(WidgetLook look)
	{
		_shown = look;
		Strip.Opacity = look == WidgetLook.Rest ? 1 : 0;
		Body.Opacity = look == WidgetLook.Rest ? 0 : 1;
		Rows.Opacity = look == WidgetLook.Panel ? 1 : 0;
		Fit();
	}

	/// <summary>Размеры — числами: «Auto» не анимируется. Окно — с панель, форма — по виду; переход ведут Transitions.</summary>
	private void Fit()
	{
		Header.Measure(Size.Infinity);
		Rows.Measure(Size.Infinity);

		var peek = new Size(Math.Max(_stripWidth, Header.DesiredSize.Width + _sides), _headerHeight);
		var panel = new Size(Math.Max(peek.Width, Rows.DesiredSize.Width + _sides), _headerHeight + Rows.DesiredSize.Height);

		// Содержимое — в полную ширину панели: пока форма растёт, текст не переносится и не обрезается троеточием.
		Body.Width = panel.Width;
		Frame.Width = panel.Width;
		Frame.Height = panel.Height;

		var size = _shown switch
		{
			WidgetLook.Rest => new Size(_stripWidth, _stripHeight),
			WidgetLook.Peek => peek,
			WidgetLook.Panel => panel,
		};

		Shape.Width = size.Width;
		Shape.Height = size.Height;
		Shape.CornerRadius = _shown == WidgetLook.Rest ? new CornerRadius(0, 0, 2, 2) : new CornerRadius(0, 0, 14, 14);
	}
}
