using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using AgentVitals.Core;
using AgentVitals.Core.Quotas;
using AgentVitals.Core.Sessions;
using AgentVitals.Core.Usage;

namespace AgentVitals;

/// <summary>Карточка окна квоты (или доп. расхода) в том виде, в каком её рисуют окна.</summary>
internal sealed record CardView(
	string Title,
	Geometry? Icon,
	IBrush IconBrush,
	string Badge,
	IBrush BadgeBrush,
	string Value,
	string Unit,
	string Note,
	double Fill,
	IBrush Meter,
	string Resets,
	bool Limiting,
	double? Pace = null,
	string PaceWord = "",
	IBrush? PaceBrush = null,
	string? PaceTip = null)
{
	/// <summary>Метка ровного темпа на полоске: где был бы расход, если тратить равномерно.</summary>
	public bool HasPace => Pace is not null;

	/// <summary>Подпись у числа в окне: темп, если он известен, иначе «использовано».</summary>
	public string Caption => PaceWord.Length > 0 ? PaceWord : Note;

	// Пустая кисть сделала бы подпись невидимой: у доплаты темпа нет.
	public IBrush CaptionBrush => PaceBrush ?? Views.Brush("TextDim");

	/// <summary>Нижняя строка в карточке наведения: там шире, темп встаёт после сброса.</summary>
	public string Footer => PaceWord.Length > 0 && Resets.Length > 0 ? $"{Resets} · {PaceWord}" : Resets;

	public string Display => Value + Unit;
}

/// <param name="StatusUrl">Страница статуса агента; null — её нет, и строки статуса тоже.</param>
/// <param name="Sessions">Живые сессии агента; null — hooks у агента нет, и карточки тоже.</param>
/// <param name="HealthText">Плашка в шапке, пока выбрана эта вкладка: по самому узкому окну этого агента.</param>
/// <param name="Plan">Тариф аккаунта в работе: в строке под вкладками он жирным.</param>
/// <param name="Info">Остальное в той строке: кто вошёл, а у нескольких аккаунтов — ещё «в работе».</param>
internal sealed record AgentView(
	string Name,
	string? Plan,
	string Info,
	string Updated,
	IReadOnlyList<CardView> Cards,
	string Error,
	string? Dashboard,
	UsageView? Usage,
	string StatusText,
	IBrush StatusBrush,
	string? StatusUrl,
	string SessionsTitle,
	IReadOnlyList<SessionView>? Sessions,
	string SessionsHint,
	string HealthText,
	IBrush HealthBrush,
	Health Health,
	IReadOnlyList<AccountView> Accounts,
	string AccountsTitle,
	string AccountsFoot)
{
	/// <summary>Аккаунтов у агента два и больше: карточка «Аккаунты», а «Сегодня» — общее на все.</summary>
	public bool Shared => Accounts.Count > 1;

	/// <summary>Точка на вкладке: у агента запас на исходе, даже когда смотришь на соседа.</summary>
	public bool Alert => Health is Health.Warning or Health.Critical;

	/// <summary>Для карточки по наведению — только окна, которые блокируют всю работу.</summary>
	public IReadOnlyList<CardView> LimitingCards => [.. Cards.Where(card => card.Limiting)];

	public bool HasError => Error.Length > 0;

	/// <summary>Ни статистики, ни сессий — как у Copilot: на их месте строка, почему там пусто.</summary>
	public bool Bare => Usage is null && Sessions is null;
}

