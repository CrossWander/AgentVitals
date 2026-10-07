using System;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgentVitals;

/// <summary>Настройки внутри поповера: применяются сразу и сохраняются в config.json.</summary>
internal partial class SettingsPanel : UserControl
{
	// Пока переключатели выставляются по сохранённому, их события — не выбор человека.
	private bool _loading;

	public SettingsPanel() => InitializeComponent();

	public event Action? Back;

	public event Action? Changed;

	public event Action? ExitRequested;

	public void Load()
	{
		_loading = true;

		LanguageEn.IsChecked = Localization.Current == "en";
		LanguageRu.IsChecked = Localization.Current == "ru";
		LanguageUk.IsChecked = Localization.Current == "uk";
		ThemeAuto.IsChecked = Store.Current.Theme == ThemeMode.Auto;
		ThemeLight.IsChecked = Store.Current.Theme == ThemeMode.Light;
		ThemeDark.IsChecked = Store.Current.Theme == ThemeMode.Dark;
		Interval30s.IsChecked = Store.Current.IntervalSeconds == 30;
		Interval2.IsChecked = Store.Current.IntervalSeconds == 120;
		Interval5.IsChecked = Store.Current.IntervalSeconds == 300;
		Interval10.IsChecked = Store.Current.IntervalSeconds == 600;
		Interval1.IsChecked = Store.Current.IntervalSeconds is not (30 or 120 or 300 or 600);
		AutostartCard.IsVisible = OperatingSystem.IsWindows();

		if (OperatingSystem.IsWindows())
		{
			AutostartToggle.IsChecked = Autostart.IsEnabled();
		}

		ShowHooks(null);
		_loading = false;
	}

	private void OnLanguage(object? sender, RoutedEventArgs e)
	{
		if (!_loading && sender is RadioButton { IsChecked: true, Tag: string code })
		{
			Localization.Apply(code);
			ShowHooks(null);
			Changed?.Invoke();
		}
	}

	private void OnTheme(object? sender, RoutedEventArgs e)
	{
		if (!_loading && sender is RadioButton { IsChecked: true, Tag: string name })
		{
			// Внутри контрола Theme — его свойство ControlTheme, поэтому имя класса полностью.
			AgentVitals.Theme.Apply(Enum.Parse<ThemeMode>(name));
			Changed?.Invoke();
		}
	}

	private void OnInterval(object? sender, RoutedEventArgs e)
	{
		if (!_loading && sender is RadioButton { IsChecked: true, Tag: string seconds })
		{
			Store.Current.IntervalSeconds = int.Parse(seconds, CultureInfo.InvariantCulture);
			Store.Save();
		}
	}

	private void OnAutostart(object? sender, RoutedEventArgs e)
	{
		if (_loading || !OperatingSystem.IsWindows())
		{
			return;
		}

		try
		{
			Autostart.Set(AutostartToggle.IsChecked == true);
		}
		catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or SecurityException)
		{
			Journal.Add($"autostart: {exception.Message}");
			_loading = true;
			AutostartToggle.IsChecked = Autostart.IsEnabled();
			_loading = false;
		}
	}

	private void OnConnect(object? sender, RoutedEventArgs e) =>
		ChangeHooks(() => HookInstaller.Install(HookInstaller.SettingsPath, Environment.ProcessPath!), "hooks connected");

	private void OnDisconnect(object? sender, RoutedEventArgs e) =>
		ChangeHooks(() => HookInstaller.Uninstall(HookInstaller.SettingsPath), "hooks disconnected");

	private void OnBack(object? sender, RoutedEventArgs e) => Back?.Invoke();

	private void OnExit(object? sender, RoutedEventArgs e) => ExitRequested?.Invoke();

	private void ChangeHooks(Action change, string journal)
	{
		try
		{
			change();
			Journal.Add(journal);
			ShowHooks(null);
			Changed?.Invoke();
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
		{
			ShowHooks(exception.Message);
		}
	}

	private void ShowHooks(string? error)
	{
		var state = Views.Hooks(out var readError);
		error ??= readError;

		HooksState.Text = error is not null
			? Localization.Format("langHooksError", error)
			: Localization.Get(state switch
			{
				HookLink.Off => "langHooksOff",
				HookLink.On => "langHooksOn",
				HookLink.Moved => "langHooksMoved",
				_ => throw new NotImplementedException("HookLink switch error"),
			});

		HooksConnect.IsVisible = state != HookLink.On;
		HooksDisconnect.IsVisible = state != HookLink.Off;
	}
}
