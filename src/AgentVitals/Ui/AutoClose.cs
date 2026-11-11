using System;
using Avalonia;

namespace AgentVitals;

/// <summary>Что делать окну у трея на этом шаге.</summary>
internal enum AutoCloseStep
{
	Stay,
	Fade,
	Close,
}

/// <summary>Окно у трея закрывается само: мышь ушла от него и от значка и не вернулась за выбранные секунды — окно
/// гаснет и закрывается; вернулась, пока гаснет, — остаётся. Пока мышь не сдвинулась с места, где была при открытии,
/// окно ждёт: открытое щелчком по плашке или тосту читают, не трогая мышь. Клавиша в окне начинает отсчёт заново.</summary>
internal sealed class AutoClose
{
	/// <summary>Сколько окно гаснет перед тем, как закрыться.</summary>
	public static readonly TimeSpan Fading = TimeSpan.FromMilliseconds(300);

	// Поле вокруг окна, которое ещё «рядом», и дрожь руки, которая ещё не движение, — в точках экрана.
	private const int _margin = 24;
	private const int _budge = 8;

	// Где была мышь при открытии и сдвинулась ли с тех пор.
	private PixelPoint _start;
	private bool _moved;

	// С какого момента мышь не рядом; null — рядом или ещё не сдвигалась.
	private DateTime? _away;

	public void Open(PixelPoint cursor)
	{
		_start = cursor;
		_moved = false;
		_away = null;
	}

	/// <summary>Клавиша в окне: с ним работают и без мыши.</summary>
	public void Touch() => _away = null;

	/// <param name="window">Окно, в точках экрана.</param>
	/// <param name="icon">Значок в трее, в точках экрана.</param>
	/// <param name="seconds">Через сколько секунд после ухода мыши закрыть; 0 — только вручную.</param>
	public AutoCloseStep Next(PixelPoint cursor, PixelRect window, PixelRect icon, DateTime now, int seconds)
	{
		_moved |= Math.Abs(cursor.X - _start.X) > _budge || Math.Abs(cursor.Y - _start.Y) > _budge;

		if (seconds <= 0 || !_moved || Near(cursor, window, _margin) || Near(cursor, icon, 4))
		{
			_away = null;

			return AutoCloseStep.Stay;
		}

		_away ??= now;
		var away = now - _away.Value;
		var delay = TimeSpan.FromSeconds(seconds);

		return away < delay ? AutoCloseStep.Stay : away < delay + Fading ? AutoCloseStep.Fade : AutoCloseStep.Close;
	}

	private static bool Near(PixelPoint cursor, PixelRect area, int margin) =>
		new PixelRect(area.X - margin, area.Y - margin, area.Width + (2 * margin), area.Height + (2 * margin)).Contains(cursor);
}
