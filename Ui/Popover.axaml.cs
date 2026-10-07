using System;
using System.Collections.Generic;
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
	private string? _selected;

	public Popover()
	{
		InitializeComponent();
		Deactivated += (_, _) => Dismiss();

		// Высота зависит от содержимого: после смены экрана или вкладки ставим окно заново.
		// Resized, а не SizeChanged: при показе Windows возвращает окну прежний размер, Avalonia
		// растягивает его снова, а Bounds при этом не меняются и SizeChanged молчит.
		Resized += (_, _) => Placement.Place(this, _anchor);

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
		HiddenAt = DateTime.UtcNow;
	}

	public void Render(Vitals vitals)
	{
		_vitals = vitals;
		DataContext = vitals;

		var names = vitals.Providers.Select(provider => provider.Name).ToList();
		_selected = Keep(_selected, names);

		// Render бывает раз в секунду, пока Claude работает: вкладки пересоздаются, только если сменился
		// набор агентов, иначе щелчок по вкладке, попавший на пересоздание, теряется.
		if (!Tabs.Children.Select(tab => tab.Tag as string).SequenceEqual(names))
		{
			Tabs.Children.Clear();

			foreach (var name in names)
			{
				var tab = new RadioButton
				{
					Theme = (ControlTheme)this.FindResource("Tab")!,
					Content = name,
					GroupName = "providers",
					IsChecked = name == _selected,
					Tag = name,
				};

				tab.IsCheckedChanged += OnTab;
				Tabs.Children.Add(tab);
			}
		}

		ShowProvider();
	}

	/// <summary>Выбранная вкладка по имени: появился или пропал другой агент — выбор не съезжает.</summary>
	internal static string? Keep(string? selected, IReadOnlyList<string> names) =>
		selected is not null && names.Contains(selected) ? selected : names.FirstOrDefault();

	private void OnTab(object? sender, RoutedEventArgs e)
	{
		if (sender is RadioButton { IsChecked: true, Tag: string name })
		{
			_selected = name;
			ShowProvider();
		}
	}

	private void ShowProvider()
	{
		var provider = _vitals?.Providers.FirstOrDefault(provider => provider.Name == _selected);

		Cards.ItemsSource = provider?.Cards;
		InfoLine.Text = string.Join(" · ", new[] { provider?.Updated, provider?.Info }.Where(text => !string.IsNullOrEmpty(text)));
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
