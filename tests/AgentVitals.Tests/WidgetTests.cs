using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Xunit;

namespace AgentVitals.Tests;

/// <summary>Хозяин виджета и наведение — с подставными окнами: настоящие держатся на Win32 и проверяются на машине.</summary>
public class WidgetTests
{
	private static readonly DateTime _t = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

	[Fact]
	public void PillWaitsForTheMouseToStay() // мимоходом к вкладке браузера плашка не выезжает, а панель сама не раскрывается
	{
		var hover = new WidgetHover();

		Assert.Equal(
			new[] { WidgetLook.Rest, WidgetLook.Rest, WidgetLook.Peek, WidgetLook.Peek },
			new[] { hover.Next(true, _t), hover.Next(true, At(200)), hover.Next(true, At(300)), hover.Next(true, At(5000)) });
	}

	[Fact]
	public void ClickOpensAndClosesThePanel()
	{
		var hover = Armed();

		hover.Click();
		var open = hover.Next(true, At(400));
		hover.Click();

		Assert.Equal((WidgetLook.Panel, WidgetLook.Peek), (open, hover.Next(true, At(500))));
	}

	[Fact]
	public void ClickBeforeThePillDoesNothing()
	{
		var hover = new WidgetHover();
		hover.Next(true, _t);

		hover.Click();

		Assert.Equal(WidgetLook.Peek, hover.Next(true, At(300)));
	}

	[Fact]
	public void PassingByStartsOver() // мышь пробежала по полоске: следующий заход снова ждёт 0,3 с
	{
		var hover = new WidgetHover();
		hover.Next(true, _t);
		hover.Next(true, At(200));

		Assert.Equal((WidgetLook.Rest, WidgetLook.Rest), (hover.Next(false, At(250)), hover.Next(true, At(300))));
	}

	[Fact]
	public void PanelStaysAMomentAfterTheMouseLeaves()
	{
		var hover = Armed();
		hover.Click();

		Assert.Equal(
			new[] { WidgetLook.Panel, WidgetLook.Panel, WidgetLook.Rest },
			new[] { hover.Next(false, At(1000)), hover.Next(false, At(1400)), hover.Next(false, At(1500)) });
	}

	[Fact]
	public void ClickOnTheWidgetOpensThePanelAtOnce()
	{
		Fake? made = null;
		var host = new WidgetHost(kind => made = new Fake(kind) { Area = new(900, 0, 120, 4) });
		host.Apply(WidgetKind.Notch, default);
		host.Tick(new PixelPoint(950, 2), _t);
		host.Tick(new PixelPoint(950, 2), At(300));

		made!.Click();

		Assert.Equal(new[] { (WidgetLook.Rest, false), (WidgetLook.Peek, true), (WidgetLook.Panel, true) }, made.Looks);
	}

	[Fact]
	public void ClickWhileWaitingGoesToTheTerminal() // плашка говорит о ждущей сессии — щелчок сразу к её окну, без панели
	{
		Fake? made = null;
		var host = new WidgetHost(kind => made = new Fake(kind) { Area = new(900, 0, 120, 4) });
		var waiting = new Session("s", SessionState.Waiting, "Bash", "repo", null);
		Session? chosen = null;
		host.SessionChosen += session => chosen = session;
		host.Render(View(WidgetState.Waiting) with { Waiting = waiting }, hide: false);
		host.Apply(WidgetKind.Notch, default);
		host.Tick(new PixelPoint(950, 2), _t);
		host.Tick(new PixelPoint(950, 2), At(300));

		made!.Click();

		Assert.Same(waiting, chosen);
		Assert.NotEqual(WidgetLook.Panel, made.Looks[^1].Look);
	}

	[Fact]
	public void ClicksPassOnceTheCursorIsOff() // панель ещё видна полсекунды, но адресная строка под ней уже нажимается
	{
		Fake? made = null;
		var host = new WidgetHost(kind => made = new Fake(kind) { Area = new(900, 0, 120, 4) });
		host.Apply(WidgetKind.Notch, default);
		host.Tick(new PixelPoint(950, 2), _t);
		host.Tick(new PixelPoint(950, 2), At(300));
		made!.Click();

		host.Tick(new PixelPoint(950, 300), At(400));

		Assert.Equal((WidgetLook.Panel, false), made.Looks[^1]);
	}

	[Fact]
	public void PinnedPillStaysOutAndLetsClicksThrough()
	{
		Fake? made = null;
		var host = new WidgetHost(kind => made = new Fake(kind) { Area = new(900, 0, 120, 4) }) { Pinned = true };
		host.Apply(WidgetKind.Notch, default);

		host.Tick(new PixelPoint(10, 500), _t);

		Assert.Equal((WidgetLook.Peek, false), made!.Looks[^1]);
	}