/// <summary>Аккаунт агента на вкладке: строка в карточке «Аккаунты» и то, что показать сверху, когда он выбран.</summary>
/// <param name="Selected">Показан в карточках сверху; ставит поповер — выбор живёт, пока окно открыто.</param>
/// <param name="Line">«Team · сейчас»: тариф и возраст цифр.</param>
/// <param name="Meters">Ограничивающие окна полосками, в порядке карточек агента.</param>
/// <param name="Plan">Тариф: пока аккаунт выбран, он в строке под вкладками жирным.</param>
/// <param name="Info">Остальное в той строке: «Личный · в работе».</param>
/// <param name="Hint">Полоса над карточками у запомненного: это последние цифры; у аккаунта в работе — null.</param>
internal sealed record AccountView(
	string Key,
	string Name,
	bool InUse,
	bool Selected,
	string Line,
	string Badge,
	IBrush BadgeBrush,
	IReadOnlyList<CardView> Meters,
	IReadOnlyList<CardView> Cards,
	string? Plan,
	string Info,
	string Updated,
	string? Hint)
{
	public IBrush Dot => Views.Brush(InUse ? "Good" : "TextFaint");

	public bool HasHint => Hint is not null;
}

/// <summary>Агент в списке окна «Агенты».</summary>
/// <param name="Number">Место вкладки — оно же Ctrl+N; "" — вкладки нет.</param>
/// <param name="Found">Установлен: у ненайденного нет ни номера, ни переключателя.</param>
/// <param name="Shown">Не выключен — переключатель «показывать».</param>
internal sealed record AgentRowView(string Name, string Number, string State, bool Found, bool Shown, bool Picked = false)
{
	/// <summary>Слева у строки: номер вкладки, у выключенного — «—», у ненайденного — ничего.</summary>
	public string Mark => Found && !Shown ? "—" : Number;

	/// <summary>Выключенный и ненайденный — бледнее: вкладки у них нет.</summary>
	public bool Dim => !Found || !Shown;
}

/// <summary>Окно аккаунта в строке таблицы: «Сессия 100%»; израсходованное подсвечено.</summary>
internal sealed record AccountNumber(string Title, string Percent, bool Spent);

/// <summary>Аккаунт в окне «Агенты»: своё имя, тариф и последние цифры.</summary>
/// <param name="Name">Своё имя; "" — не задано, вместо него виден Placeholder — имя по умолчанию.</param>
/// <param name="Email">Почта для подсказки над именем; null — глаз её прячет.</param>
/// <param name="Note">У занятого запомненного — когда он освободится; иначе null.</param>
internal sealed record AccountEditView(string Key, string Name, string Placeholder, string? Email, string Plan, IReadOnlyList<AccountNumber> Numbers, string Age, string? Note, bool InUse);

/// <param name="Prompt">Первый запрос сессии; null — ещё не известен или экран показывают другим.</param>
/// <param name="Namesake">В той же папке идёт другая сессия агента: у обеих вместо ветки — запрос, иначе строк не различить.</param>
/// <param name="Session">Сама сессия: по щелчку на строке поднимается окно, где она идёт.</param>
internal sealed record SessionView(string Folder, string Branch, string? Prompt, bool Namesake, string State, IBrush Dot, Session Session)
{
	/// <summary>Вторая подпись строки: ветка, а у тёзок — первый запрос.</summary>
	public string Detail => Namesake && Prompt is not null ? Prompt : Branch;

	/// <summary>Подсказка над строкой: длинные имя папки, ветка и запрос в строке обрезаны троеточием.</summary>
	public string Tip => string.Join("\n", new[] { Folder, Branch, Prompt }.Where(text => !string.IsNullOrEmpty(text)));
}

/// <summary>Что откладывать по высоте столбиков графика.</summary>
internal enum ChartMode
{
	Cost,
	Tokens,
	Cache,
	Models,
}

/// <summary>Что значат проценты квот: сколько израсходовано или сколько осталось. Одинаково во всей программе.</summary>
internal enum PercentMode
{
	Used,
	Left,
}

/// <summary>Слой столбика: высота в точках и цвет.</summary>
internal sealed record BarPart(double Height, IBrush Brush);

/// <summary>Пункт легенды графика.</summary>
internal sealed record LegendItem(IBrush Brush, string Text);

/// <summary>Столбик дня: слои снизу вверх.</summary>
internal sealed record BarView(DateOnly Date, IReadOnlyList<BarPart> Parts, double Opacity, string Line)
{
	/// <summary>Слои сверху вниз — в таком порядке их кладёт StackPanel.</summary>
	public IReadOnlyList<BarPart> Stack => [.. Parts.Reverse()];
}

