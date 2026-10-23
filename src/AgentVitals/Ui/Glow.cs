using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using AgentVitals.Core.Sessions;

namespace AgentVitals;

/// <summary>Свечение плашки и значка на события: ждёт — синий пульс, закончил — зелёная вспышка, мало квоты — медленный
/// красный пульс. Ореол — светится тень вокруг формы, блик — по форме пробегает отблеск, цветом — форма подкрашивается.</summary>
/// <param name="halo">Подложка под формой того же размера: светится её тень.</param>
/// <param name="wash">Заливка внутри формы.</param>
/// <param name="glint">Отблеск внутри формы за левым краем: ключевыми кадрами сдвигается его TranslateTransform — сам
/// RenderTransform ими не анимируется.</param>
/// <param name="path">Докуда пробегает отблеск — ширина, к которой идёт форма.</param>
internal sealed class Glow(Border halo, Border wash, Border glint, Func<double> path)
{
	private (GlowStyle Style, WidgetState State, Session? Session, double Path) _shown;
	private CancellationTokenSource? _running;

	/// <summary>Тот же стиль и то же событие — анимация идёт дальше, а не начинается заново с каждым тиком. Ждёт или
	/// закончила другая сессия — событие новое; у блика форма выросла — путь новый.</summary>
	public void Show(GlowStyle style, WidgetView view)
	{
		var shown = (Style: style, view.State, Session: view.Waiting ?? view.Finished, Path: style == GlowStyle.Sheen ? Math.Round(path()) : 0);

		if (shown == _shown)
		{
			return;
		}

		_shown = shown;
		_running?.Cancel();
		_running = null;
		halo.Opacity = wash.Opacity = glint.Opacity = 0;

		if (style == GlowStyle.Off || view.State is not (WidgetState.Waiting or WidgetState.Finished or WidgetState.Low))
		{
			return;
		}

		// Пульс — 2,4 с по кругу, медленный — 4 с и тусклее, вспышка — один раз.
		var (color, period, peak, loop) = view.State switch
		{
			WidgetState.Waiting => (Color.Parse("#60A5FA"), 2.4, 1.0, true),
			WidgetState.Low => (Color.Parse("#F87171"), 4.0, 0.7, true),
			_ => (Color.Parse("#34D399"), 1.2, 1.0, false),
		};

		var low = loop ? peak / 4 : 0;
		_running = new CancellationTokenSource();

		// Между кругами свойство на миг возвращается к своему значению — оно нижнее, а не 0, и шва не видно.
		switch (style)
		{
			case GlowStyle.Halo:
				halo.BoxShadow = new BoxShadows(new BoxShadow { Blur = 14, Spread = 1, Color = color });
				halo.Opacity = low;
				_ = Run(halo, Visual.OpacityProperty, period, loop, low, peak, low);

				break;

			case GlowStyle.Wash:
				wash.Background = new SolidColorBrush(color);
				wash.Opacity = low * 0.3;
				_ = Run(wash, Visual.OpacityProperty, period, loop, low * 0.3, peak * 0.3, low * 0.3);

				break;

			case GlowStyle.Sheen:
				// Отблеск пробегает за первую треть круга, остальное время форма спокойна.
				var width = glint.Width;
				glint.Background = new LinearGradientBrush
				{
					StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
					EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
					GradientStops =
					{
						new GradientStop(Colors.Transparent, 0),
						new GradientStop(Color.FromArgb((byte)(peak * 160), color.R, color.G, color.B), 0.5),
						new GradientStop(Colors.Transparent, 1),
					},
				};
				glint.Opacity = 1;
				_ = Run(glint, TranslateTransform.XProperty, period, loop, -width, shown.Path, shown.Path, 0.35);

				break;
		}
	}

	// Бесконечную анимацию RunAsync не принимает: по кругу — однократными прогонами, пока событие то же. Спрятанное окно
	// её не крутит.
	private async Task Run(Animatable target, AvaloniaProperty property, double period, bool loop, object from, object peak, object to, double at = 0.5)
	{
		var token = _running!.Token;
		var animation = new Animation
		{
			Duration = TimeSpan.FromSeconds(period),
			PlaybackBehavior = PlaybackBehavior.OnlyIfVisible,
			Children =
			{
				new KeyFrame { Cue = new Cue(0), Setters = { new Setter(property, from) } },
				new KeyFrame { Cue = new Cue(at), Setters = { new Setter(property, peak) } },
				new KeyFrame { Cue = new Cue(1), Setters = { new Setter(property, to) } },
			},
		};

		do
		{
			await animation.RunAsync(target, token);
		}
		while (loop && !token.IsCancellationRequested);
	}
}
