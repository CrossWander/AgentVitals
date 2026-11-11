using System;

namespace AgentVitals;

/// <summary>На сколько включают «Не беспокоить».</summary>
internal enum QuietSpan
{
	HalfHour,
	Hour,
	Morning,
	Forever,
}

/// <summary>«Не беспокоить»: до какого момента молчат тосты и звуки, а виджет сам не раскрывается и не светится.
/// События за это время пропадают — тревоги отмечают их как обычно, и после тишины они не догоняют.</summary>
internal static class Quiet
{
	/// <summary>«Пока не выключу».</summary>
	public static readonly DateTime Forever = DateTime.MaxValue;

	// «До утра» — до этого часа по местным часам.
	private const int _morning = 8;

	/// <summary>Когда кончится тишина, включённая сейчас; «до утра» — до ближайших 8:00 по местным часам.</summary>
	/// <param name="now">Сейчас, UTC.</param>
	/// <returns>UTC; «пока не выключу» — <see cref="Forever"/>.</returns>
	public static DateTime Until(QuietSpan span, DateTime now) => span switch
	{
		QuietSpan.HalfHour => now.AddMinutes(30),
		QuietSpan.Hour => now.AddHours(1),
		QuietSpan.Morning => Morning(now.ToLocalTime()).ToUniversalTime(),
		QuietSpan.Forever => Forever,
	};

	/// <param name="until">Конец тишины из настроек; null — не включали.</param>
	public static bool On(DateTime? until, DateTime now) => until > now;

	private static DateTime Morning(DateTime local) => local.Date.AddHours(local.Hour < _morning ? _morning : 24 + _morning);
}
