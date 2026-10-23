using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace AgentVitals.Tests;

public class ViewsTests
{
	private static readonly Thresholds _levels = new(75, 90);

	[Fact]
	public void EachTabShowsItsOwnUpdateTime() // у Codex со вчерашними данными не должно быть «только что»
	{
		var now = DateTime.UtcNow;
		var claude = new AgentState(new ClaudeAgent()) { Updated = now };
		var codex = new AgentState(new CodexAgent());

		Assert.Equal("langUpdatedNow", Views.Agent(claude, now, _levels).Updated);
		Assert.Equal("", Views.Agent(codex, now, _levels).Updated);
	}

	[Theory]
	[InlineData("Codex", "Codex")] // появился Gemini — выбор не съехал на соседа
	[InlineData("Gemini", "Claude")] // выбранный пропал — первый
	[InlineData(null, "Claude")]
	public void TabSelectionFollowsTheName(string? selected, string expected) =>
		Assert.Equal(expected, Popover.Keep(selected, ["Claude", "Codex"]));

	[Theory]
	[InlineData(Avalonia.Input.Key.D1, Avalonia.Input.KeyModifiers.Control, 0)]
	[InlineData(Avalonia.Input.Key.NumPad3, Avalonia.Input.KeyModifiers.Control, 2)]
	[InlineData(Avalonia.Input.Key.D2, Avalonia.Input.KeyModifiers.None, null)] // без Ctrl цифра — просто цифра
	[InlineData(Avalonia.Input.Key.D0, Avalonia.Input.KeyModifiers.Control, null)]
	public void CtrlDigitPicksTab(Avalonia.Input.Key key, Avalonia.Input.KeyModifiers modifiers, int? tab) =>
		Assert.Equal(tab, Popover.TabKey(key, modifiers));

	[Theory]
	[InlineData(Avalonia.Input.Key.F5, Avalonia.Input.KeyModifiers.None, true)]
	[InlineData(Avalonia.Input.Key.R, Avalonia.Input.KeyModifiers.Control, true)]
	[InlineData(Avalonia.Input.Key.R, Avalonia.Input.KeyModifiers.None, false)]
	public void F5OrCtrlRRefreshes(Avalonia.Input.Key key, Avalonia.Input.KeyModifiers modifiers, bool refresh) =>
		Assert.Equal(refresh, Popover.RefreshKey(key, modifiers));

	[Fact]
	public void HeaderNamesTheTightestWindow() // исчерпано окно — «исчерпано», как на его карточке
	{
		var now = DateTime.UtcNow;
		var claude = new AgentState(new ClaudeAgent()) { Snapshot = new("Max", null, [new(WindowKind.Session, null, 100, null, Limiting: true)], null) };

		Assert.Equal("langHealthExhausted", Views.Build([claude], Hooks(), [], now, _levels).HealthText);
		Assert.Equal("langHealthUnknown", Views.Build([], Hooks(), [], now, _levels).HealthText);
	}

	[Fact]
	public void EachTabHasItsOwnBadge() // на вкладке Codex без трат — не «критично» из-за Claude
	{
		var now = DateTime.UtcNow;
		var claude = new AgentState(new ClaudeAgent()) { Snapshot = new("Max", null, [new(WindowKind.Session, null, 95, null, Limiting: true)], null) };
		var codex = new AgentState(new CodexAgent()) { Snapshot = new("Free", null, [new(WindowKind.Month, null, 0, null, Limiting: true)], null) };
		var gemini = new AgentState(new GeminiAgent());

		var vitals = Views.Build([claude, codex, gemini], Hooks(), [], now, _levels);

		Assert.Equal("langHealthCritical", vitals.HealthText); // значок и карточка по наведению — по худшему агенту
		Assert.Equal(("langHealthCritical", true), (vitals.Agents[0].HealthText, vitals.Agents[0].Alert));
		Assert.Equal(("langHealthGood", false), (vitals.Agents[1].HealthText, vitals.Agents[1].Alert));
		Assert.Equal(("langHealthUnknown", false), (vitals.Agents[2].HealthText, vitals.Agents[2].Alert)); // без данных — не тревога
	}

	[Theory]
	[InlineData(HookLink.Off, "langHooksHint")]
	[InlineData(HookLink.Moved, "langHooksHint")] // программу перенесли — hooks смотрят на старый путь
	[InlineData(HookLink.On, "langNoSessions")]
	public void HintSaysWhySessionsAreEmpty(HookLink hooks, string hint) =>
		Assert.Equal(hint, Views.Build([new AgentState(new ClaudeAgent())], Hooks(claude: hooks), [], DateTime.UtcNow, _levels).Agents[0].SessionsHint);

	private static readonly DateTime _now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

	private static Account Signed(string key, string plan, string? email, double used = 40) =>
		new(key, new Snapshot(plan, email, [new(WindowKind.Session, null, used, new DateTimeOffset(_now.AddHours(2)), Limiting: true)], null), _now.AddHours(-2), null);

	/// <summary>Claude с аккаунтами в порядке появления; в работе — inUse.</summary>
	private static AgentState Claude(string inUse, params Account[] accounts) => Remembering(new ClaudeAgent(), inUse, accounts);

	/// <summary>Агент с аккаунтами. Где важно, найден ли агент, — подставной: настоящий Claude ищет свой вход в профиле,
	/// а на сборке релиза на GitHub его нет.</summary>
	private static AgentState Remembering(IAgent agent, string inUse, params Account[] accounts)
	{
		var state = new AgentState(agent);
		var current = accounts.Single(account => account.Key == inUse);
		state.Recall(new(current.Snapshot, current.Updated, null, inUse, accounts), _now);

		return state;
	}

