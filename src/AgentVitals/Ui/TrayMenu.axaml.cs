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

	public void Open(PixelRect anchor)
	{
		ShowAt(anchor);
		Activate();
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
