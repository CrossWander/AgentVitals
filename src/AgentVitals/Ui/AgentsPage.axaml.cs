using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AgentVitals.Core;
using AgentVitals.Core.Sessions;
using AgentVitals.Platform;

namespace AgentVitals;

/// <summary>Вкладка «Агенты» окна AgentVitals: за кем следить, порядок вкладок, живые сессии и аккаунты с их именами.
/// Правки сохраняются в config.json сразу.</summary>
internal partial class AgentsPage : UserControl
{
	private readonly Poller _poller;

	// Выбранный слева агент; пропал из списка — первый.
	private string? _picked;

	// Имена, которые ввели, но не сохранили, по ключу «агент/аккаунт»: строки перерисовываются, ввод остаётся.
	private readonly Dictionary<string, string> _drafts = [];

	public AgentsPage(Poller poller)
	{
		_poller = poller;
		InitializeComponent();
		Render();
	}

	/// <summary>Порядок, выключенные агенты, hooks или имена аккаунтов поменялись: окна программы рисуются заново.</summary>
	public event Action? Changed;

	/// <summary>Нарисовать по свежему состоянию. Имя, которое сейчас вводят, не трогается: опрос раз в минуту стёр бы его.</summary>
	/// <param name="eye">Переключили глаз: почты прячутся сразу и при вводе имени — введённое ждёт в черновике.</param>
	public void Render(bool eye = false)
	{
		var rows = Views.AgentRows(_poller.Agents);
		_picked = rows.Any(row => row.Name == _picked) ? _picked : rows.FirstOrDefault()?.Name;
		AgentList.ItemsSource = rows.Select(row => row with { Picked = row.Name == _picked }).ToList();

		var found = rows.Where(row => row.Found).ToList();
		var place = found.FindIndex(row => row.Name == _picked);
		UpButton.IsEnabled = place > 0;
		DownButton.IsEnabled = place >= 0 && place < found.Count - 1;

		if (eye || TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not TextBox)
		{
			ShowDetails();
		}
	}

	private AgentState? Picked => _poller.Agents.FirstOrDefault(state => state.Agent.Name == _picked);

	private void ShowDetails(HookTarget? failed = null, string? error = null)
	{
		if (Picked is not { } state)
		{
			return;
		}

		var number = Views.AgentRows(_poller.Agents).First(row => row.Name == state.Agent.Name).Number;
		AgentName.Text = state.Agent.Name;
		AgentDot.Fill = Views.Brush(Dot(state));
		AgentState.Text = Text.AgentDetails(state, number);
		AgentError.Text = state.Error is { } problem && state.Installed && !state.Off ? Text.Error(problem, state.Agent.Name) : "";
		AgentError.IsVisible = AgentError.Text.Length > 0;

		// Как было в настройках: строка — только у агента, чья папка настроек есть.
		var target = state.Agent.Hooks;
		HooksCard.IsVisible = target is not null && Directory.Exists(Path.GetDirectoryName(target.File));
		HooksCard.DataContext = target is null ? null : HookRow.Of(target, target == failed ? error : null);

		var accounts = Views.AccountEdits(state, Store.Current.AccountNames, Store.Current.Private, DateTime.UtcNow, Store.Current.Percent);
		AccountsCard.IsVisible = accounts.Count > 0;
		AccountRows.ItemsSource = accounts;
		AccountsFoot.Inlines = Code($"{Text.AccountsHow(state.Agent.Name)} {Localization.Get("langAccountsMemo")}");
	}

	private static string Dot(AgentState state) =>
		!state.Installed || state.Off ? "Muted"
		: state.Error is not null ? "Warn"
		: state.Snapshot is not null ? "Good"
		: "Muted";

