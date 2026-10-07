using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace AgentVitals;

/// <summary>Уведомление у значка: своё окно вместо системного тоста, уходит само через шесть секунд.</summary>
internal partial class Toast : Window
{
	private readonly DispatcherTimer _life = new()
	{
		Interval = TimeSpan.FromSeconds(6),
	};

	private PixelRect _anchor;

	public Toast()
	{
		InitializeComponent();

		_life.Tick += (_, _) =>
		{
			_life.Stop();
			Hide();
		};

		SizeChanged += (_, _) => Placement.Place(this, _anchor);
	}

	public event Action? Clicked;

	public void Say(string message, PixelRect anchor)
	{
		Message.Text = message;
		_anchor = anchor;
		Show();
		Placement.Place(this, anchor);
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
