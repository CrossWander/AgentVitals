using System;
using Avalonia;
using Avalonia.Threading;

namespace AgentVitals;

/// <summary>Плашка вверху по центру экрана с треем: полоска, под мышью — плашка, щелчком — панель. Переходы плавные:
/// форма меняет размер и скругление, содержимое проявляется.</summary>
internal partial class NotchWindow : WidgetWindow
{
	private const double _stripWidth = 120;
	private const double _stripHeight = 6;
	private const double _headerHeight = 30;

	private static readonly CornerRadius _stripCorners = new(0, 0, _stripHeight / 2, _stripHeight / 2);

	// Поля плашки по бокам: содержимое не прижимается к скруглению.
	private const double _sides = 28;

	// Поле вокруг формы под ореол: тень за краем окна обрезалась бы.
	private const double _halo = 20;

	private readonly Glow _glow;

	private WidgetLook _shown = WidgetLook.Rest;

	// Ширина, к которой идёт форма: Width под Transitions ещё в пути, а блику нужен весь путь.
	private double _width = _stripWidth;

	public NotchWindow()
	{
		InitializeComponent();

		// Полоска — одними константами: форма в покое — она сама, рябь — её копия.
		Ripple.Width = Shape.Width = _stripWidth;
		Strip.Height = Ripple.Height = Shape.Height = _stripHeight;
		Ripple.CornerRadius = Shape.CornerRadius = _stripCorners;

		_glow = new Glow(Halo, Wash, Glint, () => _width);
		Wire(Rows);
		Opens(Top);

		// Другие строки панели или другая надпись в шапке: размеры формы пересчитываются, когда привязки уже применены.
		// Надпись меняется и после затухания — не по тику, а когда сменилась.
		DataContextChanged += (_, _) => Dispatcher.UIThread.Post(Fit, DispatcherPriority.Loaded);
		Top.DataContextChanged += (_, _) => Dispatcher.UIThread.Post(Fit, DispatcherPriority.Loaded);
	}

	protected override Visual Hit => Shape;

	protected override void Rendered() => ShowAgent();

	/// <summary>Шапка — агент из настроек или по очереди. Полоска «по очереди» — цвет ближе всех к лимиту: со сменой агента
	/// она не мигает.</summary>
	private void ShowAgent()
	{
		if (DataContext is not WidgetView view)
		{
			return;
		}

		var choice = Store.Current.NotchAgent;
		var shown = view.Pick(choice, DateTime.UtcNow);
		Strip.Background = choice == WidgetView.Rotation ? view.LimitMeter ?? shown.Meter : shown.Meter;
		ShowFace(Top, shown);
		_glow.Show(Store.Current.Glow, shown);

		// Работает показанный агент: в покое от полоски расходится рябь, у выехавшей плашки — маяк у точки.
		Frame.Classes.Set("moving", Store.Current.WorkMotion);
		Ripple.Background = Strip.Background;
		Ripple.Classes.Set("on", shown.Busy && _shown == WidgetLook.Rest);
		Beacon.Classes.Set("on", shown.Busy && _shown != WidgetLook.Rest);
	}

	protected override void Place(PixelRect anchor) => Placement.PlaceNotch(this, anchor);

	protected override void Look(WidgetLook look)
	{
		_shown = look;
		Strip.Opacity = look == WidgetLook.Rest ? 1 : 0;
		Body.Opacity = look == WidgetLook.Rest ? 0 : 1;
		Rows.Opacity = look == WidgetLook.Panel ? 1 : 0;
		Fit();

		// Рябь у полоски и маяк у точки меняются местами с видом сразу, а не со следующим тиком.
		ShowAgent();
	}

	/// <summary>Размеры — числами: «Auto» не анимируется. Окно — с панель, форма — по виду; переход ведут Transitions.</summary>
	private void Fit()
	{
		Header.Measure(Size.Infinity);
		Rows.Measure(Size.Infinity);

		var pill = new Size(Math.Max(_stripWidth, Header.DesiredSize.Width + _sides), _headerHeight);
		var panel = new Size(Math.Max(pill.Width, Rows.DesiredSize.Width + _sides), _headerHeight + Rows.DesiredSize.Height);

		// Содержимое — в полную ширину панели: пока форма растёт, текст не переносится и не обрезается троеточием.
		Body.Width = panel.Width;
		Frame.Width = panel.Width + 2 * _halo;
		Frame.Height = panel.Height + _halo;

		var size = _shown switch
		{
			WidgetLook.Rest => new Size(_stripWidth, _stripHeight),
			WidgetLook.Pill => pill,
			WidgetLook.Panel => panel,
		};

		_width = Shape.Width = size.Width;
		Shape.Height = size.Height;
		Shape.CornerRadius = _shown == WidgetLook.Rest ? _stripCorners : new CornerRadius(0, 0, 14, 14);
	}
}
