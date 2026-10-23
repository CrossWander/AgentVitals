using System;
using Avalonia.Controls;
using Avalonia.Input;
using AgentVitals.Core.Sessions;

namespace AgentVitals;

/// <summary>Строки агентов и сессий виджета: щелчок по агенту — его вкладка в поповере, по сессии — её окно.</summary>
internal partial class WidgetPanel : UserControl
{
	public WidgetPanel() => InitializeComponent();

	public event Action<string>? AgentChosen;

	public event Action<Session>? SessionChosen;

	private void OnAgent(object? sender, PointerReleasedEventArgs e)
	{
		if (e.InitialPressMouseButton == MouseButton.Left && sender is Control { DataContext: WidgetAgent row })
		{
			AgentChosen?.Invoke(row.Name);
		}
	}

	private void OnSession(object? sender, PointerReleasedEventArgs e)
	{
		if (e.InitialPressMouseButton == MouseButton.Left && sender is Control { DataContext: WidgetSession row })
		{
			SessionChosen?.Invoke(row.Session);
		}
	}
}
