using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AgentVitals;

/// <summary>Вкладка окна AgentVitals: робот открывает «Агенты», шестерня — «Настройки».</summary>
internal enum WindowPage
{
	Agents,
	Settings,
}

/// <summary>Окно AgentVitals: вкладки «Агенты» и «Настройки». Одно на программу.</summary>
internal partial class AppWindow : Window
{
	public AppWindow(Poller poller)
	{
		InitializeComponent();

		Agents = new AgentsPage(poller);
		Settings = new SettingsPage { IsVisible = false };

		foreach (Control page in new Control[] { Agents, Settings })
		{
			Grid.SetRow(page, 1);
			Body.Children.Add(page);
		}

		Agents.Changed += () => Changed?.Invoke();

		// Язык, тема и пороги: строки вкладки «Агенты» собирает код — она рисуется заново.
		Settings.Changed += () =>
		{
			Agents.Render();
			Changed?.Invoke();
		};
		Settings.ReleaseChanged += ShowRelease;
		ShowRelease(null);
	}

	public AgentsPage Agents { get; }

	public SettingsPage Settings { get; }

	/// <summary>Что-то поменяли на вкладках: окна программы рисуются заново.</summary>
	public event Action? Changed;

	public void Open(WindowPage page)
	{
		(page == WindowPage.Settings ? SettingsTab : AgentsTab).IsChecked = true;
		ShowPage(page);
	}

	/// <param name="eye">Переключили глаз: см. <see cref="AgentsPage.Render"/>.</param>
	public void Render(bool eye = false) => Agents.Render(eye);

	// Страница — по нажатой вкладке: соседняя в этот момент ещё отмечена, группа снимает с неё отметку после события.
	private void OnPage(object? sender, RoutedEventArgs e)
	{
		if (sender is RadioButton { IsChecked: true } tab)
		{
			ShowPage(tab == SettingsTab ? WindowPage.Settings : WindowPage.Agents);
		}
	}

	private void ShowPage(WindowPage page)
	{
		var settings = page == WindowPage.Settings;

		// Настройки выставляются по сохранённому при каждом переходе: их могли поменять руками в config.json.
		if (settings && !Settings.IsVisible)
		{
			Settings.Load();
		}

		Agents.IsVisible = !settings;
		Settings.IsVisible = settings;
	}

	private void ShowRelease(Release? release)
	{
		var newer = release is { State: UpdateState.Newer };
		SettingsNote.Text = newer ? Localization.Format("langUpdateBadge", release!.Version) : Localization.Get("langSettingsNote");
		SettingsNote.Classes.Set("newer", newer);
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		if (e.Key == Key.Escape)
		{
			Close();
			e.Handled = true;
		}

		base.OnKeyDown(e);
	}

	// Рамки нет: окно двигают за заголовок.
	private void OnDrag(object? sender, PointerPressedEventArgs e)
	{
		if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
		{
			BeginMoveDrag(e);
		}
	}

	private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