/// <summary>График в выбранной мере: столбики, шкала сверху вниз, даты под столбиками, легенда и сумма за 30 дней.</summary>
internal sealed record ChartView(
	IReadOnlyList<BarView> Bars,
	IReadOnlyList<string> AxisY,
	IReadOnlyList<(int Index, string Text)> AxisX,
	IReadOnlyList<LegendItem> Legend,
	string Total);

/// <summary>Блок статистики вкладки: «Сегодня» и дни для графика.</summary>
internal sealed record UsageView(
	string Cost,
	string CostNote,
	string CostYesterday,
	string Tokens,
	string TokensNote,
	string TokensYesterday,
	string WorkTime,
	string WorkTimeYesterday,
	IReadOnlyList<UsageDay> Days);

/// <summary>Всё, что показывают окна, одним снимком.</summary>
internal sealed record Vitals(
	string HealthText,
	IBrush HealthBrush,
	IReadOnlyList<AgentView> Agents,
	string StatusText,
	IBrush StatusBrush,
	string LiveText,
	IBrush LiveBrush)
{
	public bool NoAgents => Agents.Count == 0;
}

/// <summary>Собирает представления из данных опроса и сессий. Кисти — из палитры текущей темы.</summary>
internal static class Views
{
	/// <param name="agents">Установленные агенты.</param>
	/// <param name="hooks">Подключены ли hooks — по агентам, у которых они есть.</param>
	/// <param name="sessions">Живые сессии всех агентов.</param>
	/// <param name="now">Момент снимка: от него «обновлено N мин назад» и «сброс через».</param>
	/// <param name="levels">Пороги из настроек.</param>
	/// <param name="hide">Показывают экран: почты аккаунтов и запросов сессий не видно.</param>
	/// <param name="names">Свои имена аккаунтов: «агент/ключ» → имя.</param>
	/// <param name="percent">Что значат проценты: израсходовано или осталось.</param>
	public static Vitals Build(IReadOnlyList<AgentState> agents, IReadOnlyDictionary<string, HookLink> hooks, IReadOnlyList<Session> sessions, DateTime now, Thresholds levels, bool hide = false, IReadOnlyDictionary<string, string>? names = null, PercentMode percent = PercentMode.Used)
	{
		// Значок и карточка по наведению — по худшему агенту; шапка окна — по выбранной вкладке.
		var badge = Badge(agents, now, levels);
		var live = SessionWatcher.Overall(sessions);
		var focus = sessions.FirstOrDefault(session => session.State == live);
		var status = Overall(agents);

		return new(
			badge.Text,
			badge.Brush,
			[.. agents.Select(state => Agent(state, now, levels, hooks.TryGetValue(state.Agent.Name, out var link) ? link : null, sessions, hide, names, percent))],
			status.Text,
			Brush(Level(status.Health)),
			focus is null ? "" : Text.Live(focus),
			Brush(Dot(live)));
	}

	/// <summary>Плашка по самому узкому окну агентов: исчерпано оно — «исчерпано», как на его карточке.</summary>
	private static (string Text, IBrush Brush, Health Health) Badge(IReadOnlyList<AgentState> agents, DateTime now, Thresholds levels)
	{
		var health = Poller.Health(agents, now, levels);

		return (Poller.Tightest(agents, now) is { } used ? Text.Badge(used, levels) : Text.HealthName(health), Brush(Level(health)), health);
	}

	/// <summary>Строка статуса в карточке наведения: худший статус среди агентов; все в порядке — «все сервисы работают».</summary>
	internal static (string Text, Health Health) Overall(IReadOnlyList<AgentState> agents)
	{
		var pages = agents.Where(state => state.Agent.Status is not null).ToList();

		if (pages.Count == 0)
		{
			return ("", Health.Unknown);
		}

		var worst = pages.MaxBy(state => Rank(state.Status?.Health ?? Health.Unknown))!;
		var health = worst.Status?.Health ?? Health.Unknown;

		return pages.Count > 1 && health == Health.Good
			? (Localization.Get("langStatusAllOk"), Health.Good)
			: (Text.Status(worst.Status, worst.Agent.Status!.Name), health);
	}