	[Fact]
	public void OneAccountLooksLike130() // карточки «Аккаунты» нет, строка тарифа — как раньше
	{
		var view = Views.Agent(Claude("A", Signed("A", "Max", "a@x.z")), _now, _levels);

		Assert.Empty(view.Accounts);
		Assert.Equal(("Max", "a@x.z"), (view.Plan, view.Info));
	}

	[Fact]
	public void PlanStandsApartFromTheName() // тариф в строке под вкладками — жирным, отдельно от имени
	{
		var view = Views.Agent(Claude("B", Signed("A", "Max", "a@x.z"), Signed("B", "Team", "b@x.z")), _now, _levels);
		var remembered = view.Accounts.Single(account => account.Key == "A");

		Assert.Equal(("Team", "b@x.z · langInUse"), (view.Plan, view.Info));
		Assert.Equal(("Max", "a@x.z · langRemembered"), (remembered.Plan, remembered.Info));
	}

	[Fact]
	public void AccountInUseComesFirst() // остальные — в порядке появления, строки не прыгают
	{
		var view = Views.Agent(Claude("B", Signed("A", "Max", "a@x.z"), Signed("B", "Team", "b@x.z"), Signed("C", "Pro", "c@x.z")), _now, _levels);

		Assert.Equal(["B", "A", "C"], view.Accounts.Select(account => account.Key));
		Assert.Equal([true, false, false], view.Accounts.Select(account => account.InUse));
		Assert.Equal("langAccountInUse", view.Accounts[0].Badge);
	}

	[Fact]
	public void RememberedAccountShowsItsOwnCards()
	{
		var view = Views.Agent(Claude("B", Signed("A", "Max", "a@x.z", used: 70), Signed("B", "Team", "b@x.z", used: 10)), _now, _levels);
		var remembered = view.Accounts.Single(account => account.Key == "A");

		Assert.Equal("70", remembered.Cards[0].Value);
		Assert.Equal("70", remembered.Meters[0].Value);
		Assert.NotNull(remembered.Hint);
		Assert.Null(view.Accounts[0].Hint);
		Assert.Equal("10", view.Cards[0].Value); // сверху по умолчанию — аккаунт в работе
	}

	[Fact]
	public void NamesUseEmailThenNumberUnderEye()
	{
		Account[] accounts = [Signed("A", "Max", "a@x.z"), Signed("B", "Team", "b@x.z")];

		Assert.Equal(["a@x.z", "b@x.z"], Views.Names("Claude", accounts, new Dictionary<string, string>(), hide: false).Values);
		Assert.Equal(["langAccountNumber", "langAccountNumber"], Views.Names("Claude", accounts, new Dictionary<string, string>(), hide: true).Values);
	}

	[Fact]
	public void EyeSwapsCustomNameForEmail() // под глазом — своё имя вместо почты, без глаза — почта
	{
		Account[] accounts = [Signed("A", "Max", "a@x.z"), Signed("B", "Team", null)];
		var custom = new Dictionary<string, string> { ["Claude/A"] = "Личный", ["Claude/B"] = "Работа" };

		Assert.Equal(["Личный", "Работа"], Views.Names("Claude", accounts, custom, hide: true).Values);
		Assert.Equal(["a@x.z", "Работа"], Views.Names("Claude", accounts, custom, hide: false).Values); // почты нет — своё имя
	}

