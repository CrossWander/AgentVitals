using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Media;
using AgentVitals.Core;
using AgentVitals.Core.Quotas;
using AgentVitals.Core.Sessions;

namespace AgentVitals;

/// <summary>Что главное для плашки и значка, по важности.</summary>
internal enum WidgetState
{
	Waiting,
	Finished,
	Low,
	Working,
	Idle,
}

/// <summary>Всё, что рисуют три вида виджета, одним снимком.</summary>
/// <param name="Title">«Claude» в покое; «Claude ждёт разрешения», «Claude закончил».</param>
/// <param name="Value">«46%» в режиме процентов; у «ждёт» и «закончил» и без данных — "".</param>
/// <param name="Detail">Сброс «2 ч 50 мин»; у «ждёт» — «проект · инструмент», у «закончил» — проект; под глазом проектов нет.</param>
/// <param name="Fill">Полоска и кольцо, 0–100, в режиме процентов.</param>
/// <param name="Meter">Цвет квоты показанного агента: полоска 4 px, кольцо, полоска плашки.</param>
/// <param name="Dot">Цвет точки — общее состояние сессий, как у значка в трее.</param>
/// <param name="Agent">Чья вкладка откроется щелчком; null — агентов нет.</param>
/// <param name="LimitMeter">Цвет агента ближе всех к лимиту — полоска плашки «по очереди»: она не мигает со сменой агента.</param>
/// <param name="Waiting">Сессия, о которой «ждёт разрешения»: щелчок по плашке — сразу её окно; null — никто не ждёт.</param>
internal sealed record WidgetView(
	WidgetState State,
	string Title,
	string Value,
	string Detail,
	double Fill,
	IBrush Meter,
	IBrush Dot,
	string? Agent,
	IReadOnlyList<WidgetAgent> Agents,
	IReadOnlyList<WidgetSession> Sessions,
	IBrush? LimitMeter = null,
	Session? Waiting = null)
{
	/// <summary>Плашка раскрывается сама; «мало квоты» и «работает» — только цветом полоски: квота бывает критичной
	/// часами, и раскрытая плашка всё это время закрывала бы вкладки.</summary>
	public bool Loud => State is WidgetState.Waiting or WidgetState.Finished;

	/// <summary>Дуга кольца у значка над треем, в градусах.</summary>
	public double Sweep => Fill * 3.6;

	/// <summary>Есть процент — есть и полоска: у «ждёт» и «закончил» её нет.</summary>
	public bool HasValue => Value.Length > 0;

	public bool HasSessions => Sessions.Count > 0;

	/// <summary>Значок «все сразу» — агенты в ряд; «ждёт» и «закончил» занимают его целиком, без агентов — надпись.</summary>
	public bool ShowsAgents => !Loud && Agents.Count > 0;

	// Значок «по очереди»: столько показывается один агент.
	private static readonly TimeSpan _turn = TimeSpan.FromSeconds(6);

	/// <summary>Агент значка «по очереди»: каждые 6 с следующий из тех, у кого есть данные; квота на исходе — смена
	/// замирает на нём. Без данных ни у кого — null.</summary>
	public WidgetAgent? Turn(DateTime now)
	{
		if (State == WidgetState.Low)
		{
			return Agents.FirstOrDefault(agent => agent.Name == Agent);
		}

		List<WidgetAgent> known = [.. Agents.Where(agent => agent.Known)];

		return known.Count == 0 ? null : known[(int)(now.Ticks / _turn.Ticks % known.Count)];
	}

	/// <summary>Тот же снимок, но с агентом «по очереди» вместо главного; «ждёт», «закончил» и без данных — как есть.</summary>
	public WidgetView Turned(DateTime now) => !Loud && Turn(now) is { } agent
		? this with { Title = agent.Name, Value = agent.Value, Detail = agent.Resets, Fill = agent.Fill, Meter = agent.Meter, Agent = agent.Name }
		: this;
}

/// <summary>Агент в панели и в колонке на рабочем столе.</summary>
/// <param name="Value">«46%»; нет данных — «—».</param>
/// <param name="Window">«сессия · 2 ч 50 мин» — строка панели.</param>
/// <param name="Resets">«2 ч 50 мин» — под колонкой на рабочем столе.</param>
internal sealed record WidgetAgent(string Name, string Value, double Fill, IBrush Meter, string Window, string Resets)
{
	/// <summary>Без данных у агента нет и окна квоты.</summary>
	public bool Known => Window.Length > 0;

	/// <summary>Дуга кольца на значке, в градусах.</summary>
	public double Sweep => Fill * 3.6;
}

/// <summary>Сессия в панели и на рабочем столе; по щелчку поднимается её окно.</summary>
/// <param name="Name">Проект; под глазом — имя агента.</param>
/// <param name="Branch">Ветка; под глазом — "".</param>
internal sealed record WidgetSession(string Name, string Branch, string State, IBrush Dot, Session Session);

