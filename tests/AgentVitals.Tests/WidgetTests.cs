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
	[Fact]
	public void HoverPeeksThenOpensThePanel()
	{
		var hover = new WidgetHover();
		var t = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

		Assert.Equal(
			new[] { WidgetLook.Rest, WidgetLook.Peek, WidgetLook.Peek, WidgetLook.Panel, WidgetLook.Panel, WidgetLook.Rest, WidgetLook.Peek },
			new[]
			{
				hover.Next(false, t), hover.Next(true, t), hover.Next(true, t.AddMilliseconds(300)), hover.Next(true, t.AddMilliseconds(500)),
				hover.Next(true, t.AddSeconds(5)), hover.Next(false, t.AddSeconds(6)), hover.Next(true, t.AddSeconds(7)),
			});
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
		Assert.Equal(WidgetLook.Peek, made!.Looks[^1]);

		host.Render(View(WidgetState.Idle), hide: false);
		host.Tick(new PixelPoint(10, 500), DateTime.UtcNow);
		Assert.Equal(WidgetLook.Rest, made.Looks[^1]);
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

	/// <summary>Окно, которое только записывает, что с ним делали.</summary>
	private sealed class Fake(WidgetKind kind) : IWidgetWindow
	{
		public event Action<string>? AgentChosen;

		public event Action<Session>? SessionChosen;

		public WidgetKind Kind => kind;

		public bool Closed { get; private set; }

		public bool Hidden { get; private set; }

		public List<WidgetLook> Looks { get; } = [];

		public PixelRect Area { get; init; }

		public void Open(PixelRect anchor)
		{
		}

		public void Render(WidgetView view)
		{
		}

		public void Show(WidgetLook look) => Looks.Add(look);

		public void Private(bool hide) => Hidden = hide;

		public void Tick()
		{
		}

		public void Close() => Closed = true;

		public void Choose(string agent) => AgentChosen?.Invoke(agent);

		public void Choose(Session session) => SessionChosen?.Invoke(session);
	}
}
