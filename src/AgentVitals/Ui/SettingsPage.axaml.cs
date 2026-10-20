using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AgentVitals.Agents;
using AgentVitals.Core.Quotas;
using AgentVitals.Core.Sessions;
using AgentVitals.Platform;

namespace AgentVitals;

/// <summary>Вкладка «Настройки» окна AgentVitals: разделы слева, строки справа. Всё применяется сразу и сохраняется
/// в config.json.</summary>
internal partial class SettingsPage : UserControl
{
	// Пока переключатели выставляются по сохранённому, их события — не выбор человека.
	private bool _loading;

	// Последний ответ GitHub — от кнопки или автопроверки; null — ещё не спрашивали.
	private Release? _release;

	// Открытый раздел — Tag его строки слева: General, Notifications, Widget, System, About.
	private string _section = "General";

	public SettingsPage()
	{
		InitializeComponent();

		// Не в разметке: событие пришло бы раньше, чем найдены строки разделов.
		GeneralTab.IsChecked = true;
		ShowTexts();
	}

	public event Action? Changed;

	public event Action? ExitRequested;

	public void Load()
	{
		_loading = true;

		Select(Languages, Localization.Current);
		Select(Themes, Store.Current.Theme.ToString());
		Select(Intervals, Store.Current.IntervalSeconds.ToString(CultureInfo.InvariantCulture));
		Select(Percents, Store.Current.Percent.ToString());
		Select(TurnMinutes, Store.Current.TurnMinutes.ToString(CultureInfo.InvariantCulture));
		ShowLevels();
		SessionToastsToggle.IsChecked = Store.Current.SessionToasts;
		TurnPanel.IsEnabled = TurnEnabled(Store.Current.SessionToasts, Store.Current.Widget);

		// Виджет держится на Win32: на других системах раздела нет.
		WidgetTab.IsVisible = OperatingSystem.IsWindows();
		Select(WidgetKinds, Store.Current.Widget.ToString());
		WaitingSoundToggle.IsChecked = Store.Current.WaitingSound;
		WaitingSoundToggle.IsEnabled = Store.Current.SessionToasts;

		LowSoundToggle.IsChecked = Store.Current.LowSound;

		// Звук играет только Windows (winmm).
		WaitingSoundRow.IsVisible = OperatingSystem.IsWindows();
		LowSoundRow.IsVisible = OperatingSystem.IsWindows();
		AutostartCard.IsVisible = OperatingSystem.IsWindows();

		if (OperatingSystem.IsWindows())
		{
			AutostartToggle.IsChecked = Autostart.IsEnabled();
		}

		AutoUpdateToggle.IsChecked = Store.Current.AutoUpdate;
		ShowTexts();
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
			ShowTexts();
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

	private void OnPercent(object? sender, RoutedEventArgs e)
	{
		if (!_loading && sender is RadioButton { IsChecked: true, Tag: string name })
		{
			Store.Current.Percent = Enum.Parse<PercentMode>(name);
			Store.Save();
			ShowLevels();
			Changed?.Invoke();
		}
	}

	private void OnLevel(object? sender, RoutedEventArgs e)
	{
		if (sender is Button { Tag: string tag } && tag.Split(':') is [var which, var step])
		{
			var delta = Step(int.Parse(step, CultureInfo.InvariantCulture), Store.Current.Percent);
			Store.Current.Thresholds = QuotaAlarm.Shift(Store.Current.Thresholds, which == "critical", delta);
			Store.Save();
			ShowLevels();

			// По порогам и цвета: значок и карточки перекрашиваются сразу, а не со следующим опросом.
			Changed?.Invoke();
		}
	}

	/// <summary>Сдвиг порога кнопкой: «+» прибавляет к показанному числу, а в режиме «осталось» это меньше израсходованного.</summary>
	internal static int Step(int step, PercentMode percent) => percent == PercentMode.Left ? -step : step;

	private void ShowLevels()
	{
		var left = Store.Current.Percent == PercentMode.Left;
		ImportantName.Text = Localization.Get(left ? "langLevelImportantLeft" : "langLevelImportant");
		CriticalName.Text = Localization.Get(left ? "langLevelCriticalLeft" : "langLevelCritical");
		ImportantValue.Text = Text.Level(Store.Current.ImportantAt, Store.Current.Percent);
		CriticalValue.Text = Text.Level(Store.Current.CriticalAt, Store.Current.Percent);
	}

	private void OnSessionToasts(object? sender, RoutedEventArgs e)
	{
		if (!_loading)
		{
			Store.Current.SessionToasts = SessionToastsToggle.IsChecked == true;
			TurnPanel.IsEnabled = TurnEnabled(Store.Current.SessionToasts, Store.Current.Widget);
			WaitingSoundToggle.IsEnabled = Store.Current.SessionToasts;
			Store.Save();
		}
	}

	/// <summary>Порог «ход дольше» нужен, пока о конце хода говорит хоть кто-то: тост или виджет.</summary>
	internal static bool TurnEnabled(bool toasts, WidgetKind widget) => toasts || widget != WidgetKind.Off;

	private void OnWidget(object? sender, RoutedEventArgs e)
	{
		if (!_loading && sender is RadioButton { IsChecked: true, Tag: string name })
		{
			Store.Current.Widget = Enum.Parse<WidgetKind>(name);
			Store.Save();
			TurnPanel.IsEnabled = TurnEnabled(Store.Current.SessionToasts, Store.Current.Widget);

			// Виджет включается и меняет вид сразу, без перезапуска.
			Changed?.Invoke();
		}
	}

	private void OnLowSound(object? sender, RoutedEventArgs e)
	{
		if (!_loading)
		{
			Store.Current.LowSound = LowSoundToggle.IsChecked == true;
			Store.Save();
		}
	}

	private void OnWaitingSound(object? sender, RoutedEventArgs e)
	{
		if (!_loading)
		{
			Store.Current.WaitingSound = WaitingSoundToggle.IsChecked == true;
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

	private void OnOpenReleases(object? sender, RoutedEventArgs e) => Launch(Updates.Releases);

	private void OnOpenRepository(object? sender, RoutedEventArgs e) => Launch(Updates.Repository);

	private void OnWhatsNew(object? sender, RoutedEventArgs e) => Launch($"{Updates.Releases}/tag/v{Updates.Current}");

	private async void Launch(string address)
	{
		if (TopLevel.GetTopLevel(this) is { } top)
		{
			await top.Launcher.LaunchUriAsync(new Uri(address));
		}
	}

	private void OnSection(object? sender, RoutedEventArgs e)
	{
		if (sender is RadioButton { IsChecked: true, Tag: string section })
		{
			_section = section;
			ShowSection();
		}
	}

	private void ShowSection()
	{
		GeneralRows.IsVisible = _section == "General";
		NotificationsRows.IsVisible = _section == "Notifications";
		WidgetRows.IsVisible = _section == "Widget";
		SystemRows.IsVisible = _section == "System";
		AboutView.IsVisible = _section == "About";

		// У «О программе» заголовка нет: всё по центру.
		SectionHead.IsVisible = _section != "About";
		SectionTitle.Text = Localization.Get($"lang{_section}");
		SectionHint.Text = Localization.Has($"lang{_section}Hint") ? Localization.Get($"lang{_section}Hint") : "";
	}

	/// <summary>Строки, которые собирает код: при смене языка — заново.</summary>
	private void ShowTexts()
	{
		AboutNote.Text = Localization.Format("langAboutNote", Updates.Current);
		AboutLine.Text = Localization.Format("langAboutLine", Updates.Current);
		WhatsNew.Content = Localization.Format("langWhatsNew", Updates.Current);
		ShowLevels();
		ShowSection();
		ShowRelease();
	}

	/// <summary>Узнали, есть ли новая версия: шестерня ставит метку.</summary>
	public event Action<Release?>? ReleaseChanged;

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
		UpdateLine.Classes.Set("newer", newer);

		// В списке разделов: есть новая версия — у «Запуск и обновления», цветом.
		SystemNote.Text = _release is { State: UpdateState.Newer } ? Localization.Format("langUpdateBadge", _release.Version) : Localization.Get("langSystemNote");
		SystemNote.Classes.Set("newer", _release is { State: UpdateState.Newer });
		ReleaseChanged?.Invoke(_release);
	}
}
