using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgentVitals;

/// <summary>Меню по правому щелчку на значке — своё окно вместо системного меню.</summary>
internal partial class TrayMenu : TrayWindow
{
	public TrayMenu()
	{
		InitializeComponent();
		Deactivated += (_, _) => Hide();
	}

	public event Action? RefreshRequested;

	public event Action? SettingsRequested;

	public event Action? AgentsRequested;

	public event Action? ExitRequested;

	/// <summary>«Не беспокоить»: на сколько включили; null — выключили.</summary>
	public event Action<QuietSpan?>? QuietChosen;

	/// <param name="quiet">Конец «Не беспокоить» из настроек; null — не включали.</param>
	public void Open(PixelRect anchor, DateTime? quiet)
	{
		var now = DateTime.UtcNow;
		var on = Quiet.On(quiet, now);
		QuietChoices.IsVisible = !on;
		QuietActive.IsVisible = on;
		QuietUntil.Text = on ? Text.QuietMenu(quiet!.Value, now) : "";

		ShowAt(anchor);
		Activate();
	}

	private void OnQuiet(object? sender, RoutedEventArgs e)
	{
		Hide();
		QuietChosen?.Invoke(sender is Button { Tag: string span } ? Enum.Parse<QuietSpan>(span) : null);
	}

	private void OnRefresh(object? sender, RoutedEventArgs e)
	{
		Hide();
		RefreshRequested?.Invoke();
	}

	private void OnAgents(object? sender, RoutedEventArgs e)
	{
		Hide();
		AgentsRequested?.Invoke();
	}

	private void OnSettings(object? sender, RoutedEventArgs e)
	{
		Hide();
		SettingsRequested?.Invoke();
	}

	private void OnExit(object? sender, RoutedEventArgs e)
	{
		Hide();
		ExitRequested?.Invoke();
	}
}