	[Fact]
	public void OnlyOneLookAtATime()
	{
		List<Fake> made = [];
		var host = new WidgetHost(kind =>
		{
			var fake = new Fake(kind);
			made.Add(fake);

			return fake;
		});

		host.Apply(WidgetKind.Notch, default);
		host.Apply(WidgetKind.Notch, default);
		host.Apply(WidgetKind.Chip, default);
		host.Apply(WidgetKind.Off, default);

		Assert.Equal(new[] { (WidgetKind.Notch, true), (WidgetKind.Chip, true) }, made.Select(fake => (fake.Kind, fake.Closed)));
		Assert.Equal(WidgetKind.Off, host.Kind);
	}

	[Fact]
	public void EyeReachesEveryNewWindow() // глаз включили до того, как выбрали вид
	{
		Fake? made = null;
		var host = new WidgetHost(kind => made = new Fake(kind));

		host.Render(View(WidgetState.Idle), hide: true);
		host.Apply(WidgetKind.Chip, default);

		Assert.True(made!.Hidden);
	}

	[Fact]
	public void LoudStateShowsThePillWithoutTheMouse()
	{
		Fake? made = null;
		var host = new WidgetHost(kind => made = new Fake(kind) { Area = new(900, 0, 120, 4) });
		host.Apply(WidgetKind.Notch, default);

		host.Render(View(WidgetState.Waiting), hide: false);
		host.Tick(new PixelPoint(10, 500), DateTime.UtcNow);
		Assert.Equal((WidgetLook.Peek, false), made!.Looks[^1]);

		host.Render(View(WidgetState.Idle), hide: false);
		host.Tick(new PixelPoint(10, 500), DateTime.UtcNow);
		Assert.Equal((WidgetLook.Rest, false), made.Looks[^1]);
	}

	[Fact]
	public void RowClicksReachTheApp()
	{
		Fake? made = null;
		var host = new WidgetHost(kind => made = new Fake(kind));
		string? chosen = null;
		host.AgentChosen += agent => chosen = agent;
		host.Apply(WidgetKind.Desktop, default);

		made!.Choose("Codex");

		Assert.Equal("Codex", chosen);
	}

	[Fact]
	public void ChipAreaIsKnownOnlyForTheChip()
	{
		var host = new WidgetHost(kind => new Fake(kind) { Area = new(1708, 994, 200, 34) });

		host.Apply(WidgetKind.Notch, default);
		Assert.Null(host.Chip);

		host.Apply(WidgetKind.Chip, default);
		Assert.Equal(new PixelRect(1708, 994, 200, 34), host.Chip);
	}

	private static WidgetView View(WidgetState state) => new(state, "", "", "", 0, Brushes.Gray, Brushes.Gray, null, [], []);

	private static DateTime At(int milliseconds) => _t.AddMilliseconds(milliseconds);

	/// <summary>Мышь простояла над виджетом 0,3 с: плашка выехала и нажимается.</summary>
	private static WidgetHover Armed()
	{
		var hover = new WidgetHover();
		hover.Next(true, _t);
		hover.Next(true, At(300));

		return hover;
	}

	/// <summary>Окно, которое только записывает, что с ним делали.</summary>
	private sealed class Fake(WidgetKind kind) : IWidgetWindow
	{
		public event Action<string>? AgentChosen;

		public event Action<Session>? SessionChosen;

		public event Action? Clicked;

		public WidgetKind Kind => kind;

		public bool Closed { get; private set; }

		public bool Hidden { get; private set; }

		/// <summary>Каждый новый вид и нажимается ли он; повтор того же не пишется — как настоящее окно его пропускает.</summary>
		public List<(WidgetLook Look, bool Clickable)> Looks { get; } = [];

		public PixelRect Area { get; init; }

		public void Open(PixelRect anchor)
		{
		}

		public void Render(WidgetView view)
		{
		}

		public void Show(WidgetLook look, bool clickable)
		{
			if (Looks.Count == 0 || Looks[^1] != (look, clickable))
			{
				Looks.Add((look, clickable));
			}
		}

		public void Click() => Clicked?.Invoke();

		public void Private(bool hide) => Hidden = hide;

		public void Tick()
		{
		}

		public void Close() => Closed = true;

		public void Choose(string agent) => AgentChosen?.Invoke(agent);

		public void Choose(Session session) => SessionChosen?.Invoke(session);
	}
}
