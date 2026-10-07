using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace AgentVitals;

/// <summary>Уведомление у значка: своё окно вместо системного тоста, уходит само через шесть секунд.</summary>
internal partial class Toast : TrayWindow
{
	private readonly DispatcherTimer _life = new()
	{
		Interval = TimeSpan.FromSeconds(6),
	};

	public Toast()
	{
		InitializeComponent();

		_life.Tick += (_, _) =>
		{
			_life.Stop();
			Hide();
		};
	}

	public event Action? Clicked;

	/// <summary>Показать уведомление у значка.</summary>
	/// <param name="message">Текст.</param>
	/// <param name="anchor">Где значок.</param>
	/// <param name="title">Заголовок; без него — имя программы.</param>
	/// <param name="accent">Цвет заголовка: насколько серьёзно; без него — обычный текст.</param>
	public void Say(string message, PixelRect anchor, string? title = null, IBrush? accent = null)
	{
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
		_life.Stop();
		_life.Start();
	}

	private void OnClick(object? sender, PointerReleasedEventArgs e)
	{
		_life.Stop();
		Hide();
		Clicked?.Invoke();
	}
}