	// Тревога важнее неизвестности, неизвестность — важнее «всё в порядке».
	private static int Rank(Health health) => health switch
	{
		Health.Good => 0,
		Health.Unknown => 1,
		Health.Warning => 2,
		Health.Critical => 3,
	};

	/// <param name="hooks">Подключены ли hooks агента; null — у агента их нет.</param>
	/// <param name="hide">Показывают экран: почты аккаунта и запросов сессий не видно.</param>
	/// <param name="names">Свои имена аккаунтов: «агент/ключ» → имя.</param>
	/// <param name="percent">Что значат проценты: израсходовано или осталось.</param>
	internal static AgentView Agent(AgentState state, DateTime now, Thresholds levels, HookLink? hooks = null, IReadOnlyList<Session>? sessions = null, bool hide = false, IReadOnlyDictionary<string, string>? names = null, PercentMode percent = PercentMode.Used)
	{
		var cards = Cards(state.Snapshot, state.Windows(now), now, levels, percent);
		var accounts = state.Accounts;
		var named = Names(state.Agent.Name, accounts, names ?? new Dictionary<string, string>(), hide);

		// Аккаунт в работе — первым, остальные — в порядке появления: строки не прыгают.
		List<AccountView> rows = accounts.Count < 2 ? [] : [.. accounts
			.OrderBy(account => account.Key == state.Key ? 0 : 1)
			.Select(account => Account(state, account, named[account.Key], now, levels, percent))];

		// Один аккаунт: «Аккаунт 1» ничего не говорит — под глазом своё имя или ничего.
		var custom = state.Key is { } key ? names?.GetValueOrDefault($"{state.Agent.Name}/{key}") : null;
		var info = rows.Count > 0 ? rows[0].Info : Join(hide ? custom : state.Snapshot?.Account ?? custom);

		var badge = Badge([state], now, levels);
		var target = hooks is null ? null : state.Agent.Hooks;
		List<Session> own = [.. (sessions ?? []).Where(session => session.Agent == state.Agent.Name)];

		return new(
			state.Agent.Name,
			state.Snapshot?.Plan,
			info,
			state.Pending ? Localization.Get("langUpdating") : Text.Ago(state.Updated, now),
			cards,
			state.Error is { } error ? Text.Error(error, state.Agent.Name) : "",
			state.Agent.Dashboard,
			state.Agent.Usage is null ? null : Usage(state.Days),
			state.Agent.Status is { } page ? Text.Status(state.Status, page.Name) : "",
			Brush(Level(state.Status?.Health ?? Health.Unknown)),
			state.Agent.Status?.Url,
			target is null ? "" : Localization.Format("langSessions", target.Title.ToUpper(Localization.Culture)),
			target is null ? null : [.. own.Select(session => new SessionView(
				session.Folder,
				session.Branch ?? "",
				hide ? null : session.Prompt,
				own.Count(other => other.Folder == session.Folder) > 1,
				Text.Session(session, now),
				Brush(Dot(session.State)),
				session))],
			target is null ? ""
				: hooks != HookLink.On ? Localization.Format("langHooksHint", target.Title)
				: own.Count == 0 ? Localization.Get("langNoSessions")
				: "",
			badge.Text,
			badge.Brush,
			badge.Health,
			rows,
			Localization.Format("langAccounts", rows.Count),
			Localization.Format("langAccountsFoot", Text.Login(state.Agent.Name)));
	}

	/// <summary>Агенты в порядке вкладок, ненайденные — в конце. Номер — место среди вкладок: Ctrl+N.</summary>
	internal static IReadOnlyList<AgentRowView> AgentRows(IReadOnlyList<AgentState> agents)
	{
		List<string> tabs = [.. agents.Where(state => state.Visible).Select(state => state.Agent.Name)];

		return [.. agents
			.OrderBy(state => state.Installed ? 0 : 1)
			.Select(state => new AgentRowView(
				state.Agent.Name,
				tabs.IndexOf(state.Agent.Name) is >= 0 and var place ? (place + 1).ToString(CultureInfo.InvariantCulture) : "",
				Text.AgentLine(state),
				state.Installed,
				!state.Off))];
	}

