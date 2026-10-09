using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;

namespace AgentVitals;

/// <summary>Окно по щелчку на значке: квоты выбранного агента, сессии и настройки.
/// Щелчок в другое окно его не закрывает — только ✕, повторный щелчок по значку или Esc.</summary>
internal partial class Popover : TrayWindow
{
	private Vitals? _vitals;
	private string? _selected;
	private ChartMode _mode = ChartMode.Cost;
	private (IReadOnlyList<UsageDay>? Days, ChartMode Mode, string Language, ThemeVariant? Theme) _shown;
	private IReadOnlyList<BarView> _bars = [];
	private string _total = "";
	private DateOnly? _hovered;

	public Popover()
	{
		InitializeComponent();

		Settings.Back += () => ShowSettings(false);
		Settings.Changed += () => Changed?.Invoke();
		Settings.ExitRequested += () => ExitRequested?.Invoke();
	}

	public event Action? RefreshRequested;

	/// <summary>В настройках что-то поменяли — подписи из кода пора перерисовать.</summary>
	public event Action? Changed;

	public event Action? ExitRequested;

	public void Open(PixelRect anchor, bool settings)
	{
		ShowSettings(settings);
		ShowAt(anchor);
		Activate();
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		if (e.Key == Key.Escape)
		{
			Hide();
			e.Handled = true;
		}
		else if (RefreshKey(e.Key, e.KeyModifiers))
		{
			RefreshRequested?.Invoke();
			e.Handled = true;
		}
		else if (TabKey(e.Key, e.KeyModifiers) is { } index && index < Tabs.Children.Count && Tabs.Children[index] is RadioButton tab)
		{
			tab.IsChecked = true;
			e.Handled = true;
		}

		base.OnKeyDown(e);
	}

	internal static bool RefreshKey(Key key, KeyModifiers modifiers) =>
		key == Key.F5 || (key == Key.R && modifiers == KeyModifiers.Control);

	/// <summary>Ctrl+1…9 — вкладка по порядку, с нуля; null — не то сочетание.</summary>
	internal static int? TabKey(Key key, KeyModifiers modifiers) =>
		modifiers != KeyModifiers.Control ? null
		: key is >= Key.D1 and <= Key.D9 ? key - Key.D1
		: key is >= Key.NumPad1 and <= Key.NumPad9 ? key - Key.NumPad1
		: null;

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
		DashboardButton.IsVisible = provider?.Dashboard is not null;

		var usage = provider?.Usage;
		UsageBlock.IsVisible = usage is not null;

		if (usage is not null)
		{
			ShowUsage(usage);
		}
	}

	private void ShowUsage(UsageView usage)
	{
		CostValue.Text = usage.Cost;
		CostNote.Text = usage.CostNote;
		CostYesterday.Text = usage.CostYesterday;
		TokensValue.Text = usage.Tokens;
		TokensNote.Text = usage.TokensNote;
		TokensYesterday.Text = usage.TokensYesterday;
		WorkValue.Text = usage.WorkTime;
		WorkYesterday.Text = usage.WorkTimeYesterday;

		// Render бывает раз в секунду, пока Claude работает: столбики пересоздаются, только когда сменились
		// дни, мера, язык или тема (цвета слоёв берутся из её палитры), — иначе столбик под мышью терял бы свою строку.
		var shown = (usage.Days, _mode, Localization.Current, Application.Current?.ActualThemeVariant);

		if (shown != _shown)
		{
			_shown = shown;
			_bars = Views.Bars(usage.Days, _mode);
			Bars.ItemsSource = _bars;
			var axis = Views.AxisY(Views.Ceiling(Views.Top(usage.Days, _mode)), _mode);
			(AxisTop.Text, AxisMiddle.Text, AxisBottom.Text) = (axis[0], axis[1], axis[2]);
			ShowDates(usage.Days);
			_total = usage.Days.Count == 0 ? "" : Views.Total(usage.Days, _mode);

			// Легенда всегда в одну строку: у любой меры она есть, и окно не прыгает на её высоту.
			Legend.ItemsSource = Views.Legend(_mode);
		}

		ChartLine.Text = _bars.FirstOrDefault(bar => bar.Date == _hovered)?.Line ?? _total;
	}

	/// <summary>Даты под столбиками: по колонке на день и последняя — шириной с подписи шкалы; подпись тянется на неделю вправо.</summary>
	private void ShowDates(IReadOnlyList<UsageDay> days)
	{
		AxisX.Children.Clear();
		AxisX.ColumnDefinitions.Clear();

		foreach (var _ in days)
		{
			AxisX.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
		}

		AxisX.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto) { SharedSizeGroup = "Axis" });

		foreach (var (index, text) in Views.AxisX(days))
		{
			var label = new TextBlock
			{
				Text = text,
				Classes = { "faint" },
			};

			Grid.SetColumn(label, index);
			Grid.SetColumnSpan(label, Math.Min(7, days.Count + 1 - index));
			AxisX.Children.Add(label);
		}
	}

	private void OnMode(object? sender, RoutedEventArgs e)
	{
		if (sender is RadioButton { IsChecked: true, Tag: string tag } && Enum.TryParse<ChartMode>(tag, out var mode))
		{
			_mode = mode;

			// Срабатывает и при загрузке разметки, когда показывать ещё нечего.
			if (_vitals is not null)
			{
				ShowProvider();
			}
		}
	}

	private void OnBarEnter(object? sender, PointerEventArgs e)
	{
		if (sender is Control { DataContext: BarView bar })
		{
			_hovered = bar.Date;
			ChartLine.Text = bar.Line;
		}
	}

	private void OnBarLeave(object? sender, PointerEventArgs e)
	{
		_hovered = null;
		ChartLine.Text = _total;
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

	private void OnClose(object? sender, RoutedEventArgs e) => Hide();

	private async void OnDashboard(object? sender, RoutedEventArgs e)
	{
		if (_vitals?.Providers.FirstOrDefault(provider => provider.Name == _selected)?.Dashboard is { } dashboard)
		{
			await Launcher.LaunchUriAsync(new Uri(dashboard));
		}
	}

	private async void OnStatus(object? sender, PointerReleasedEventArgs e) =>
		await Launcher.LaunchUriAsync(new Uri(StatusPage.Url));
}