	[Fact]
	public void OneAccountShowsCustomNameUnderEye() // «Team · Работа» под глазом, «Team · почта» без него; без имени — только тариф
	{
		var claude = Claude("A", Signed("A", "Team", "a@x.z"));
		var custom = new Dictionary<string, string> { ["Claude/A"] = "Работа" };

		Assert.Equal("Работа", Views.Agent(claude, _now, _levels, hide: true, names: custom).Info);
		Assert.Equal("a@x.z", Views.Agent(claude, _now, _levels, names: custom).Info);
		Assert.Equal("", Views.Agent(claude, _now, _levels, hide: true).Info);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)] // своё имя без глаза уступает почте — и почты снова совпадают
	public void SameEmailGetsPlan(bool named) // одна почта и в Team, и в личном тарифе
	{
		var custom = named ? new Dictionary<string, string> { ["Claude/A"] = "Работа" } : [];
		var names = Views.Names("Claude", [Signed("A", "Team", "a@x.z"), Signed("B", "Max", "a@x.z")], custom, hide: false);

		Assert.Equal(["a@x.z · Team", "a@x.z · Max"], names.Values);
	}

	[Fact]
	public void SameEmailAndPlanGetsNumber()
	{
		var names = Views.Names("Claude", [Signed("A", "Max", "a@x.z"), Signed("B", "Max", "a@x.z")], new Dictionary<string, string>(), hide: false);

		Assert.Equal(["a@x.z · Max · 1", "a@x.z · Max · 2"], names.Values);
	}

	[Fact]
	public void BusyBadgeNamesLastReset() // дальше суток — день и время, ближе — «через»
	{
		QuotaWindow Week(DateTime resets) => new(WindowKind.Week, null, 100, new DateTimeOffset(resets), Limiting: true);

		Assert.Equal("langAccountBusyAt", Text.AccountBadge(Availability.Busy, Week(_now.AddDays(3)), _now));
		Assert.Equal("langAccountBusyIn", Text.AccountBadge(Availability.Busy, Week(_now.AddMinutes(70)), _now));
		Assert.Equal("langAccountBusy", Text.AccountBadge(Availability.Busy, new(WindowKind.Week, null, 100, null, Limiting: true), _now));
		Assert.Equal("langAccountBack", Text.AccountBadge(Availability.Back, null, _now));
		Assert.Equal("langAccountFree", Text.AccountBadge(Availability.Free, null, _now));
	}

	[Fact]
	public void ChosenFallsBackToAccountInUse() // забытый или пропавший выбор — снова аккаунт в работе
	{
		var view = Views.Agent(Claude("B", Signed("A", "Max", "a@x.z"), Signed("B", "Team", "b@x.z")), _now, _levels);

		Assert.Equal("A", Popover.Chosen(view, "A")?.Key);
		Assert.Equal("B", Popover.Chosen(view, "gone")?.Key);
		Assert.Equal("B", Popover.Chosen(view, null)?.Key);
		Assert.Null(Popover.Chosen(Views.Agent(Claude("A", Signed("A", "Max", "a@x.z")), _now, _levels), "A"));
	}

	private static AgentState Found(string name, bool installed = true, bool off = false, Snapshot? snapshot = null) =>
		new(new Named(name, installed)) { Off = off, Snapshot = snapshot };

	[Fact]
	public void LeftModeShowsWhatRemains() // 46% израсходовано — 54 осталось; полоска тает, метка темпа зеркальна
	{
		var window = new QuotaWindow(WindowKind.Session, null, 46, _now.AddHours(4), Limiting: true);
		var state = Found("Claude", snapshot: new("Max", null, [window], null));
		var used = Views.Agent(state, _now, _levels).Cards[0];
		var left = Views.Agent(state, _now, _levels, percent: PercentMode.Left).Cards[0];

		Assert.Equal(("46", "langUsed", 46.0), (used.Value, used.Note, used.Fill));
		Assert.Equal(("54", "langLeft", 54.0), (left.Value, left.Note, left.Fill));
		Assert.Equal(80, left.Pace!.Value, precision: 6); // ровный темп 20% израсходованного — 80% осталось
		Assert.Equal("langPaceTipLeft", left.PaceTip);
		Assert.Equal(used.Badge, left.Badge); // уровни — по израсходованному
	}

	[Fact]
	public void LeftNeverGoesBelowZero() // израсходовано 120% — осталось 0, а не −20
	{
		var state = Found("Claude", snapshot: new("Max", null, [new(WindowKind.Session, null, 120, null, Limiting: true)], null));
		var card = Views.Agent(state, _now, _levels, percent: PercentMode.Left).Cards[0];

		Assert.Equal(("0", 0.0), (card.Value, card.Fill));
	}

	[Fact]
	public void AccountNumbersFollowTheMode() // окно «Агенты»: «Сессия 60%» в режиме «осталось»
	{
		var state = Remembering(new Named("Claude", installed: true), "A", Signed("A", "Max", "a@x.z", used: 40));

		Assert.Equal("60%", Views.AccountEdits(state, new Dictionary<string, string>(), hide: false, _now, PercentMode.Left)[0].Numbers[0].Percent);
	}

	[Fact]
	public void WidgetShowsTheFirstAgentUnlessAnotherIsChosen()
	{
		var view = Widget([Found("Claude", snapshot: Quotas(46)), Found("Codex", snapshot: Quotas(80))]);

		Assert.Equal(
			new[] { ("Claude", "46%"), ("Codex", "80%"), ("Claude", "46%") },
			new[] { view.Pick(null, _now), view.Pick("Codex", _now), view.Pick("Gemini", _now) }.Select(shown => (shown.Title, shown.Value)));
	}

	[Fact]
	public void LowAgentTakesTurnsWithTheChosenOne() // выбран Claude, у Copilot квота на исходе — по 10 с каждому
	{
		var view = Widget([Found("Claude", snapshot: Quotas(46)), Found("Copilot", snapshot: Quotas(95))]);

		var shown = new[] { view.Pick("Claude", _now), view.Pick("Claude", _now.AddSeconds(10)) };

		Assert.Equal(new[] { "Claude", "Copilot" }, shown.Select(picked => picked.Title).Order());
		Assert.Equal(WidgetState.Low, shown.Single(picked => picked.Title == "Copilot").State);
	}

	[Fact]
	public void ChosenLowAgentStaysAlone()
	{
		var view = Widget([Found("Claude", snapshot: Quotas(95)), Found("Codex", snapshot: Quotas(10))]);

		Assert.Equal(new[] { "Claude", "Claude" }, new[] { view.Pick("Claude", _now).Title, view.Pick("Claude", _now.AddSeconds(10)).Title });
	}

	[Fact]
	public void WaitingSessionIsKnownForTheClick()
	{
		var waiting = new Session("s", SessionState.Waiting, "Bash", "repo", null);

		Assert.Same(waiting, Widget([Found("Claude", snapshot: Quotas(40))], [waiting]).Waiting);
	}

	[Fact]
	public void WaitingBeatsEverything() // и критичная квота, и только что законченный ход, и выбранный агент
	{
		var waiting = new Session("w", SessionState.Waiting, "Bash", "repo", "main", Agent: "Codex");
		var view = Widget([Found("Claude", snapshot: Quotas(95)), Found("Codex", snapshot: Quotas(10))], [waiting], finished: waiting with { Id = "f", State = SessionState.Idle });

		Assert.Equal((WidgetState.Waiting, "langToastWaiting", "", "repo · Bash", "Codex", true), (view.State, view.Title, view.Value, view.Detail, view.Agent, view.Loud));
		Assert.Same(view, view.Pick("Claude", _now));
	}

	[Fact]
	public void FinishedComesBeforeLowQuota()
	{
		var done = new Session("f", SessionState.Idle, null, "repo", "main");
		var view = Widget([Found("Claude", snapshot: Quotas(95))], [done], done);

		Assert.Equal((WidgetState.Finished, "langToastDone", "repo", true), (view.State, view.Title, view.Detail, view.Loud));
		Assert.Same(done, view.Finished); // следующее «закончил» — новая вспышка, даже с той же надписью
	}

	[Fact]
	public void LowQuotaDoesNotOpenTheNotch() // критично часами — плашка закрывала бы вкладки
	{
		var view = Widget([Found("Claude", snapshot: Quotas(92))]).Pick(null, _now);

		Assert.Equal((WidgetState.Low, false), (view.State, view.Loud));
	}

	[Fact]
	public void WorkingSessionShowsAsWorking() =>
		Assert.Equal(WidgetState.Working, Widget([Found("Claude", snapshot: Quotas(40))], [new Session("s", SessionState.Working, "Edit", "repo", null)]).Pick(null, _now).State);

	[Fact]
	public void EyeHidesProjectsInTheWidget()
	{
		var waiting = new Session("w", SessionState.Waiting, "Bash", "repo", "main");
		var view = Widget([Found("Claude", snapshot: Quotas(40))], [waiting], hide: true);

		Assert.Equal(("", "Claude", ""), (view.Detail, view.Sessions[0].Name, view.Sessions[0].Branch));
	}

	[Fact]
	public void EyeHidesTheToolOfAWorkingSession()
	{
		var working = new Session("s", SessionState.Working, "mcp__db__query", "repo", "main");

		Assert.Equal("langSessionWorking", Widget([Found("Claude", snapshot: Quotas(40))], [working], hide: true).Sessions[0].State);
	}

	[Fact]
	public void WidgetWithoutDataSaysSo()
	{
		var view = Widget([Found("Claude")]).Pick(null, _now);

		Assert.Equal(("Claude", "", "langWidgetNoData"), (view.Title, view.Value, view.Detail));
		Assert.Equal(("langNoAgents", (string?)null), (Widget([]).Title, Widget([]).Agent));
	}

	[Fact]
	public void WidgetListsWaitingFirstAndAtMostThree()
	{
		Session One(string id, SessionState state) => new(id, state, null, id, null);
		var view = Widget([Found("Claude", snapshot: Quotas(40))], [One("a", SessionState.Idle), One("b", SessionState.Working), One("c", SessionState.Waiting), One("d", SessionState.Idle)]);

		Assert.Equal(new[] { "c", "b", "a" }, view.Sessions.Select(session => session.Name));
	}

	[Fact]
	public void WidgetFollowsThePercentMode()
	{
		var view = Widget([Found("Claude", snapshot: Quotas(46))], percent: PercentMode.Left).Pick(null, _now);

		Assert.Equal(("54%", 54.0, "54%"), (view.Value, view.Fill, view.Agents[0].Value));
	}

	[Fact]
	public void ChipShowsEveryAgentUnlessSomeoneWaits()
	{
		var waiting = new Session("s", SessionState.Waiting, "Bash", "repo", null);

		Assert.Equal(
			(true, false, false),
			(Widget([Found("Claude", snapshot: Quotas(46))]).ShowsAgents, Widget([Found("Claude", snapshot: Quotas(46))], [waiting]).ShowsAgents, Widget([]).ShowsAgents));
	}

	[Fact]
	public void TurnsSkipAgentsWithoutData()
	{
		var view = Widget([Found("Claude", snapshot: Quotas(46)), Found("Copilot"), Found("Codex", snapshot: Quotas(10))]);

		var shown = Enumerable.Range(0, 4).Select(slot => view.Turn(_now.AddSeconds(6 * slot))!.Name).ToList();

		Assert.Equal(new[] { "Claude", "Codex" }, shown.Distinct().Order());
		Assert.NotEqual(shown[0], shown[1]);
	}

	[Fact]
	public void TurnsDoNotStopOnALowAgent() // агент под 100% неделями не должен останавливать смену
	{
		var view = Widget([Found("Claude", snapshot: Quotas(10)), Found("Codex", snapshot: Quotas(95))]);

		Assert.Equal(new[] { "Claude", "Codex" }, new[] { view.Pick(WidgetView.Turns, _now).Title, view.Pick(WidgetView.Turns, _now.AddSeconds(6)).Title }.Order());
	}

	[Fact]
	public void NoTurnWithoutData() => Assert.Null(Widget([Found("Claude")]).Turn(_now));

	[Fact]
	public void TurnShowsTheAgentOfItsTurn()
	{
		var view = Widget([Found("Claude", snapshot: Quotas(46)), Found("Codex", snapshot: Quotas(10))]);

		var shown = new[] { view.Pick(WidgetView.Turns, _now), view.Pick(WidgetView.Turns, _now.AddSeconds(6)) };

		Assert.Equal(new[] { "Claude", "Codex" }, shown.Select(turned => turned.Title).Order());
		Assert.All(shown, turned => Assert.Equal(view.Agents.Single(agent => agent.Name == turned.Title).Value, turned.Value));
	}

	[Fact]
	public void WaitingDoesNotTurn()
	{
		var view = Widget([Found("Claude", snapshot: Quotas(46)), Found("Codex", snapshot: Quotas(10))], [new Session("s", SessionState.Waiting, "Bash", "repo", null)]);

		Assert.Same(view, view.Pick(WidgetView.Turns, _now.AddSeconds(6)));
	}

	[Fact]
	public void EveryAgentAtOnceGlowsWhenOneIsLow() // значок «все сразу»: красное кольцо видно, свечение — медленное
	{
		var view = Widget([Found("Claude", snapshot: Quotas(46)), Found("Copilot", snapshot: Quotas(95))]).Pick(WidgetView.All, _now);

		Assert.Equal((true, WidgetState.Low), (view.ShowsAgents, view.State));
	}

	[Fact]
	public void OnlyTheWorkingAgentMoves() // работает Codex — рябь у Codex, а не у выбранного Claude
	{
		var working = new Session("s", SessionState.Working, "Edit", "repo", null, Agent: "Codex");
		var view = Widget([Found("Claude", snapshot: Quotas(46)), Found("Codex", snapshot: Quotas(10)), Found("Copilot")], [working]);

		Assert.Equal(new[] { false, true, false }, view.Agents.Select(agent => agent.Working));
		Assert.Equal((false, true), (view.Pick("Claude", _now).Busy, view.Pick("Codex", _now).Busy));
	}

	[Fact]
	public void NothingMovesWhileWaiting() // «ждёт» светится само, движение работы его не перебивает
	{
		var view = Widget([Found("Claude", snapshot: Quotas(46))], [new Session("w", SessionState.Waiting, "Bash", "repo", null), new Session("s", SessionState.Working, "Edit", "repo", null)]);

		Assert.False(view.Pick(null, _now).Busy);
	}

	private static Snapshot Quotas(double used) => new("Max", null, [new(WindowKind.Session, null, used, _now.AddHours(3), Limiting: true)], null);

	private static WidgetView Widget(IReadOnlyList<AgentState> agents, IReadOnlyList<Session>? sessions = null, Session? finished = null, bool hide = false, PercentMode percent = PercentMode.Used) =>
		Views.Widget(agents, sessions ?? [], finished, _now, _levels, percent, hide);

	[Fact]
	public void AgentRowsNumberOnlyVisibleTabs() // номер — это Ctrl+N: у выключенного его нет, и соседи не сдвигаются
	{
		var rows = Views.AgentRows([Found("Claude"), Found("Codex", off: true), Found("Gemini")]);

		Assert.Equal(["1", "", "2"], rows.Select(row => row.Number));
		Assert.Equal([true, false, true], rows.Select(row => row.Shown));
	}

	[Fact]
	public void MissingAgentsGoLast()
	{
		var rows = Views.AgentRows([Found("Claude", installed: false), Found("Codex")]);

		Assert.Equal(["Codex", "Claude"], rows.Select(row => row.Name));
		Assert.False(rows[1].Found);
		Assert.Equal("", rows[1].Number);
	}

	[Fact]
	public void AgentLineTellsWhyNoData()
	{
		Assert.Equal("langAgentMissing", Text.AgentLine(Found("Claude", installed: false, off: true)));
		Assert.Equal("langAgentOff", Text.AgentLine(Found("Claude", off: true)));
		Assert.Equal("Max", Text.AgentLine(Found("Claude", snapshot: new("Max", null, [], null))));
		Assert.Equal("langAgentNoData", Text.AgentLine(Found("Claude")));
	}

	[Fact]
	public void AgentLineCountsAccounts() // «2 аккаунта · Team», как на макете
	{
		var state = Remembering(new Named("Claude", installed: true), "B", Signed("A", "Max", "a@x.z"), Signed("B", "Team", "b@x.z"));

		Assert.Equal($"langAccountCount{Text.Plural(2, Localization.Current)} · Team", Text.AgentLine(state));
	}

	[Theory]
	[InlineData(1, "ru", "One")]
	[InlineData(2, "ru", "Few")]
	[InlineData(4, "uk", "Few")]
	[InlineData(5, "ru", "Many")]
	[InlineData(11, "ru", "Many")]
	[InlineData(12, "uk", "Many")]
	[InlineData(21, "ru", "One")]
	[InlineData(22, "ru", "Few")]
	[InlineData(1, "en", "One")]
	[InlineData(2, "en", "Many")]
	public void PluralFollowsTheLanguage(int count, string language, string form) => Assert.Equal(form, Text.Plural(count, language));

	[Fact]
	public void AgentDetailsSayWhatIsKnown() // «Найден · вход есть · подписка Team · вкладка Ctrl+1»
	{
		Assert.Equal(
			"langStateFound · langStateSignedIn · langStatePlan · langStateTab",
			Text.AgentDetails(Found("Claude", snapshot: new("Team", null, [], null)), "1"));
		Assert.Equal("langStateFound · langAgentOff", Text.AgentDetails(Found("Claude", off: true), ""));
		Assert.Equal("langAgentMissing", Text.AgentDetails(Found("Claude", installed: false), ""));
	}

	[Fact]
	public void BusyAccountSaysWhenItFreesUp() // «Сессия 100% · Неделя 64% · 2 ч назад; сессия сбросится через 1 ч 10 мин»
	{
		var busy = new Account("A", new Snapshot("Max", "a@x.z", [new(WindowKind.Session, null, 100, new DateTimeOffset(_now.AddMinutes(70)), Limiting: true)], null), _now.AddHours(-2), null);
		var edits = Views.AccountEdits(Claude("B", busy, Signed("B", "Team", "b@x.z")), new Dictionary<string, string>(), hide: false, _now);
		var remembered = edits.Single(edit => edit.Key == "A");

		Assert.Equal("langAccountFreesIn", remembered.Note);
		Assert.True(remembered.Numbers.Single().Spent);
		Assert.Null(edits.Single(edit => edit.Key == "B").Note);
	}

	[Fact]
	public void AgeIsShort()
	{
		Assert.Equal("langAgeNow", Text.Age(_now.AddSeconds(-30), _now));
		Assert.Equal("langAgeAgo", Text.Age(_now.AddHours(-2), _now));
		Assert.Equal("langAgeAt", Text.Age(_now.AddDays(-2), _now));
		Assert.Equal("", Text.Age(null, _now));
	}

	/// <summary>Агент с именем, установленный или нет; его не спрашивают.</summary>
	private sealed class Named(string name, bool installed) : IAgent
	{
		public string Name => name;

		public string Cli => "fake";

		public string? Credentials => null;

		public bool Installed => installed;

		public string? Dashboard => null;

		public IUsageLog? Usage => null;

		public IStatusPage? Status => null;

		public HookTarget? Hooks => null;

		public System.Threading.Tasks.Task<Snapshot> FetchAsync(System.Net.Http.HttpClient http, System.Threading.CancellationToken cancellation) =>
			throw new NotSupportedException();
	}

	private static Dictionary<string, HookLink> Hooks(HookLink claude = HookLink.On, HookLink codex = HookLink.On) =>
		new() { ["Claude"] = claude, ["Codex"] = codex };

	[Fact]
	public void EachTabShowsItsOwnSessions()
	{
		Session[] sessions = [new("a", SessionState.Working, null, "api", null), new("b", SessionState.Waiting, null, "web", null, Agent: "Codex")];
		var tabs = Views.Build([new(new ClaudeAgent()), new(new CodexAgent()), new(new GeminiAgent())], Hooks(), sessions, DateTime.UtcNow, _levels).Agents;

		Assert.Equal(["api"], tabs[0].Sessions!.Select(row => row.Folder));
		Assert.Equal(["web"], tabs[1].Sessions!.Select(row => row.Folder));
		Assert.Equal("langSessions", tabs[1].SessionsTitle);
		Assert.Null(tabs[2].Sessions); // у Gemini hooks нет — и карточки нет
	}

	[Fact]
	public void EachTabShowsItsOwnStatus() // на вкладке Codex — статус Codex, а не Anthropic
	{
		var now = DateTime.UtcNow;
		var claude = Views.Agent(new AgentState(new ClaudeAgent()) { Status = new("none", "", null, "Anthropic") }, now, _levels);
		var codex = Views.Agent(new AgentState(new CodexAgent()), now, _levels);
		var gemini = Views.Agent(new AgentState(new GeminiAgent()), now, _levels);

		Assert.Equal(("langStatusOk", "https://status.claude.com"), (claude.StatusText, claude.StatusUrl));
		Assert.Equal(("langStatusUnknown", "https://status.openai.com"), (codex.StatusText, codex.StatusUrl)); // ещё не спрашивали
		Assert.Null(gemini.StatusUrl); // у Gemini статус-страницы нет — и строки нет
	}

	[Fact]
	public void HoverCardShowsTheWorstStatus()
	{
		var now = DateTime.UtcNow;
		var claude = new AgentState(new ClaudeAgent()) { Status = new("none", "", null, "Anthropic") };
		var codex = new AgentState(new CodexAgent()) { Status = new("minor", "Degraded Performance", "Codex is slow", "Codex") };

		Assert.Equal("Codex is slow", Views.Build([claude, codex], Hooks(), [], now, _levels).StatusText);

		codex.Status = new("none", "", null, "Codex");
		Assert.Equal("langStatusAllOk", Views.Build([claude, codex], Hooks(), [], now, _levels).StatusText); // все в порядке — без имён
		Assert.Equal("langStatusOk", Views.Build([claude], Hooks(), [], now, _levels).StatusText); // один агент — его строка
		Assert.Equal("", Views.Build([new AgentState(new GeminiAgent())], Hooks(), [], now, _levels).StatusText);
	}

	[Fact]
	public void HiddenAccountLeavesOnlyThePlan() // показывают экран — почты на виду нет, ни в окне, ни в карточке наведения
	{
		var now = DateTime.UtcNow;
		var claude = new AgentState(new ClaudeAgent()) { Snapshot = new("Max", "me@example.com", [], null) };

		Assert.Equal(("Max", "me@example.com"), (Views.Agent(claude, now, _levels).Plan, Views.Agent(claude, now, _levels).Info));
		Assert.Equal(("Max", ""), (Views.Build([claude], Hooks(), [], now, _levels, hide: true).Agents[0].Plan, Views.Build([claude], Hooks(), [], now, _levels, hide: true).Agents[0].Info));
	}

	[Fact]
	public void TabOpensItsAgentsUsagePage() =>
		Assert.Equal("https://claude.ai/settings/usage", Views.Agent(new AgentState(new ClaudeAgent()), DateTime.UtcNow, _levels).Dashboard);

	[Theory]
	[InlineData("feature/APP-1234", null, "APP-1234-retry-failed-payments-in-checkout\nfeature/APP-1234")]
	[InlineData("", null, "APP-1234-retry-failed-payments-in-checkout")]
	[InlineData("main", "почини тесты", "APP-1234-retry-failed-payments-in-checkout\nmain\nпочини тесты")]
	public void SessionTipShowsTheWholeName(string branch, string? prompt, string tip) => // в строке длинное имя обрезано троеточием
		Assert.Equal(tip, new SessionView("APP-1234-retry-failed-payments-in-checkout", branch, prompt, false, "", Avalonia.Media.Brushes.Gray, new Session("s", SessionState.Idle, null, "repo", null)).Tip);

	[Fact]
	public void NamesakesShowTheirFirstPrompt() // две сессии в одной папке на одной ветке иначе не различить
	{
		Session[] sessions =
		[
			new("a", SessionState.Waiting, null, "ProjectsComfy", "master", Prompt: "почини тесты"),
			new("b", SessionState.Working, null, "ProjectsComfy", "master"), // начата до обновления — запроса ещё нет
			new("c", SessionState.Idle, null, "api", "main", Prompt: "добавь метод"),
			new("d", SessionState.Idle, null, "api", null, Agent: "Codex", Prompt: "проверь"), // у Codex своя вкладка
		];

		var rows = Views.Build([new(new ClaudeAgent())], Hooks(), sessions, DateTime.UtcNow, _levels).Agents[0].Sessions!;
		Assert.Equal(["почини тесты", "master", "main"], rows.Select(row => row.Detail));

		// Показывают экран — запросов не видно и в подсказке.
		var hidden = Views.Build([new(new ClaudeAgent())], Hooks(), sessions, DateTime.UtcNow, _levels, hide: true).Agents[0].Sessions!;
		Assert.Equal(["master", "master", "main"], hidden.Select(row => row.Detail));
		Assert.Equal("ProjectsComfy\nmaster", hidden[0].Tip);
	}

	[Fact]
	public void SessionRowKnowsItsSession() // щелчок по строке поднимает окно, где идёт эта сессия
	{
		var session = new Session("s", SessionState.Working, null, "repo", "main", Pid: 4242, Cwd: @"C:\Work\repo");

		Assert.Same(session, Views.Build([new AgentState(new ClaudeAgent())], Hooks(), [session], DateTime.UtcNow, _levels).Agents[0].Sessions![0].Session);
	}

	[Theory]
	[InlineData(WindowKind.Session, 2.5, 50d)] // половина пяти часов прошла — по ровному темпу половина и потрачена
	[InlineData(WindowKind.Week, 24 * 7, 0d)]
	[InlineData(WindowKind.Week, 24 * 3.5, 50d)]
	[InlineData(WindowKind.WeekModel, 24 * 3.5, 50d)] // «Неделя · Opus» — те же семь дней
	[InlineData(WindowKind.Model, 24, null)] // длина окна модели неизвестна
	[InlineData(WindowKind.Session, -0.1, null)] // сброс прошёл — сравнивать не с чем
	public void ExpectedPaceIsTheElapsedShare(WindowKind kind, double hoursLeft, double? expected)
	{
		var now = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
		var window = new QuotaWindow(kind, null, 30, now.AddHours(hoursLeft), Limiting: true);

		Assert.Equal(expected, Quota.Expected(window, now));
	}

	[Theory]
	[InlineData(76, "langPaceRunout")] // с опережением — когда кончится: «хватит до 13:40»
	[InlineData(54, "langPaceEven")] // ±5 пунктов — ещё ровно
	[InlineData(20, "langPaceSpare")]
	public void CardTellsThePace(double used, string word)
	{
		var now = DateTime.UtcNow;
		var state = new AgentState(new ClaudeAgent())
		{
			Snapshot = new("Max", null, [new(WindowKind.Session, null, used, now.AddHours(2.5), Limiting: true)], null),
		};

		var card = Assert.Single(Views.Agent(state, now, _levels).Cards);

		Assert.Equal(word, card.Caption); // в окне — вместо «использовано» у числа
		Assert.EndsWith(" · " + word, card.Footer); // в карточке наведения — после сброса
		Assert.Equal(50, card.Pace!.Value, 3);
		Assert.Equal("langPaceTip", card.PaceTip); // что за белая метка — подсказкой над карточкой
	}

	[Fact]
	public void UsedUpWindowIsJustAhead() // исчерпано — «хватит до» уже некуда
	{
		var now = DateTime.UtcNow;
		var state = new AgentState(new ClaudeAgent())
		{
			Snapshot = new("Max", null, [new(WindowKind.Session, null, 100, now.AddHours(2.5), Limiting: true)], null),
		};

		Assert.Equal("langPaceAhead", Assert.Single(Views.Agent(state, now, _levels).Cards).Caption);
	}

	[Fact]
	public void NoPaceNoTip()
	{
		var state = new AgentState(new ClaudeAgent())
		{
			Snapshot = new("Max", null, [new(WindowKind.Model, "Opus", 30, DateTime.UtcNow.AddDays(2), Limiting: false)], null),
		};

		Assert.Null(Assert.Single(Views.Agent(state, DateTime.UtcNow, _levels).Cards).PaceTip);
	}

	[Fact]
	public void PassedResetShowsAnEmptyWindow() // после сна вход истёк, а сессия за это время сбросилась — не 91%
	{
		var now = DateTime.UtcNow;
		var state = new AgentState(new ClaudeAgent())
		{
			Snapshot = new("Max", null,
			[
				new(WindowKind.Session, null, 91, now.AddMinutes(-5), Limiting: true),
				new(WindowKind.Week, null, 40, now.AddDays(2), Limiting: true),
			], null),
		};

		var cards = Views.Agent(state, now, _levels).Cards;

		Assert.Equal(("0", 0d, "langResetDone"), (cards[0].Value, cards[0].Fill, cards[0].Resets));
		Assert.Equal("40", cards[1].Value);
	}

	private static UsageDay Day(int day, long input = 0, long output = 0, decimal? cost = 0, bool unpriced = false) =>
		new(new DateOnly(2026, 10, day), input, output, 0, 0, cost, unpriced, TimeSpan.Zero);

	[Fact]
	public void GeminiHasNoStatisticsBlock() =>
		Assert.Null(Views.Agent(new AgentState(new GeminiAgent()), DateTime.UtcNow, _levels).Usage);

	[Fact]
	public void CodexCountsUntilTheFirstRead() =>
		Assert.Equal("langCounting", Views.Agent(new AgentState(new CodexAgent()), DateTime.UtcNow, _levels).Usage!.CostNote);

	[Fact]
	public void ClaudeCountsUntilTheFirstRead() // первое чтение логов — секунды: не нули, а «считаю…»
	{
		var usage = Views.Agent(new AgentState(new ClaudeAgent()), DateTime.UtcNow, _levels).Usage!;

		Assert.Equal("…", usage.Cost);
		Assert.Equal("langCounting", usage.CostNote);
	}

	[Fact]
	public void TodayIsTheLastDay()
	{
		var usage = Views.Usage([Day(6, cost: 9.80m), Day(7, cost: 12.34m)]);

		Assert.Equal("$12.34", usage.Cost);
		Assert.Equal("langCostNote", usage.CostNote);
	}

	[Fact]
	public void CostSaysWhenItIsPartial()
	{
		Assert.Equal("langCostPartial", Views.Usage([Day(6), Day(7, unpriced: true)]).CostNote);
		Assert.Equal("langCostMissing", Views.Usage([Day(6, cost: null), Day(7, cost: null)]).CostNote);
		Assert.Equal("—", Views.Usage([Day(6, cost: null), Day(7, cost: null)]).Cost);
	}

	[Fact]
	public void StackedBarsShareOneScale()
	{
		var bars = Views.Chart([Day(6, input: 10, output: 30), Day(7, input: 20, output: 20)], ChartMode.Tokens).Bars;

		Assert.Equal([22.5, 67.5], bars[0].Parts.Select(part => part.Height)); // самый большой день — 40 — во всю высоту 90; снизу вход
		Assert.Equal(45, bars[1].Parts[0].Height);
		Assert.Equal(0.6, bars[0].Opacity);
		Assert.Equal(1, bars[1].Opacity); // сегодня — ярче
	}

	[Fact]
	public void EmptyMonthDrawsNothing() =>
		Assert.All(Views.Chart([Day(6), Day(7)], ChartMode.Cost).Bars, bar => Assert.All(bar.Parts, part => Assert.Equal(0, part.Height)));

	[Fact]
	public void ModelsStackCostByFamily()
	{
		var today = Day(7, cost: 20) with { Models = new ModelCosts(15, 5, 0, 0, 0, 0, 0, 0) };

		var bar = Views.Chart([Day(6), today], ChartMode.Models).Bars[1];

		Assert.Equal([67.5, 22.5], bar.Parts.Select(part => part.Height)); // $20 — верх шкалы, снизу Opus; пустых семейств нет
	}

	[Fact]
	public void ScaleFitsTheBiggestDay() =>
		Assert.Equal(["$30", "$15", "$0"], Views.Chart([Day(6, cost: 10m), Day(7, cost: 21.3m)], ChartMode.Cost).AxisY);

	[Fact]
	public void NoDaysNoTotal() => Assert.Equal("", Views.Chart([], ChartMode.Cost).Total); // «$0.00» за месяц, которого нет, — неправда

	[Fact]
	public void LegendNamesEachLayer()
	{
		UsageDay[] days = [Day(7, cost: 3) with { Models = new ModelCosts(1, 0, 0, 0, 0, 0, 0, 2) }];

		Assert.Equal(["langCostNote"], Views.Legend(ChartMode.Cost, days).Select(item => item.Text));
		Assert.Equal(["langInput", "langOutput"], Views.Legend(ChartMode.Tokens, days).Select(item => item.Text));
		Assert.Equal(["Opus", "langOther"], Views.Legend(ChartMode.Models, days).Select(item => item.Text)); // только семейства со стоимостью
	}

	[Fact]
	public void CacheTotalSaysWhatTheCacheSaved()
	{
		Assert.EndsWith(" · langCacheSaved", Views.Total([Day(6) with { Saved = 3.80m }, Day(7) with { Saved = 1.20m }], ChartMode.Cache));
		Assert.DoesNotContain("·", Views.Total([Day(6), Day(7)], ChartMode.Cache)); // без цен экономию не посчитать
	}

	[Theory]
	[InlineData(21.3, 30)]
	[InlineData(40, 40)]
	[InlineData(124.61, 160)]
	[InlineData(294_400_000, 300_000_000)]
	[InlineData(0, 0)]
	public void ScaleIsTwoRoundSteps(double top, double ceiling) => Assert.Equal(ceiling, Views.Ceiling(top), 6);

	[Fact]
	public void AxisSaysMoneyForCost() => Assert.Equal(["$40", "$20", "$0"], Views.AxisY(40, ChartMode.Cost));

	[Fact]
	public void EmptyAxisShowsOnlyZero() => Assert.Equal(["", "", "$0"], Views.AxisY(0, ChartMode.Cost));

	[Fact]
	public void DatesEveryWeekFromTheFirstDay()
	{
		var days = Enumerable.Range(0, 30).Select(offset => Day(1) with { Date = new DateOnly(2026, 9, 8).AddDays(offset) }).ToList();

		Assert.Equal([0, 7, 14, 21, 28], Views.AxisX(days).Select(tick => tick.Index));
	}

	[Fact]
	public void AgentWithoutStatisticsOrSessionsIsBare() // на месте статистики — строка, почему её нет
	{
		Assert.True(Views.Agent(new AgentState(new CopilotAgent()), DateTime.UtcNow, _levels).Bare);
		Assert.False(Views.Agent(new AgentState(new ClaudeAgent()), DateTime.UtcNow, _levels).Bare);
	}

	[Fact]
	public void TeamPoolCardSaysSo() // командный пул Cursor — не свой расход
	{
		var state = new AgentState(new GeminiAgent()) { Snapshot = new("Team", null, [], new ExtraUsage(300m, 1000m, "USD", Team: true)) };

		Assert.Equal("LANGEXTRATEAM", Assert.Single(Views.Agent(state, DateTime.UtcNow, _levels).Cards).Title);
	}

	[Fact]
	public void FeatureWindowGetsACard()
	{
		var state = new AgentState(new GeminiAgent())
		{
			Snapshot = new("Free", "octocat", [new(WindowKind.MonthFeature, "chat", 30, DateTime.UtcNow.AddDays(10), Limiting: true)], null),
		};

		var card = Assert.Single(Views.Agent(state, DateTime.UtcNow, _levels).Cards);

		Assert.Equal("CHAT", card.Title);
		Assert.True(card.HasPace); // 30 дней — темп известен
	}
}
