using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using AgentVitals.Core.Sessions;
using AgentVitals.Core.Usage;

namespace AgentVitals;

/// <summary>Окно по щелчку на значке: квоты, статус и сессии выбранного агента и настройки.
/// Щелчок в другое окно его не закрывает — только ✕, повторный щелчок по значку или Esc.</summary>
internal partial class Popover : TrayWindow
{
	private Vitals? _vitals;
	private string? _selected;

	// Агент → выбранный щелчком аккаунт; нет записи — аккаунт в работе. Живёт, пока окно открыто.
	private readonly Dictionary<string, string> _accounts = [];

	// Агент → аккаунт в работе при прошлом Render: вошли в другой — выбор переходит на него.
	private readonly Dictionary<string, string?> _inUse = [];
	private ChartMode _mode = ChartMode.Cost;
	private (IReadOnlyList<UsageDay>? Days, ChartMode Mode, string Language, ThemeVariant? Theme) _shown;
	private IReadOnlyList<BarView> _bars = [];
	private string _total = "";
	private DateOnly? _hovered;

	public Popover()
	{
		InitializeComponent();


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

	/// <summary>Кнопка с роботом: окно AgentVitals на вкладке «Агенты».</summary>
	public event Action? AgentsRequested;

	/// <summary>Шестерня: окно AgentVitals на вкладке «Настройки».</summary>
	public event Action? SettingsRequested;

	/// <summary>Метка на шестерне — есть новая версия. Держится, пока её не поставят: тост о ней один на версию и мог
	/// пройти мимо.</summary>
	public void ShowRelease(Release? release)
	{
		var newer = release is { State: UpdateState.Newer };
		UpdateBadge.IsVisible = newer;
		ToolTip.SetTip(SettingsButton, newer ? Localization.Format("langUpdateAvailable", release!.Version) : Localization.Get("langSettings"));
	}

	public void Open(PixelRect anchor)
	{
		// При каждом открытии сверху — аккаунт в работе: выбор прошлого раза сбрасывается и вкладка рисуется заново.
		_accounts.Clear();
		ShowAgent();

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

		var names = vitals.Agents.Select(agent => agent.Name).ToList();
		_selected = Keep(_selected, names);

		foreach (var agent in vitals.Agents)
		{
			var inUse = agent.Accounts.FirstOrDefault(account => account.InUse)?.Key;

			if (_inUse.TryGetValue(agent.Name, out var was) && was != inUse)
			{
				_accounts.Remove(agent.Name);
			}

			_inUse[agent.Name] = inUse;
		}

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

					// Точка — у вкладки соседа, когда у него запас на исходе (ShowAgent).
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
					GroupName = "agents",
					IsChecked = name == _selected,
					Tag = name,
				};

				tab.IsCheckedChanged += OnTab;
				Tabs.Children.Add(tab);
			}
		}

		ShowAgent();
	}

	/// <summary>Щелчок по тосту о запомненном аккаунте — его вкладка, и сверху он; по агенту в виджете — только вкладка.</summary>
	public void Choose(string agent, string? account = null)
	{
		_selected = agent;

		if (account is not null)
		{
			_accounts[agent] = account;
		}

		foreach (var tab in Tabs.Children.OfType<RadioButton>())
		{
			tab.IsChecked = tab.Tag as string == agent;
		}

		ShowAgent();
	}

	/// <summary>Какой аккаунт показать сверху: выбранный, а забытый или пропавший — аккаунт в работе; один аккаунт — null.</summary>
	internal static AccountView? Chosen(AgentView agent, string? key) =>
		agent.Accounts.FirstOrDefault(account => account.Key == key) ?? agent.Accounts.FirstOrDefault(account => account.InUse);

	/// <summary>Строка под вкладками: «Обновлено только что · <b>Team</b> · Рабочий» — тариф жирным, он есть и под глазом.
	/// Цветом обычного текста, как имя в строке аккаунта: полужирный бледным в 11 px от обычного не отличить.</summary>
	private static InlineCollection Line(string? updated, string? plan, string? info)
	{
		InlineCollection line = [];

		foreach (var (text, bold) in new[] { (updated, false), (plan, true), (info, false) })
		{
			if (string.IsNullOrEmpty(text))
			{
				continue;
			}

			if (line.Count > 0)
			{
				line.Add(new Run(" · "));
			}

			line.Add(bold ? new Run(text) { FontWeight = FontWeight.Bold, Foreground = Views.Brush("Text") } : new Run(text));
		}

		return line;
	}

	/// <summary>Выбранная вкладка по имени: появился или пропал другой агент — выбор не съезжает.</summary>
	internal static string? Keep(string? selected, IReadOnlyList<string> names) =>
		selected is not null && names.Contains(selected) ? selected : names.FirstOrDefault();

	private void OnTab(object? sender, RoutedEventArgs e)
	{
		if (sender is RadioButton { IsChecked: true, Tag: string name })
		{
			_selected = name;
			ShowAgent();
		}
	}

	private AgentView? Selected => _vitals?.Agents.FirstOrDefault(agent => agent.Name == _selected);

	private void ShowAgent()
	{
		var agent = Selected;

		// Шапка — о выбранном агенте: Claude на 90% не делает «критично» вкладку Codex без трат.
		HealthPill.DataContext = agent;
		HealthPill.IsVisible = agent is not null;
		ShowAlerts();

		var chosen = agent is null ? null : Chosen(agent, _accounts.GetValueOrDefault(agent.Name));

		Cards.DataContext = agent;
		Cards.ItemsSource = chosen?.Cards ?? agent?.Cards;
		InfoLine.Inlines = Line(chosen?.Updated ?? agent?.Updated, chosen is null ? agent?.Plan : chosen.Plan, chosen?.Info ?? agent?.Info);
		HintText.Text = chosen?.Hint;
		HintBar.IsVisible = chosen?.HasHint == true;
		AccountsCard.DataContext = agent;
		AccountsCard.IsVisible = agent?.Shared == true;
		AccountRows.ItemsSource = agent is null ? null : agent.Accounts.Select(account => account with { Selected = account.Key == chosen?.Key }).ToList();
		TodayLabel.Text = Localization.Get(agent?.Shared == true ? "langTodayShared" : "langToday");
		Dispatcher.UIThread.Post(FitAccounts, DispatcherPriority.Background);
		PrivateIcon.Data = this.FindResource(Store.Current.Private ? "IconEyeOff" : "IconEye") as Geometry;
		ToolTip.SetTip(PrivateButton, Localization.Get(Store.Current.Private ? "langShowPrivate" : "langHidePrivate"));
		ErrorLine.Text = agent?.Error;
		ErrorLine.IsVisible = agent?.HasError == true;
		BareHint.Text = agent is null ? null : Localization.Format("langNoStats", agent.Name);
		BareHint.IsVisible = agent?.Bare == true;
		DashboardButton.IsVisible = agent?.Dashboard is not null;
		StatusLine.DataContext = agent;
		StatusLine.IsVisible = agent?.StatusUrl is not null;
		SessionsCard.DataContext = agent;
		SessionsCard.IsVisible = agent?.Sessions is not null;

		var usage = agent?.Usage;
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
				var agent = _vitals?.Agents.FirstOrDefault(agent => agent.Name == tab.Tag as string);
				dot.IsVisible = agent is { Alert: true } && agent.Name != _selected;
				dot.Fill = agent?.HealthBrush;
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
				ShowAgent();
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

	private void OnRefresh(object? sender, RoutedEventArgs e) => RefreshRequested?.Invoke();

	private void OnPrivate(object? sender, RoutedEventArgs e)
	{
		Store.Current.Private = !Store.Current.Private;
		Store.Save();
		PrivateChanged?.Invoke();
	}

	private void OnSettings(object? sender, RoutedEventArgs e) => SettingsRequested?.Invoke();

	private void OnAgents(object? sender, RoutedEventArgs e) => AgentsRequested?.Invoke();

	private void OnAccount(object? sender, PointerReleasedEventArgs e)
	{
		if (e.InitialPressMouseButton == MouseButton.Left && sender is Control { DataContext: AccountView row } && _selected is { } agent)
		{
			_accounts[agent] = row.Key;
			ShowAgent();
		}
	}

	/// <summary>Видны три аккаунта, остальные прокручиваются внутри карточки; два-три — карточка ровно по ним.</summary>
	private void FitAccounts()
	{
		var rows = AccountRows.GetRealizedContainers().ToList();
		var height = rows.Count > 3 ? rows.Take(3).Sum(row => row.Bounds.Height) : double.PositiveInfinity;

		if (AccountsScroll.MaxHeight != height)
		{
			AccountsScroll.MaxHeight = height;
		}
	}

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
