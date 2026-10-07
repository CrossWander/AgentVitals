using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;

namespace AgentVitals;

/// <summary>Окно по щелчку на значке: квоты выбранного агента, сессии и настройки.</summary>
internal partial class Popover : Window
{
	private PixelRect _anchor;
	private Vitals? _vitals;
	private int _selected;

	public Popover()
	{
		InitializeComponent();
		Deactivated += (_, _) => Dismiss();

		// Высота зависит от содержимого: после смены экрана или вкладки ставим окно заново.
		SizeChanged += (_, _) => Placement.Place(this, _anchor);

		Settings.Back += () => ShowSettings(false);
		Settings.Changed += () => Changed?.Invoke();
		Settings.ExitRequested += () => ExitRequested?.Invoke();
	}

	public event Action? RefreshRequested;

	/// <summary>В настройках что-то поменяли — подписи из кода пора перерисовать.</summary>
	public event Action? Changed;

	public event Action? ExitRequested;

	/// <summary>Когда окно спряталось: щелчок по значку сразу после этого — тот же щелчок, что увёл фокус.</summary>
	public DateTime HiddenAt { get; private set; }

	public void Open(PixelRect anchor, bool settings)
	{
		_anchor = anchor;
		ShowSettings(settings);
		Show();
		Placement.Place(this, anchor);
		Activate();
	}

	public void Dismiss()
	{
		if (!IsVisible)
		{
			return;
		}

		Hide();
		HiddenAt = DateTime.Now;
	}

	public void Render(Vitals vitals)
	{
		_vitals = vitals;
		DataContext = vitals;
		_selected = Math.Clamp(_selected, 0, Math.Max(0, vitals.Providers.Count - 1));

		Tabs.Children.Clear();

		for (var index = 0; index < vitals.Providers.Count; index++)
		{
			var tab = new RadioButton
			{
				Theme = (ControlTheme)this.FindResource("Tab")!,
				Content = vitals.Providers[index].Name,
				GroupName = "providers",
				IsChecked = index == _selected,
				Tag = index,
			};

			tab.IsCheckedChanged += OnTab;
			Tabs.Children.Add(tab);
		}

		ShowProvider();
	}

	private void OnTab(object? sender, RoutedEventArgs e)
	{
		if (sender is RadioButton { IsChecked: true, Tag: int index })
		{
			_selected = index;
			ShowProvider();
		}
	}

	private void ShowProvider()
	{
		var provider = _vitals?.Providers.ElementAtOrDefault(_selected);

		Cards.ItemsSource = provider?.Cards;
		InfoLine.Text = string.Join(" · ", new[] { _vitals?.Updated, provider?.Info }.Where(text => !string.IsNullOrEmpty(text)));
		ErrorLine.Text = provider?.Error;
		ErrorLine.IsVisible = provider?.HasError == true;
	}

	private void ShowSettings(bool settings)
	{
		MainView.IsVisible = !settings;
		Settings.IsVisible = settings;

		if (settings)
		{
			Settings.Load();
		}
	}

	private void OnRefresh(object? sender, RoutedEventArgs e) => RefreshRequested?.Invoke();

	private void OnSettings(object? sender, RoutedEventArgs e) => ShowSettings(true);

	private void OnClose(object? sender, RoutedEventArgs e) => Dismiss();

	private async void OnStatus(object? sender, PointerReleasedEventArgs e) =>
		await Launcher.LaunchUriAsync(new Uri(StatusPage.Url));
}