	/// <summary>Аккаунты агента для окна «Агенты»: аккаунт в работе первым, как в карточке на вкладке.</summary>
	internal static IReadOnlyList<AccountEditView> AccountEdits(AgentState state, IReadOnlyDictionary<string, string> custom, bool hide, DateTime now, PercentMode percent = PercentMode.Used)
	{
		var plain = Names(state.Agent.Name, state.Accounts, new Dictionary<string, string>(), hide);

		return [.. state.Accounts
			.OrderBy(account => account.Key == state.Key ? 0 : 1)
			.Select(account => new AccountEditView(
				account.Key,
				custom.GetValueOrDefault($"{state.Agent.Name}/{account.Key}") ?? "",
				plain[account.Key],
				hide ? null : account.Snapshot?.Account,
				account.Snapshot?.Plan ?? "",
				[.. account.Windows(now).Where(window => window.Limiting).Select(window => new AccountNumber(
					Text.Title(window),
					Math.Round(Shown(window.Used, percent)).ToString(CultureInfo.InvariantCulture) + "%",
					Math.Round(window.Used) >= 100))],
				Text.Age(account.Updated, now),
				account.Key != state.Key && account.Availability(now) is (Availability.Busy, { } blocker) ? Text.Frees(blocker, now) : null,
				account.Key == state.Key))];
	}

	/// <summary>Карточки квот снимка: окна на этот момент и доплата сверх тарифа.</summary>
	private static List<CardView> Cards(Snapshot? snapshot, IReadOnlyList<QuotaWindow> windows, DateTime now, Thresholds levels, PercentMode percent)
	{
		List<CardView> cards = [.. windows.Select(window => Card(window, now, levels, percent))];

		if (snapshot?.Extra is { } extra)
		{
			cards.Add(Extra(extra, levels));
		}

		return cards;
	}

	private static AccountView Account(AgentState state, Account account, string name, DateTime now, Thresholds levels, PercentMode percent)
	{
		var inUse = account.Key == state.Key;
		var pending = inUse && state.Pending;
		var windows = account.Windows(now);
		var (availability, blocker) = account.Availability(now);
		var plan = account.Snapshot?.Plan;

		return new(
			account.Key,
			name,
			inUse,
			false,
			Join(plan, pending ? Localization.Get("langAgeUpdating") : Text.Age(account.Updated, now)),
			inUse ? Localization.Get("langAccountInUse") : Text.AccountBadge(availability, blocker, now),
			Brush(!inUse && availability == Availability.Busy ? "BadgeBad" : "BadgeGood"),
			[.. windows.Where(window => window.Limiting).Select(window => Card(window, now, levels, percent) with { Title = Text.Title(window) })],
			Cards(account.Snapshot, windows, now, levels, percent),
			plan,
			Join(name, Localization.Get(inUse ? "langInUse" : "langRemembered")),
			pending ? Localization.Get("langUpdating") : Text.Ago(account.Updated, now),
			inUse ? null : Localization.Format("langRememberedHint", name, Text.Login(state.Agent.Name)));
	}

	/// <summary>Как назвать аккаунты агента: без глаза — почта (у Copilot — логин), под глазом и без почты — своё имя,
	/// а без него «Аккаунт N» по порядку появления. Одна почта у двух — с тарифом, совпал и он — ещё с номером.</summary>
	/// <param name="custom">Свои имена: «агент/ключ» → имя.</param>
	/// <returns>Ключ аккаунта → имя, в порядке появления.</returns>
	internal static IReadOnlyDictionary<string, string> Names(string agent, IReadOnlyList<Account> accounts, IReadOnlyDictionary<string, string> custom, bool hide)
	{
		var plain = accounts.Select((account, index) => (
			Account: account,
			Own: custom.TryGetValue($"{agent}/{account.Key}", out var own) ? own : null,
			Email: hide ? null : account.Snapshot?.Account,
			Number: index + 1)).ToList();

		Dictionary<string, string> names = [];

		foreach (var item in plain)
		{
			names[item.Account.Key] = item switch
			{
				{ Email: { Length: > 0 } email } => Unique(email, item.Account, [.. plain.Where(other => other.Email == email).Select(other => other.Account)]),
				{ Own: { } own } => own,
				_ => Localization.Format("langAccountNumber", item.Number),
			};
		}

		return names;
	}

