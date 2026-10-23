using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using AgentVitals.Core;
using AgentVitals.Core.Quotas;
using AgentVitals.Core.Sessions;

namespace AgentVitals;

/// <summary>Что главное для плашки и значка, по важности. Low — у показанного агента квота «критично».</summary>
internal enum WidgetState
{
	Waiting,
	Finished,
	Low,
	Working,
	Idle,
}

/// <summary>Всё, что рисуют три вида виджета, одним снимком.</summary>
/// <param name="Title">«Claude» в покое — первый агент, другого выбирает <see cref="Pick"/>; «Claude ждёт разрешения», «Claude закончил».</param>
/// <param name="Value">«46%» в режиме процентов; у «ждёт» и «закончил» и без данных — "".</param>
/// <param name="Detail">Сброс «2 ч 50 мин», без данных — «нет данных»; у «ждёт» — «проект · инструмент», у «закончил» — проект;
/// под глазом проектов нет.</param>
/// <param name="Fill">Полоска и кольцо, 0–100, в режиме процентов.</param>
/// <param name="Meter">Цвет квоты показанного агента: полоска 4 px, кольцо, полоска плашки.</param>
/// <param name="Dot">Цвет точки — общее состояние сессий, как у значка в трее.</param>
/// <param name="Agent">Чья вкладка откроется щелчком; null — агентов нет.</param>
/// <param name="LimitMeter">Цвет агента ближе всех к лимиту — полоска плашки «по очереди»: она не мигает со сменой агента.</param>
/// <param name="Waiting">Сессия, о которой «ждёт разрешения»: щелчок по плашке — сразу её окно; null — никто не ждёт.</param>
/// <param name="Finished">Сессия, о которой «закончил»: другая — новая вспышка, даже с той же надписью; null — такой нет.</param>
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
	Session? Waiting = null,
	Session? Finished = null)
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

	/// <summary>У показанного агента работает сессия — плашка и значок тихо движутся. «Ждёт» и «закончил» светятся сами.</summary>
	public bool Busy => !Loud && Agents.Any(agent => agent.Name == Agent && agent.Working);

	/// <summary>Настройка «Агент»: по очереди.</summary>
	public const string Rotation = "rotation";

	/// <summary>Настройка «Агент» у значка: все сразу в ряд.</summary>
	public const string All = "all";

	// «По очереди»: столько показывается один агент.
	private static readonly TimeSpan _slot = TimeSpan.FromSeconds(6);

	// Агент с квотой на исходе чередуется с выбранным: столько показывается каждый.
	private static readonly TimeSpan _alarm = TimeSpan.FromSeconds(10);

	/// <summary>Что показать по настройке «Агент»: выбранного (нет такого — первого), а у кого квота «критично» — по
	/// очереди с ним каждые 10 с; «По очереди» — всех с данными по 6 с; «Все сразу» — ряд. «Ждёт» и «закончил» — как есть.</summary>
	/// <param name="choice">Имя агента, <see cref="Rotation"/>, <see cref="All"/>; null — первый.</param>
	public WidgetView Pick(string? choice, DateTime now)
	{
		if (Loud || Agents.Count == 0)
		{
			return this;
		}

		if (choice == All)
		{
			return Agents.Any(agent => agent.Low) ? this with { State = WidgetState.Low } : this;
		}

		if (choice == Rotation)
		{
			return Rotate(now) is { } next ? With(next) : this;
		}

		var chosen = Agents.FirstOrDefault(agent => agent.Name == choice) ?? Agents[0];
		List<WidgetAgent> shown = [chosen, .. Agents.Where(agent => agent.Low && agent != chosen)];

		return With(shown[(int)(now.Ticks / _alarm.Ticks % shown.Count)]);
	}

	/// <summary>Агент «по очереди»: каждые 6 с следующий из тех, у кого есть данные; квота «критично» смену не останавливает —
	/// агент под 100% неделями держал бы её. Без данных ни у кого — null.</summary>
	public WidgetAgent? Rotate(DateTime now)
	{
		List<WidgetAgent> known = [.. Agents.Where(agent => agent.Known)];

		return known.Count == 0 ? null : known[(int)(now.Ticks / _slot.Ticks % known.Count)];
	}

	/// <summary>Тот же снимок с этим агентом в шапке; «мало квоты» — по самому агенту: у снимка его не бывает.</summary>
	internal WidgetView With(WidgetAgent agent) => this with
	{
		State = agent.Low ? WidgetState.Low : State,
		Title = agent.Name,
		Value = agent.Known ? agent.Value : "",
		Detail = agent.Known ? agent.Resets : Localization.Get("langWidgetNoData"),
		Fill = agent.Fill,
		Meter = agent.Meter,
		Agent = agent.Name,
	};
}