	/// <summary>Текст с `командами`: они — моноширинным на подложке.</summary>
	private static InlineCollection Code(string text)
	{
		var parts = text.Split('`');
		InlineCollection inlines = [];

		for (var i = 0; i < parts.Length; i++)
		{
			inlines.Add(i % 2 == 0 ? new Run(parts[i]) : new Run($" {parts[i]} ") { FontFamily = new FontFamily("Consolas"), Background = Views.Brush("Well") });
		}

		return inlines;
	}

	/// <summary>Последние цифры аккаунта: израсходованное — цветом, у занятого — когда освободится.</summary>
	private void OnNumbers(object? sender, EventArgs e)
	{
		if (sender is not TextBlock { DataContext: AccountEditView row } block)
		{
			return;
		}

		InlineCollection inlines = [];

		foreach (var number in row.Numbers)
		{
			inlines.Add(new Run($"{number.Title} "));
			inlines.Add(number.Spent ? new Run(number.Percent) { Foreground = Views.Brush("Warn"), FontWeight = FontWeight.SemiBold } : new Run(number.Percent));
			inlines.Add(new Run(" · "));
		}

		inlines.Add(new Run(row.Age));

		if (row.Note is { } note)
		{
			inlines.Add(new Run($"; {note}"));
		}

		block.Inlines = inlines;
	}

	private void OnAgent(object? sender, PointerReleasedEventArgs e)
	{
		if (e.InitialPressMouseButton == MouseButton.Left && sender is Control { DataContext: AgentRowView row })
		{
			_picked = row.Name;
			Render();
			ShowDetails();
		}
	}

	private void OnShow(object? sender, RoutedEventArgs e)
	{
		// Переключатель выставляется и при отрисовке строки: выбор человека — только когда он расходится с сохранённым.
		if (sender is CheckBox { DataContext: AgentRowView row, IsChecked: { } on } && on != row.Shown)
		{
			Store.Current.SwitchedOff.Remove(row.Name);

			if (!on)
			{
				Store.Current.SwitchedOff.Add(row.Name);
			}

			Apply();
		}
	}

	private void OnUp(object? sender, RoutedEventArgs e) => Move(-1);

	private void OnDown(object? sender, RoutedEventArgs e) => Move(1);

	/// <summary>Поменять выбранного агента местами с соседом среди найденных; ненайденные остаются в конце.</summary>
	private void Move(int step)
	{
		var rows = Views.AgentRows(_poller.Agents);
		List<string> order = [.. rows.Where(row => row.Found).Select(row => row.Name)];
		var place = order.IndexOf(_picked ?? "");

		if (place < 0 || place + step < 0 || place + step >= order.Count)
		{
			return;
		}

		(order[place], order[place + step]) = (order[place + step], order[place]);
		Store.Current.AgentOrder = [.. order, .. rows.Where(row => !row.Found).Select(row => row.Name)];
		Apply();
	}

	private void Apply()
	{
		Store.Save();
		_poller.Reload();
		Render();
		Changed?.Invoke();
	}

	private string NameKey(AccountEditView row) => $"{_picked}/{row.Key}";

	// Строка аккаунта построена заново: в поле — недописанное имя, если оно есть, иначе сохранённое.
	private void OnNameRow(object? sender, EventArgs e)
	{
		if (sender is TextBox { DataContext: AccountEditView row } box)
		{
			box.Text = _drafts.GetValueOrDefault(NameKey(row)) ?? row.Name;
			Mark(box, row);
		}
	}

	private void OnNameChanged(object? sender, TextChangedEventArgs e)
	{
		if (sender is not TextBox { DataContext: AccountEditView row } box)
		{
			return;
		}

		if ((box.Text ?? "").Trim() == row.Name)
		{
			_drafts.Remove(NameKey(row));
		}
		else
		{
			_drafts[NameKey(row)] = box.Text ?? "";
		}

		Mark(box, row);
	}

