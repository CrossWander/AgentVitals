using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace AgentVitals;

/// <summary>Уведомление в углу над часами, как карточка AudioDirigent: выезжает снизу, живёт несколько секунд,
/// под мышью не уходит. Своё окно вместо системного тоста.</summary>
internal partial class Toast : TrayWindow
{
	/// <summary>Сколько живёт обычный тост.</summary>
	public static readonly TimeSpan Brief = TimeSpan.FromSeconds(4);

	/// <summary>Сколько живёт тост о квоте: его нельзя пропустить, а за четыре секунды взгляд до угла не доходит.</summary>
	public static readonly TimeSpan Long = TimeSpan.FromSeconds(10);

	// Отступ окна от угла рабочей области; ещё полтора-два десятка точек внутри окна занимает поле под тень.
	private const double _margin = 6;

	private static readonly TimeSpan _rise = TimeSpan.FromMilliseconds(380);
	private static readonly TimeSpan _fall = TimeSpan.FromMilliseconds(220);

	private readonly DispatcherTimer _life = new();

	// Анимация в Avalonia 12 водит только свойства элементов, а не самого сдвига: окно водит своё, а сдвиг идёт следом.
	private static readonly StyledProperty<double> ShiftProperty = AvaloniaProperty.Register<Toast, double>("Shift");

	private readonly TranslateTransform _slide = new();

	// Новое движение отменяет прежнее: иначе конец ухода спрятал бы тост, пришедший ему на смену.
	private CancellationTokenSource _motion = new();

	public Toast()
	{
		InitializeComponent();

		Card.RenderTransform = _slide;

		_life.Tick += (_, _) => Dismiss();

		// Пока мышь на тосте, он не уходит: человек дочитывает или ведёт курсор, чтобы щёлкнуть.
		Card.PointerEntered += (_, _) => _life.Stop();
		Card.PointerExited += (_, _) => _life.Start();
	}

	public event Action? Clicked;

	/// <summary>Показать уведомление. Повторный вызов заменяет текст и продлевает показ.</summary>
	/// <param name="message">Текст.</param>
	/// <param name="anchor">Где значок: тост встаёт в угол того же экрана.</param>
	/// <param name="title">Заголовок; без него — имя программы.</param>
	/// <param name="accent">Цвет заголовка: насколько серьёзно; без него — обычный текст.</param>
	/// <param name="life">Сколько показывать; без него — <see cref="Brief"/>.</param>
	public void Say(string message, PixelRect anchor, string? title = null, IBrush? accent = null, TimeSpan? life = null)
	{
		_life.Interval = life ?? Brief;
		Heading.Text = title ?? "AgentVitals";

		if (accent is null)
		{
			Heading.ClearValue(TextBlock.ForegroundProperty);
		}
		else
		{
			Heading.Foreground = accent;
		}

		Message.Text = message;
		ShowAt(anchor);
		Move(1, _rise, new CubicEaseOut(), then: null);
		_life.Stop();
		_life.Start();
	}

	protected override void Place(PixelRect anchor) => Placement.PlaceInCorner(this, anchor, _margin);

	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);

		if (change.Property == ShiftProperty)
		{
			_slide.Y = change.GetNewValue<double>();
		}
	}

	private void Dismiss()
	{
		_life.Stop();
		Move(0, _fall, new CubicEaseIn(), then: Hide);
	}

	/// <summary>Въехать снизу и проявиться (1) или уехать вниз и погаснуть (0).</summary>
	private async void Move(double shown, TimeSpan duration, Easing easing, Action? then)
	{
		_motion.Cancel();
		_motion = new CancellationTokenSource();
		var motion = _motion.Token;

		// Ниже собственного окна тост обрезан его краем — так он и выезжает из-за нижней кромки.
		var below = ClientSize.Height;

		await Task.WhenAll(
			Tween(OpacityProperty, 1 - shown, shown, duration, easing).RunAsync(Card, motion),
			Tween(ShiftProperty, shown > 0 ? below : 0, shown > 0 ? 0 : below, duration, easing).RunAsync(this, motion));

		if (!motion.IsCancellationRequested)
		{
			then?.Invoke();
		}
	}

	private static Animation Tween(AvaloniaProperty property, double from, double to, TimeSpan duration, Easing easing) => new()
	{
		Duration = duration,
		Easing = easing,
		FillMode = FillMode.Forward,
		Children =
		{
			new KeyFrame { Cue = new Cue(0), Setters = { new Setter(property, from) } },
			new KeyFrame { Cue = new Cue(1), Setters = { new Setter(property, to) } },
		},
	};

	private void OnClick(object? sender, PointerReleasedEventArgs e)
	{
		_life.Stop();
		_motion.Cancel();
		Hide();
		Clicked?.Invoke();
	}
}