/// <summary>Агент в панели и в колонке на рабочем столе.</summary>
/// <param name="Value">«46%»; нет данных — «—».</param>
/// <param name="Window">«сессия · 2 ч 50 мин» — строка панели.</param>
/// <param name="Resets">«2 ч 50 мин» — под колонкой на рабочем столе.</param>
/// <param name="Low">Квота «критично».</param>
/// <param name="Working">У агента работает сессия.</param>
internal sealed record WidgetAgent(string Name, string Value, double Fill, IBrush Meter, string Window, string Resets, bool Low = false, bool Working = false)
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

	/// <summary>Что показать виджету: агентов и сессии, а важнее агента — сессию, которая ждёт человека или только что
	/// закончила ход. Какого агента показать в шапке, решает <see cref="WidgetView.Pick"/> по настройке; снимок — с первым.</summary>
	/// <param name="agents">Видимые агенты в порядке вкладок.</param>
	/// <param name="finished">Сессия, чей долгий ход закончился меньше 5 с назад; null — такой нет.</param>
	/// <param name="hide">Показывают экран: проектов, веток и инструментов не видно.</param>
	internal static WidgetView Widget(IReadOnlyList<AgentState> agents, IReadOnlyList<Session> sessions, Session? finished, DateTime now, Thresholds levels, PercentMode percent, bool hide)
	{
		List<string> order = [.. agents.Select(state => state.Agent.Name)];
		var waiting = sessions
			.Where(session => session.State == SessionState.Waiting)
			.OrderBy(session => order.IndexOf(session.Agent) is >= 0 and var place ? place : int.MaxValue)
			.FirstOrDefault();

		var state = waiting is not null ? WidgetState.Waiting
			: finished is not null ? WidgetState.Finished
			: sessions.Any(session => session.State == SessionState.Working) ? WidgetState.Working
			: WidgetState.Idle;

		var title = state switch
		{
			WidgetState.Waiting => Localization.Format("langToastWaiting", waiting!.Agent),
			WidgetState.Finished => Localization.Format("langToastDone", finished!.Agent),
			_ => Localization.Get("langNoAgents"),
		};

		var detail = state switch
		{
			WidgetState.Waiting => hide ? "" : Join(waiting!.Folder, waiting.Tool),
			WidgetState.Finished => hide ? "" : finished!.Folder,
			_ => "",
		};

		List<WidgetAgent> rows = [.. agents.Select(agent => WidgetRow(agent, now, levels, percent) with
		{
			Working = sessions.Any(session => session.Agent == agent.Agent.Name && session.State == SessionState.Working),
		})];

		var view = new WidgetView(
			state,
			title,
			"",
			detail,
			0,
			Brush("MeterMuted"),
			Brush(Dot(SessionWatcher.Overall(sessions))),
			(waiting ?? finished)?.Agent ?? rows.FirstOrDefault()?.Name,
			rows,
			[.. sessions
				.OrderByDescending(session => session.State)
				.Take(_widgetSessions)
				.Select(session => new WidgetSession(
					hide ? session.Agent : session.Folder,
					hide ? "" : session.Branch ?? "",
					Text.Session(hide ? session with { Tool = null } : session, now),
					Brush(Dot(session.State)),
					session))],
			// Как у значка в трее: самое узкое окно всех агентов.
			Brush("Meter" + Level(Poller.Health(agents, now, levels))),
			waiting,
			finished);

		// Снимок — с первым агентом; «мало квоты» решает выбор в Pick, а не снимок.
		return view.Loud || rows.Count == 0 ? view : view.With(rows[0]) with { State = state };
	}

	private static WidgetAgent WidgetRow(AgentState agent, DateTime now, Thresholds levels, PercentMode percent)
	{
		// Без входа окна старые: как и для значка, такой агент не в счёт.
		if (agent.Tightest(now) is null || Quota.Bottleneck(agent.Windows(now)) is not { } window)
		{
			return new(agent.Agent.Name, "—", 0, Brush("MeterMuted"), "", "");
		}

		var resets = Text.Short(window.ResetsAt, now);
		var health = Quota.Rate(window.Used, levels);

		return new(
			agent.Agent.Name,
			Percent(window.Used, percent),
			Math.Min(Shown(window.Used, percent), 100),
			Brush("Meter" + Level(health)),
			Join(Text.Lower(Text.Title(window)), resets),
			resets,
			health == Health.Critical);
	}
}