	// Имя правят — вместо «в работе» и «Забыть» кнопка «Сохранить»: без неё не видно, что ввод ещё не сохранён.
	private static void Mark(TextBox box, AccountEditView row)
	{
		var editing = (box.Text ?? "").Trim() != row.Name;

		foreach (var control in ((Grid)box.Parent!).Children.OfType<Panel>().Single().Children)
		{
			control.IsVisible = control.Classes.Contains("save") ? editing : !editing && control.Classes.Contains("inuse") == row.InUse;
		}
	}

	// Enter сохраняет, Esc возвращает сохранённое; без правки Esc закрывает окно, как везде.
	private void OnNameKey(object? sender, KeyEventArgs e)
	{
		if (sender is not TextBox { DataContext: AccountEditView row } box)
		{
			return;
		}

		if (e.Key == Key.Enter)
		{
			Save(row);
			e.Handled = true;
		}
		else if (e.Key == Key.Escape && _drafts.ContainsKey(NameKey(row)))
		{
			box.Text = row.Name;
			e.Handled = true;
		}
	}

	private void OnSave(object? sender, RoutedEventArgs e)
	{
		if (sender is Control { DataContext: AccountEditView row })
		{
			Save(row);
		}
	}

	private void Save(AccountEditView row)
	{
		var key = NameKey(row);

		if (!_drafts.Remove(key, out var text))
		{
			return;
		}

		var name = text.Trim();

		if (name.Length == 0)
		{
			Store.Current.AccountNames.Remove(key);
		}
		else
		{
			Store.Current.AccountNames[key] = name;
		}

		Store.Save();

		// Строки пересобираются после события: поле, в котором нажали Enter, ещё обрабатывает его.
		Dispatcher.UIThread.Post(() => ShowDetails());
		Changed?.Invoke();
	}

	private void OnForget(object? sender, RoutedEventArgs e)
	{
		if (sender is Control { DataContext: AccountEditView row } && Picked is { } state)
		{
			_poller.Forget(state, row.Key);
			_drafts.Remove(NameKey(row));
			Store.Current.AccountNames.Remove($"{state.Agent.Name}/{row.Key}");
			Store.Save();
			ShowDetails();
			Changed?.Invoke();
		}
	}

	private void OnConnect(object? sender, RoutedEventArgs e)
	{
		if (sender is Control { DataContext: HookRow row })
		{
			ChangeHooks(row.Target, () => HookInstaller.Install(row.Target, Environment.ProcessPath!), $"hooks connected to {row.Title}");
		}
	}

	private void OnDisconnect(object? sender, RoutedEventArgs e)
	{
		if (sender is Control { DataContext: HookRow row })
		{
			ChangeHooks(row.Target, () => HookInstaller.Uninstall(row.Target), $"hooks disconnected from {row.Title}");
		}
	}

	private void ChangeHooks(HookTarget target, Action change, string journal)
	{
		try
		{
			change();
			Journal.Add(journal);
			ShowDetails();
			Changed?.Invoke();
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
		{
			ShowDetails(target, exception.Message);
		}
	}
}

/// <summary>Hooks агента в разделе «Живые сессии».</summary>
internal sealed record HookRow(HookTarget Target, string State, bool CanConnect, bool CanDisconnect)
{
	public string Title => Target.Title;

	/// <summary>Состояние hooks словами: подключены, нет или смотрят на старый путь программы.</summary>
	/// <param name="error">Почему не удалось подключить или отключить; null — не пробовали или вышло.</param>
	public static HookRow Of(HookTarget target, string? error)
	{
		var state = HookInstaller.Link(target, out var readError);
		error ??= readError;

		var text = error is not null
			? Localization.Format("langHooksError", Path.GetFileName(target.File), error)
			: Localization.Get(state switch
			{
				HookLink.Off => "langHooksOff",
				HookLink.On => "langHooksOn",
				HookLink.Moved => "langHooksMoved",
			} + target.Agent);

		return new(target, text, state != HookLink.On, state != HookLink.Off);
	}
}
