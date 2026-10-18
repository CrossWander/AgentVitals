using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AgentVitals.Core;
using AgentVitals.Core.Sessions;
using AgentVitals.Platform;

namespace AgentVitals;

/// <summary>Окно «Агенты»: за кем следить, порядок вкладок, живые сессии и аккаунты с их именами.
/// Одно на программу; правки сохраняются в config.json сразу.</summary>
internal partial class AgentsWindow : Window
{
	private readonly Poller _poller;

	// Выбранный слева агент; пропал из списка — первый.
	private string? _picked;

	public AgentsWindow(Poller poller)
	{
		_poller = poller;
		InitializeComponent();
		Render();
	}

	/// <summary>Порядок, выключенные агенты, hooks или имена аккаунтов поменялись: окна программы рисуются заново.</summary>
	public event Action? Changed;

	/// <summary>Нарисовать по свежему состоянию. Имя, которое сейчас вводят, не трогается: опрос раз в минуту стёр бы его.</summary>
	public void Render()
	{
		Title = $"AgentVitals — {Localization.Get("langAgents")}";

		var rows = Views.AgentRows(_poller.Agents);
		_picked = rows.Any(row => row.Name == _picked) ? _picked : rows.FirstOrDefault()?.Name;
		AgentList.ItemsSource = rows.Select(row => row with { Picked = row.Name == _picked }).ToList();

		var found = rows.Where(row => row.Found).ToList();
		var place = found.FindIndex(row => row.Name == _picked);
		UpButton.IsEnabled = place > 0;
		DownButton.IsEnabled = place >= 0 && place < found.Count - 1;

		if (FocusManager?.GetFocusedElement() is not TextBox)
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
		AgentState.Text = string.Join(" · ", new[] { Text.AgentLine(state), number.Length > 0 ? Localization.Format("langAgentTab", number) : null }.OfType<string>());
		AgentError.Text = state.Error is { } problem && state.Installed && !state.Off ? Text.Error(problem, state.Agent.Name) : "";
		AgentError.IsVisible = AgentError.Text.Length > 0;

		// Как было в настройках: строка — только у агента, чья папка настроек есть.
		var target = state.Agent.Hooks;
		HooksCard.IsVisible = target is not null && Directory.Exists(Path.GetDirectoryName(target.File));
		HookRows.ItemsSource = target is null ? null : new[] { HookRow.Of(target, target == failed ? error : null) };

		var accounts = Views.AccountEdits(state, Store.Current.AccountNames, Store.Current.Private, DateTime.UtcNow);
		AccountsCard.IsVisible = accounts.Count > 0;
		AccountsTitle.Text = Localization.Format("langAccounts", accounts.Count);
		AccountRows.ItemsSource = accounts;
		AccountsFoot.Text = Localization.Format("langAccountsFoot", Text.Login(state.Agent.Name));
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

	private void OnNameKey(object? sender, KeyEventArgs e)
	{
		if (e.Key == Key.Enter && sender is TextBox box)
		{
			SaveName(box);
		}
	}

	private void OnNameDone(object? sender, RoutedEventArgs e)
	{
		if (sender is TextBox box)
		{
			SaveName(box);
		}
	}

	private void SaveName(TextBox box)
	{
		if (box.DataContext is not AccountEditView row || _picked is not { } agent)
		{
			return;
		}

		var key = $"{agent}/{row.Key}";
		var name = box.Text?.Trim() ?? "";

		if (name == (Store.Current.AccountNames.GetValueOrDefault(key) ?? ""))
		{
			return;
		}

		if (name.Length == 0)
		{
			Store.Current.AccountNames.Remove(key);
		}
		else
		{
			Store.Current.AccountNames[key] = name;
		}

		Store.Save();
		Changed?.Invoke();
	}

	private void OnForget(object? sender, RoutedEventArgs e)
	{
		if (sender is Control { DataContext: AccountEditView row } && Picked is { } state)
		{
			_poller.Forget(state, row.Key);
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

/// <summary>Строка hooks агента в разделе «Живые сессии».</summary>
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
