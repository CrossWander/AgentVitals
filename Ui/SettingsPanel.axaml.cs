using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
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

	// Последний ответ GitHub — от кнопки или автопроверки; null — ещё не спрашивали.
	private Release? _release;

	public SettingsPanel()
	{
		InitializeComponent();
		Credits.Text = $"Developed by Dykalo Pavlo, 2026 · {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}";
	}

	public event Action? Back;

	public event Action? Changed;

	public event Action? ExitRequested;

	public void Load()
	{
		_loading = true;

		Select(Languages, Localization.Current);
		Select(Themes, Store.Current.Theme.ToString());
		Select(Intervals, Store.Current.IntervalSeconds.ToString(CultureInfo.InvariantCulture));
		Select(TurnMinutes, Store.Current.TurnMinutes.ToString(CultureInfo.InvariantCulture));
		ShowLevels();
		SessionToastsToggle.IsChecked = Store.Current.SessionToasts;
		TurnPanel.IsEnabled = Store.Current.SessionToasts;
		AutostartCard.IsVisible = OperatingSystem.IsWindows();

		if (OperatingSystem.IsWindows())
		{
			AutostartToggle.IsChecked = Autostart.IsEnabled();
		}

		AutoUpdateToggle.IsChecked = Store.Current.AutoUpdate;
		ShowRelease();
		ShowHooks(null);
		_loading = false;
	}

	/// <summary>Ответ автопроверки: открыв настройки из тоста, человек видит кнопку «Обновить».</summary>
	public void Show(Release release)
	{
		_release = release;
		ShowRelease();
	}

	// Значения живут только в Tag кнопок: новый язык или интервал — правка XAML и строк, не кода.
	// Сохранённого среди кнопок нет (руками поправили config.json) — отмечается первая, она же умолчание.
	private static void Select(Panel group, string value)
	{
		var buttons = group.Children.OfType<RadioButton>().ToList();
		(buttons.FirstOrDefault(button => (string?)button.Tag == value) ?? buttons[0]).IsChecked = true;
	}

	private void OnLanguage(object? sender, RoutedEventArgs e)
	{
		if (!_loading && sender is RadioButton { IsChecked: true, Tag: string code })
		{
			Localization.Apply(code);
			Store.Current.Language = Localization.Current;
			Store.Save();
			ShowHooks(null);
			ShowRelease();
			Changed?.Invoke();
		}
	}

	private void OnTheme(object? sender, RoutedEventArgs e)
	{
		if (!_loading && sender is RadioButton { IsChecked: true, Tag: string name })
		{
			var mode = Enum.Parse<ThemeMode>(name);

			// Внутри контрола Theme — его свойство ControlTheme, поэтому имя класса полностью.
			AgentVitals.Theme.Apply(mode);
			Store.Current.Theme = mode;
			Store.Save();
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

	private void OnLevel(object? sender, RoutedEventArgs e)
	{
		if (sender is Button { Tag: string tag } && tag.Split(':') is [var which, var step])
		{
			Store.Current.Thresholds = QuotaAlarm.Shift(Store.Current.Thresholds, which == "critical", int.Parse(step, CultureInfo.InvariantCulture));
			Store.Save();
			ShowLevels();

			// По порогам и цвета: значок и карточки перекрашиваются сразу, а не со следующим опросом.
			Changed?.Invoke();
		}
	}

	private void ShowLevels()
	{
		ImportantValue.Text = $"{Store.Current.ImportantAt}%";
		CriticalValue.Text = $"{Store.Current.CriticalAt}%";
	}

	private void OnSessionToasts(object? sender, RoutedEventArgs e)
	{
		if (!_loading)
		{
			Store.Current.SessionToasts = SessionToastsToggle.IsChecked == true;
			TurnPanel.IsEnabled = Store.Current.SessionToasts;
			Store.Save();
		}
	}

	private void OnTurnMinutes(object? sender, RoutedEventArgs e)
	{
		if (!_loading && sender is RadioButton { IsChecked: true, Tag: string minutes })
		{
			Store.Current.TurnMinutes = int.Parse(minutes, CultureInfo.InvariantCulture);
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

	private void OnAutoUpdate(object? sender, RoutedEventArgs e)
	{
		if (!_loading)
		{
			Store.Current.AutoUpdate = AutoUpdateToggle.IsChecked == true;
			Store.Save();
		}
	}

	private async void OnCheckUpdates(object? sender, RoutedEventArgs e)
	{
		UpdateCheck.IsEnabled = false;
		UpdateInstall.IsVisible = UpdateOpen.IsVisible = false;
		UpdateLine.Text = Localization.Get("langUpdateChecking");

		_release = await Updates.CheckAsync();
		UpdateCheck.IsEnabled = true;
		ShowRelease();
	}

	private async void OnInstallUpdate(object? sender, RoutedEventArgs e)
	{
		if (_release is not { } release)
		{
			return;
		}

		UpdateCheck.IsEnabled = UpdateInstall.IsEnabled = false;
		UpdateLine.Text = Localization.Get("langUpdateDownloading");

		// При удаче сюда не вернёмся: программа закрывается, сценарий поднимет новую.
		var error = await Updates.InstallAsync(release, () => ExitRequested?.Invoke());

		UpdateCheck.IsEnabled = UpdateInstall.IsEnabled = true;
		ShowRelease(error);
	}

	private async void OnOpenReleases(object? sender, RoutedEventArgs e)
	{
		if (TopLevel.GetTopLevel(this) is { } top)
		{
			await top.Launcher.LaunchUriAsync(new Uri(Updates.Releases));
		}
	}

	private void ShowRelease(string? error = null)
	{
		UpdateLine.Text = error ?? (_release is null ? Localization.Format("langVersion", Updates.Current) : _release.State switch
		{
			UpdateState.Latest => Localization.Format("langUpdateLatest", Updates.Current),
			UpdateState.Newer => Localization.Format("langUpdateAvailable", _release.Version),
			UpdateState.Failed => Localization.Get("langUpdateFailed"),
		});

		// Подменяет exe сценарий cmd — только на Windows; на других системах новую версию берут с GitHub.
		var newer = error is null && _release?.State == UpdateState.Newer;
		UpdateInstall.IsVisible = newer && OperatingSystem.IsWindows();
		UpdateOpen.IsVisible = error is not null || (newer && !OperatingSystem.IsWindows());
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
		var state = HookInstaller.Link(out var readError);
		error ??= readError;

		HooksState.Text = error is not null
			? Localization.Format("langHooksError", error)
			: Localization.Get(state switch
			{
				HookLink.Off => "langHooksOff",
				HookLink.On => "langHooksOn",
				HookLink.Moved => "langHooksMoved",
			});

		HooksConnect.IsVisible = state != HookLink.On;
		HooksDisconnect.IsVisible = state != HookLink.Off;
	}
}