	private static string Unique(string email, Account account, IReadOnlyList<Account> same)
	{
		if (same.Count == 1)
		{
			return email;
		}

		var plan = account.Snapshot?.Plan;
		List<Account> twins = [.. same.Where(other => other.Snapshot?.Plan == plan)];

		return twins.Count == 1 ? $"{email} · {plan}" : $"{email} · {plan} · {twins.IndexOf(account) + 1}";
	}

	private static string Join(params string?[] parts) => string.Join(" · ", parts.Where(part => !string.IsNullOrEmpty(part)));

	public const double ChartHeight = 90;

	/// <summary>«Сегодня» — последний день, «вчера» — предпоследний.</summary>
	internal static UsageView Usage(IReadOnlyList<UsageDay>? days)
	{
		// Первое чтение логов идёт секунды: до него — «считаю…», а не нули.
		if (days is not { Count: >= 2 })
		{
			return new("…", Localization.Get("langCounting"), "", "…", "", "", "…", "", []);
		}

		var today = days[^1];
		var yesterday = days[^2];
		var costNote = today.Cost is null ? "langCostMissing" : today.Unpriced ? "langCostPartial" : "langCostNote";

		return new(
			Text.Cost(today.Cost),
			Localization.Get(costNote),
			Localization.Format("langYesterday", Text.Cost(yesterday.Cost)),
			Text.Tokens(today.Total),
			today.Total == 0 ? "" : Localization.Format("langFromCache", Math.Round(100.0 * today.CacheRead / today.Total)),
			Localization.Format("langYesterday", Text.Tokens(yesterday.Total)),
			Text.Span(today.WorkTime, least: 0),
			Localization.Format("langYesterday", Text.Span(yesterday.WorkTime, least: 0)),
			days);
	}

	/// <summary>Всё, что рисует график, — за один подсчёт шкалы.</summary>
	internal static ChartView Chart(IReadOnlyList<UsageDay> days, ChartMode mode)
	{
		var ceiling = Ceiling(Top(days, mode));

		// Без дней «$0.00 за месяц» было бы неправдой: дней просто ещё нет.
		return new(Bars(days, mode, ceiling), AxisY(ceiling, mode), AxisX(days), Legend(mode, days), days.Count == 0 ? "" : Total(days, mode));
	}

	/// <summary>Столбики в выбранной мере: во всю высоту — верх шкалы, сегодняшний ярче.</summary>
	private static IReadOnlyList<BarView> Bars(IReadOnlyList<UsageDay> days, ChartMode mode, double ceiling)
	{
		var layers = Layers(mode, days);
		var scale = ceiling > 0 ? ChartHeight / ceiling : 0;

		return [.. days.Select((day, index) => new BarView(
			day.Date,
			[.. layers.Select(layer => new BarPart(layer.Value(day) * scale, Brush(layer.Brush)))],
			index == days.Count - 1 ? 1 : 0.6,
			Text.DayLine(day)))];
	}

	/// <summary>Самый большой день в выбранной мере.</summary>
	private static double Top(IReadOnlyList<UsageDay> days, ChartMode mode) =>
		days.Select(day => Layers(mode, days).Sum(layer => layer.Value(day))).DefaultIfEmpty(0).Max();

	/// <summary>Легенда под графиком: по пункту на слой.</summary>
	internal static IReadOnlyList<LegendItem> Legend(ChartMode mode, IReadOnlyList<UsageDay> days) =>
		[.. Layers(mode, days).Select(layer => new LegendItem(Brush(layer.Brush), layer.Name))];

