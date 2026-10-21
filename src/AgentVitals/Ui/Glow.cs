using System;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace AgentVitals;

/// <summary>Свечение плашки и значка на события: ждёт — синий пульс, закончил — зелёная вспышка, мало квоты — медленный
/// красный пульс. Ореол — светится тень вокруг формы, блик — по форме пробегает отблеск, цветом — форма подкрашивается.</summary>
/// <param name="halo">Подложка под формой того же размера: светится её тень.</param>
/// <param name="wash">Заливка внутри формы.</param>
/// <param name="glint">Отблеск внутри формы за левым краем: ключевыми кадрами сдвигается его TranslateTransform — сам
/// RenderTransform ими не анимируется.</param>
/// <param name="path">Докуда пробегает отблеск — ширина формы.</param>
internal sealed class Glow(Border halo, Border wash, Border glint, Func<double> path)
{
	private (GlowStyle Style, WidgetGlow Glow) _shown;
	private CancellationTokenSource? _running;

	/// <summary>Тот же стиль и то же событие — анимация идёт дальше, а не начинается заново с каждым тиком.</summary>
	public void Show(GlowStyle style, WidgetGlow glow)
	{
		if ((style, glow) == _shown)
		{
			return;
		}

		_shown = (style, glow);
		_running?.Cancel();
		_running = null;
		glint.Opacity = 0;

		if (style == GlowStyle.Off || glow == WidgetGlow.None)
		{
			return;
		}

		var color = glow switch
		{
			WidgetGlow.Pulse => Color.Parse("#60A5FA"),
			WidgetGlow.Flash => Color.Parse("#34D399"),
			_ => Color.Parse("#F87171"),
		};

		// Пульс — 2,4 с по кругу, медленный — 4 с и тусклее, вспышка — один раз.
		var (period, peak, times) = glow switch
		{
			WidgetGlow.Pulse => (2.4, 1.0, IterationCount.Infinite),
			WidgetGlow.Slow => (4.0, 0.7, IterationCount.Infinite),
			_ => (1.2, 1.0, new IterationCount(1)),
		};

		var low = glow == WidgetGlow.Flash ? 0 : peak / 4;
		_running = new CancellationTokenSource();

		switch (style)
		{
			case GlowStyle.Halo:
				halo.BoxShadow = new BoxShadows(new BoxShadow { Blur = 14, Spread = 1, Color = color });
				Run(halo, Visual.OpacityProperty, period, times, low, peak, low);

				break;

			case GlowStyle.Wash:
				wash.Background = new SolidColorBrush(color);
				Run(wash, Visual.OpacityProperty, period, times, low * 0.3, peak * 0.3, low * 0.3);

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
				Run(glint, TranslateTransform.XProperty, period, times, -width, path(), path(), 0.35);

				break;
		}
	}

	private void Run(Animatable target, AvaloniaProperty property, double period, IterationCount times, object from, object peak, object to, double at = 0.5) =>
		_ = new Animation
		{
			Duration = TimeSpan.FromSeconds(period),
			IterationCount = times,
			Children =
			{
				new KeyFrame { Cue = new Cue(0), Setters = { new Setter(property, from) } },
				new KeyFrame { Cue = new Cue(at), Setters = { new Setter(property, peak) } },
				new KeyFrame { Cue = new Cue(1), Setters = { new Setter(property, to) } },
			},
		}.RunAsync(target, _running!.Token);
}
