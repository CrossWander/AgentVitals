using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Media;

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
internal sealed record ProviderView(
	string Name,
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
	Health Health)
{
	/// <summary>Точка на вкладке: у агента запас на исходе, даже когда смотришь на соседа.</summary>
	public bool Alert => Health is Health.Warning or Health.Critical;

	/// <summary>Для карточки по наведению — только окна, которые блокируют всю работу.</summary>
	public IReadOnlyList<CardView> LimitingCards => [.. Cards.Where(card => card.Limiting)];

	public bool HasError => Error.Length > 0;

	/// <summary>Ни статистики, ни сессий — как у Copilot: на их месте строка, почему там пусто.</summary>
	public bool Bare => Usage is null && Sessions is null;
}

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
	IReadOnlyList<ProviderView> Providers,
	string StatusText,
	IBrush StatusBrush,
	string LiveText,
	IBrush LiveBrush)
{
	public bool NoAgents => Providers.Count == 0;
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
	public static Vitals Build(IReadOnlyList<ProviderState> agents, IReadOnlyDictionary<string, HookLink> hooks, IReadOnlyList<Session> sessions, DateTime now, Thresholds levels, bool hide = false)
	{
		// Значок и карточка по наведению — по худшему агенту; шапка окна — по выбранной вкладке.
		var badge = Badge(agents, now, levels);
		var live = SessionWatcher.Overall(sessions);
		var focus = sessions.FirstOrDefault(session => session.State == live);
		var status = Overall(agents);

		return new(
			badge.Text,
			badge.Brush,
			[.. agents.Select(state => Provider(state, now, levels, hooks.TryGetValue(state.Provider.Name, out var link) ? link : null, sessions, hide))],
			status.Text,
			Brush(Level(status.Health)),
			focus is null ? "" : Text.Live(focus),
			Brush(Dot(live)));
	}

	/// <summary>Плашка по самому узкому окну агентов: исчерпано оно — «исчерпано», как на его карточке.</summary>
	private static (string Text, IBrush Brush, Health Health) Badge(IReadOnlyList<ProviderState> agents, DateTime now, Thresholds levels)
	{
		var health = Poller.Health(agents, now, levels);

		return (Poller.Tightest(agents, now) is { } used ? Text.Badge(used, levels) : Text.HealthName(health), Brush(Level(health)), health);
	}

	/// <summary>Строка статуса в карточке наведения: худший статус среди агентов; все в порядке — «все сервисы работают».</summary>
	internal static (string Text, Health Health) Overall(IReadOnlyList<ProviderState> agents)
	{
		var pages = agents.Where(state => state.Provider.Status is not null).ToList();

		if (pages.Count == 0)
		{
			return ("", Health.Unknown);
		}

		var worst = pages.MaxBy(state => Rank(state.Status?.Health ?? Health.Unknown))!;
		var health = worst.Status?.Health ?? Health.Unknown;

		return pages.Count > 1 && health == Health.Good
			? (Localization.Get("langStatusAllOk"), Health.Good)
			: (Text.Status(worst.Status, worst.Provider.Status!.Name), health);
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
	internal static ProviderView Provider(ProviderState state, DateTime now, Thresholds levels, HookLink? hooks = null, IReadOnlyList<Session>? sessions = null, bool hide = false)
	{
		List<CardView> cards = [];

		if (state.Snapshot is { } snapshot)
		{
			cards.AddRange(state.Windows(now).Select(window => Card(window, now, levels)));

			if (snapshot.Extra is { } extra)
			{
				cards.Add(Extra(extra, levels));
			}
		}

		var info = string.Join(" · ", new[] { state.Snapshot?.Plan, hide ? null : state.Snapshot?.Account }.Where(text => !string.IsNullOrEmpty(text)));

		var badge = Badge([state], now, levels);
		var target = hooks is null ? null : state.Provider.Hooks;
		List<Session> own = [.. (sessions ?? []).Where(session => session.Agent == state.Provider.Name)];

		return new(
			state.Provider.Name,
			info,
			Text.Ago(state.Updated, now),
			cards,
			state.Error is { } error ? Text.Error(error, state.Provider.Name) : "",
			state.Provider.Dashboard,
			state.Provider.Usage is null ? null : Usage(state.Days),
			state.Provider.Status is { } page ? Text.Status(state.Status, page.Name) : "",
			Brush(Level(state.Status?.Health ?? Health.Unknown)),
			state.Provider.Status?.Url,
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
			badge.Health);
	}

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

	private static CardView Card(QuotaWindow window, DateTime now, Thresholds levels)
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
			Math.Round(window.Used).ToString(CultureInfo.InvariantCulture),
			"%",
			Localization.Get("langUsed"),
			Math.Min(window.Used, 100),
			Brush("Meter" + Level(health)),
			Text.Until(window.ResetsAt, now),
			window.Limiting,
			pace,
			runout is { } at ? Text.Runout(at, now) : word is null ? "" : Localization.Get($"langPace{word}"),
			Brush(word switch
			{
				Pace.Ahead => "Warn",
				Pace.Spare => "Good",
				Pace.Even or null => "TextDim",
			}),
			pace is { } even ? Localization.Format("langPaceTip", Math.Round(even), Math.Round(window.Used)) : null);
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