	// Слои меры снизу вверх: цвет, подпись в легенде и значение дня. По ним — и столбики, и легенда, и шкала.
	// Семейства моделей — только те, что стоили денег за эти дни: у Claude нет Luna, у Codex — Opus.
	private static IReadOnlyList<(string Brush, string Name, Func<UsageDay, double> Value)> Layers(ChartMode mode, IReadOnlyList<UsageDay> days) => mode switch
	{
		ChartMode.Cost => [("Blue", Localization.Get("langCostNote"), day => (double)(day.Cost ?? 0))],
		ChartMode.Tokens => [("Blue", Localization.Get("langInput"), day => day.Input), ("Good", Localization.Get("langOutput"), day => day.Output)],
		ChartMode.Cache => [("Blue", Localization.Get("langCacheWrite"), day => day.CacheWrite), ("Good", Localization.Get("langCacheRead"), day => day.CacheRead)],
		ChartMode.Models => [.. Enum.GetValues<ModelFamily>()
			.Where(family => days.Any(day => (day.Models?.All[(int)family] ?? 0) > 0))
			.Select(family => (
				Color(family),
				family == ModelFamily.Other ? Localization.Get("langOther") : family.ToString(),
				(Func<UsageDay, double>)(day => (double)(day.Models?.All[(int)family] ?? 0))))],
	};

	// Цвета по размеру модели: флагман, средняя, малая — у Claude и у Codex одни и те же.
	private static string Color(ModelFamily family) => family switch
	{
		ModelFamily.Opus or ModelFamily.Astra => "Blue",
		ModelFamily.Sonnet or ModelFamily.Sol => "Good",
		ModelFamily.Haiku or ModelFamily.Luna => "Warn",
		ModelFamily.Fable => "Bad",
		ModelFamily.Other => "TextFaint",
	};

	/// <summary>Верх шкалы: две «круглые» ступени, как $0 / $80 / $160, не ниже самого большого дня.</summary>
	internal static double Ceiling(double top)
	{
		if (top <= 0)
		{
			return 0;
		}

		var half = top / 2;
		var power = Math.Pow(10, Math.Floor(Math.Log10(half)));

		return 2 * new[] { 1, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10 }.Select(step => step * power).First(step => step >= half);
	}

	/// <summary>Подписи оси Y сверху вниз: верх шкалы, середина, ноль.</summary>
	internal static IReadOnlyList<string> AxisY(double ceiling, ChartMode mode)
	{
		string Label(double value) => mode is ChartMode.Cost or ChartMode.Models
			? "$" + value.ToString("0.##", CultureInfo.InvariantCulture)
			: value == 0 ? "0" : Text.Tokens((long)value);

		return ceiling > 0 ? [Label(ceiling), Label(ceiling / 2), Label(0)] : ["", "", Label(0)];
	}

	/// <summary>Подписи оси X: каждый седьмой день от первого — номер столбика и дата.</summary>
	internal static IReadOnlyList<(int Index, string Text)> AxisX(IReadOnlyList<UsageDay> days) =>
		[.. days.Select((day, index) => (Index: index, day.Date)).Where(item => item.Index % 7 == 0)
			.Select(item => (item.Index, item.Date.ToString("d MMM", Localization.Culture)))];

	/// <summary>Сумма за 30 дней в выбранной мере — в углу графика.</summary>
	internal static string Total(IReadOnlyList<UsageDay> days, ChartMode mode) => mode switch
	{
		ChartMode.Cost or ChartMode.Models => Text.Cost(days.All(day => day.Cost is not null) ? days.Sum(day => day.Cost!.Value) : null),
		ChartMode.Tokens => Text.Tokens(days.Sum(day => day.Input + day.Output)),

		// Без цен экономию не посчитать — тогда только токены.
		ChartMode.Cache => Text.Tokens(days.Sum(day => day.CacheWrite + day.CacheRead))
			+ (days.Any(day => day.Saved is not null) ? " · " + Localization.Format("langCacheSaved", Text.Cost(days.Sum(day => day.Saved ?? 0))) : ""),
	};

