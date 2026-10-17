using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace AgentVitals;

/// <summary>Окно по щелчку на значке: квоты, статус и сессии выбранного агента и настройки.
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

		// Окно прижато к значку и растёт вверх: вкладка ниже соседней — у Copilot нет статистики и сессий — сдвигала
		// бы верх и вкладки под мышью на полэкрана. Поэтому окно не становится ниже, чем уже было.
		// ponytail: самая высокая вкладка, открытая впервые, ещё раз поднимет окно; потом — ни скачка.
		Resized += (_, e) => MinHeight = Math.Max(MinHeight, Math.Min(e.ClientSize.Height, MaxHeight));
	}

	public event Action? RefreshRequested;

	/// <summary>Щелчок по сессии в списке.</summary>
	public event Action<Session>? SessionChosen;

	/// <summary>Глаз: почту спрятали или показали — окна рисуются заново.</summary>
	public event Action? PrivateChanged;

	public void Open(PixelRect anchor, bool settings)
	{
		ShowSettings(settings);

		// Другой экран бывает ниже: MinHeight сильнее MaxHeight, а середина окна и так прокручивается.
		Placement.Fit(this, anchor);
		MinHeight = Math.Min(MinHeight, MaxHeight);
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
			// Другой набор агентов — другая самая высокая вкладка.
			MinHeight = 0;
			Tabs.Children.Clear();

			foreach (var name in names)
			{
				var tab = new RadioButton
				{
					Theme = (ControlTheme)this.FindResource("Tab")!,

					// Точка — у вкладки соседа, когда у него запас на исходе (ShowProvider).
					Content = new StackPanel
					{
						Orientation = Orientation.Horizontal,
						Spacing = 6,
						Children =
						{
							new TextBlock { Text = name },
							new Ellipse { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center, IsVisible = false },
						},
					},
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

	private ProviderView? Selected => _vitals?.Providers.FirstOrDefault(provider => provider.Name == _selected);

	private void ShowProvider()
	{
		var provider = Selected;

		// Шапка — о выбранном агенте: Claude на 90% не делает «критично» вкладку Codex без трат.
		HealthPill.DataContext = provider;
		HealthPill.IsVisible = provider is not null;
		ShowAlerts();

		Cards.ItemsSource = provider?.Cards;
		InfoLine.Text = string.Join(" · ", new[] { provider?.Updated, provider?.Info }.Where(text => !string.IsNullOrEmpty(text)));
		PrivateIcon.Data = this.FindResource(Store.Current.Private ? "IconEyeOff" : "IconEye") as Geometry;
		ToolTip.SetTip(PrivateButton, Localization.Get(Store.Current.Private ? "langShowPrivate" : "langHidePrivate"));
		ErrorLine.Text = provider?.Error;
		ErrorLine.IsVisible = provider?.HasError == true;
		BareHint.Text = provider is null ? null : Localization.Format("langNoStats", provider.Name);
		BareHint.IsVisible = provider?.Bare == true;
		DashboardButton.IsVisible = provider?.Dashboard is not null;
		StatusLine.DataContext = provider;
		StatusLine.IsVisible = provider?.StatusUrl is not null;
		SessionsCard.DataContext = provider;
		SessionsCard.IsVisible = provider?.Sessions is not null;

		var usage = provider?.Usage;
		UsageBlock.IsVisible = usage is not null;
		UsageBlock.DataContext = usage;

		if (usage is not null)
		{
			ShowUsage(usage);
		}
	}

	/// <summary>Точка на вкладке другого агента, у которого запас на исходе; у выбранного об этом говорит шапка.</summary>
	private void ShowAlerts()
	{
		foreach (var tab in Tabs.Children.OfType<RadioButton>())
		{
			if (tab.Content is StackPanel { Children: [_, Ellipse dot] })
			{
				var provider = _vitals?.Providers.FirstOrDefault(provider => provider.Name == tab.Tag as string);
				dot.IsVisible = provider is { Alert: true } && provider.Name != _selected;
				dot.Fill = provider?.HealthBrush;
			}
		}
	}

	private void ShowUsage(UsageView usage)
	{
		// Render бывает раз в секунду, пока Claude работает: столбики пересоздаются, только когда сменились
		// дни, мера, язык или тема (цвета слоёв берутся из её палитры), — иначе столбик под мышью терял бы свою строку.
		var shown = (usage.Days, _mode, Localization.Current, Application.Current?.ActualThemeVariant);

		if (shown != _shown)
		{
			_shown = shown;
			var chart = Views.Chart(usage.Days, _mode);
			_bars = chart.Bars;
			Bars.ItemsSource = _bars;
			(AxisTop.Text, AxisMiddle.Text, AxisBottom.Text) = (chart.AxisY[0], chart.AxisY[1], chart.AxisY[2]);
			ShowDates(usage.Days.Count, chart.AxisX);
			_total = chart.Total;

			// Легенда всегда в одну строку: у любой меры она есть, и окно не прыгает на её высоту.
			Legend.ItemsSource = chart.Legend;
		}

		ChartLine.Text = _bars.FirstOrDefault(bar => bar.Date == _hovered)?.Line ?? _total;
	}

	/// <summary>Даты под столбиками: по колонке на день и последняя — шириной с подписи шкалы; подпись тянется на неделю вправо.</summary>
	private void ShowDates(int days, IReadOnlyList<(int Index, string Text)> ticks)
	{
		AxisX.Children.Clear();
		AxisX.ColumnDefinitions.Clear();

		for (var day = 0; day < days; day++)
		{
			AxisX.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
		}

		AxisX.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto) { SharedSizeGroup = "Axis" });

		foreach (var (index, text) in ticks)
		{
			var label = new TextBlock
			{
				Text = text,
				Classes = { "faint" },
			};

			Grid.SetColumn(label, index);
			Grid.SetColumnSpan(label, Math.Min(7, days + 1 - index));
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
		// Главный вид остаётся в раскладке и задаёт высоту окна — и когда настройки открыты прямо из меню;
		// под настройками он не виден и не берёт ни щелчков, ни фокуса.
		MainView.Opacity = settings ? 0 : 1;
		MainView.IsEnabled = !settings;
		Settings.IsVisible = settings;

		if (settings)
		{
			Settings.Load();
		}
	}

	private void OnRefresh(object? sender, RoutedEventArgs e) => RefreshRequested?.Invoke();

	private void OnPrivate(object? sender, RoutedEventArgs e)
	{
		Store.Current.Private = !Store.Current.Private;
		Store.Save();
		PrivateChanged?.Invoke();
	}

	private void OnSettings(object? sender, RoutedEventArgs e) => ShowSettings(true);

	private void OnClose(object? sender, RoutedEventArgs e) => Hide();

	private void OnSession(object? sender, PointerReleasedEventArgs e)
	{
		if (e.InitialPressMouseButton == MouseButton.Left && sender is Control { DataContext: SessionView row })
		{
			SessionChosen?.Invoke(row.Session);
		}
	}

	private async void OnDashboard(object? sender, RoutedEventArgs e)
	{
		if (Selected?.Dashboard is { } dashboard)
		{
			await Launcher.LaunchUriAsync(new Uri(dashboard));
		}
	}

	private async void OnStatus(object? sender, PointerReleasedEventArgs e)
	{
		if (Selected?.StatusUrl is { } url)
		{
			await Launcher.LaunchUriAsync(new Uri(url));
		}
	}
}