internal static partial class Views
{
	// Больше трёх строк сессий виджет не вмещает: остальные — в поповере.
	private const int _widgetSessions = 3;

	/// <summary>Что показать виджету: главного агента и его самое узкое окно, а важнее него — сессию, которая ждёт
	/// человека или только что закончила ход. Главный — с квотой «критично», иначе тот, чья сессия работает, иначе ближе
	/// всех к лимиту.</summary>
	/// <param name="agents">Видимые агенты в порядке вкладок.</param>
	/// <param name="finished">Сессия, чей долгий ход закончился меньше 5 с назад; null — такой нет.</param>
	/// <param name="hide">Показывают экран: проектов, веток и инструментов не видно.</param>
	internal static WidgetView Widget(IReadOnlyList<AgentState> agents, IReadOnlyList<Session> sessions, Session? finished, DateTime now, Thresholds levels, PercentMode percent, bool hide)
	{
		// Правило цвета значка в трее: самое узкое окно; при равенстве — первая вкладка (MaxBy берёт первого).
		var known = agents.Where(state => state.Tightest(now) is not null).ToList();
		var top = known.MaxBy(state => state.Tightest(now));
		var limit = top is null ? Health.Unknown : Quota.Rate(top.Tightest(now)!.Value, levels);

		// Работающий агент без данных не в счёт, как и для значка: показать у него нечего.
		var working = sessions.Where(session => session.State == SessionState.Working).Select(session => session.Agent).ToHashSet();
		var main = limit == Health.Critical ? top : known.Where(state => working.Contains(state.Agent.Name)).MaxBy(state => state.Tightest(now)) ?? top;

		var window = main is null ? null : Quota.Bottleneck(main.Windows(now));
		var health = window is null ? Health.Unknown : Quota.Rate(window.Used, levels);

		List<string> order = [.. agents.Select(state => state.Agent.Name)];
		var waiting = sessions
			.Where(session => session.State == SessionState.Waiting)
			.OrderBy(session => order.IndexOf(session.Agent) is >= 0 and var place ? place : int.MaxValue)
			.FirstOrDefault();

		var state = waiting is not null ? WidgetState.Waiting
			: finished is not null ? WidgetState.Finished
			: health == Health.Critical ? WidgetState.Low
			: sessions.Any(session => session.State == SessionState.Working) ? WidgetState.Working
			: WidgetState.Idle;

		var title = state switch
		{
			WidgetState.Waiting => Localization.Format("langToastWaiting", waiting!.Agent),
			WidgetState.Finished => Localization.Format("langToastDone", finished!.Agent),
			_ when agents.Count == 0 => Localization.Get("langNoAgents"),
			_ => main?.Agent.Name ?? Localization.Get("langWidgetNoData"),
		};

		var detail = state switch
		{
			WidgetState.Waiting => hide ? "" : Join(waiting!.Folder, waiting.Tool),
			WidgetState.Finished => hide ? "" : finished!.Folder,
			_ => window is null ? "" : Text.Short(window.ResetsAt, now),
		};

		var loud = state is WidgetState.Waiting or WidgetState.Finished;

		return new(
			state,
			title,
			loud || window is null ? "" : Percent(window.Used, percent),
			detail,
			window is null ? 0 : Math.Min(Shown(window.Used, percent), 100),
			Brush("Meter" + Level(health)),
			Brush(Dot(SessionWatcher.Overall(sessions))),
			(waiting ?? finished)?.Agent ?? main?.Agent.Name ?? agents.FirstOrDefault()?.Agent.Name,
			[.. agents.Select(agent => WidgetRow(agent, now, levels, percent))],
			[.. sessions
				.OrderByDescending(session => session.State)
				.Take(_widgetSessions)
				.Select(session => new WidgetSession(
					hide ? session.Agent : session.Folder,
					hide ? "" : session.Branch ?? "",
					Text.Session(session, now),
					Brush(Dot(session.State)),
					session))],
			Brush("Meter" + Level(limit)),
			waiting);
	}

	private static WidgetAgent WidgetRow(AgentState agent, DateTime now, Thresholds levels, PercentMode percent)
	{
		// Без входа окна старые: как и для значка, такой агент не в счёт.
		if (agent.Tightest(now) is null || Quota.Bottleneck(agent.Windows(now)) is not { } window)
		{
			return new(agent.Agent.Name, "—", 0, Brush("MeterMuted"), "", "");
		}

		var resets = Text.Short(window.ResetsAt, now);

		return new(
			agent.Agent.Name,
			Percent(window.Used, percent),
			Math.Min(Shown(window.Used, percent), 100),
			Brush("Meter" + Level(Quota.Rate(window.Used, levels))),
			Join(Text.Lower(Text.Title(window)), resets),
			resets);
	}

	private static string Percent(double used, PercentMode percent) =>
		Math.Round(Shown(used, percent)).ToString(CultureInfo.InvariantCulture) + "%";
}