	/// <summary>Процент для показа: израсходованный или остаток, не меньше нуля. Уровни и цвета считаются по израсходованному.</summary>
	internal static double Shown(double used, PercentMode percent) => percent == PercentMode.Left ? Math.Max(0, 100 - used) : used;

	private static CardView Card(QuotaWindow window, DateTime now, Thresholds levels, PercentMode percent)
	{
		var health = Quota.Rate(window.Used, levels);
		var (icon, tint) = window.Kind switch
		{
			WindowKind.Session => ("IconBolt", "IconSession"),
			WindowKind.Week or WindowKind.Day or WindowKind.Month => ("IconCalendar", "IconWeek"),
			WindowKind.WeekModel or WindowKind.Model or WindowKind.MonthFeature => ("IconSpark", "IconModel"),
		};

		var pace = Quota.Expected(window, now);
		Pace? word = pace is { } expected ? Quota.Pace(window.Used, expected) : null;

		// С опережением — сразу когда кончится: это и есть то, что нужно знать.
		var runout = Quota.Runout(window, now);

		return new(
			Text.Title(window).ToUpper(Localization.Culture),
			Shape(icon),
			Brush(tint),
			Text.Badge(window.Used, levels),
			Brush("Badge" + Level(health)),
			Math.Round(Shown(window.Used, percent)).ToString(CultureInfo.InvariantCulture),
			"%",
			Localization.Get(percent == PercentMode.Left ? "langLeft" : "langUsed"),
			Math.Min(Shown(window.Used, percent), 100),
			Brush("Meter" + Level(health)),
			Text.Until(window.ResetsAt, now),
			window.Limiting,

			// В режиме «осталось» полоска тает справа налево — и метка ровного темпа зеркальна.
			pace is { } mark ? Shown(mark, percent) : null,
			runout is { } at ? Text.Runout(at, now) : word is null ? "" : Localization.Get($"langPace{word}"),
			Brush(word switch
			{
				Pace.Ahead => "Warn",
				Pace.Spare => "Good",
				Pace.Even or null => "TextDim",
			}),
			pace is { } even
				? Localization.Format(percent == PercentMode.Left ? "langPaceTipLeft" : "langPaceTip", Math.Round(Shown(even, percent)), Math.Round(Shown(window.Used, percent)))
				: null);
	}

	private static CardView Extra(ExtraUsage extra, Thresholds levels)
	{
		var fill = extra.Limit is { } limit && limit > 0 ? (double)(extra.Used / limit * 100) : 0;

		return new(
			Localization.Get(extra.Team ? "langExtraTeam" : "langExtra").ToUpper(Localization.Culture),
			Shape("IconCoin"),
			Brush("IconExtra"),
			"",
			Brushes.Transparent,
			Text.Money(extra.Used, extra.Currency),
			"",
			extra.Limit is { } ceiling ? Localization.Format("langExtraOf", Text.Money(ceiling, extra.Currency)) : Localization.Get("langExtraSpent"),
			Math.Min(fill, 100),
			Brush("Meter" + Level(Quota.Rate(fill, levels))),
			"",
			false);
	}

	// Цвет состояния в палитре; с приставкой «Badge» и «Meter» — его вариант для бейджа и полоски.
	private static string Level(Health health) => health switch
	{
		Health.Unknown => "Muted",
		Health.Good => "Good",
		Health.Warning => "Warn",
		Health.Critical => "Bad",
	};

	private static string Dot(SessionState state) => state switch
	{
		SessionState.Idle => "Muted",
		SessionState.Working => "Good",
		SessionState.Waiting => "Blue",
	};

	internal static IBrush Brush(string key) => Find(key) as IBrush ?? Brushes.Gray;

	private static Geometry? Shape(string key) => Find(key) as Geometry;

	private static object? Find(string key) =>
		Application.Current is { } application && application.TryGetResource(key, application.ActualThemeVariant, out var value)
			? value
			: null;
}
