using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;

namespace AgentVitals.Core.Quotas;

/// <summary>Вид окна квоты: от него длина окна, название и значок.</summary>
public enum WindowKind
{
	Session,
	Week,

	/// <summary>Суточное окно (Codex называет окна по длине: сутки бывают на некоторых планах).</summary>
	Day,

	/// <summary>Окно на 30 дней — например, единственный лимит бесплатного плана Codex.</summary>
	Month,

	/// <summary>Месячная квота одной функции (Label — её сырое имя: chat, completions…; перевод — при показе).</summary>
	MonthFeature,

	/// <summary>Недельный лимит одной модели (Label — её имя).</summary>
	WeekModel,

	/// <summary>Лимит модели неизвестной длины (Label — её имя).</summary>
	Model,
}

/// <summary>Окно квоты: сколько израсходовано и когда сброс. Label — имя модели у модельных окон.</summary>
/// <remarks>Limiting — окно блокирует всю работу (сессия, неделя), а не одну модель. Used может быть больше 100.</remarks>
public sealed record QuotaWindow(WindowKind Kind, string? Label, double Used, DateTimeOffset? ResetsAt, bool Limiting)
{
	/// <summary>Длина окна; null — неизвестна.</summary>
	[JsonIgnore]
	public TimeSpan? Length => LengthOf(Kind);

	/// <summary>Длина окна этого вида; null — у вида её нет.</summary>
	public static TimeSpan? LengthOf(WindowKind kind) => kind switch
	{
		WindowKind.Session => TimeSpan.FromHours(5),
		WindowKind.Day => TimeSpan.FromDays(1),
		WindowKind.Week or WindowKind.WeekModel => TimeSpan.FromDays(7),
		// ponytail: месяц — 30 дней; в 31-дневном метки темпа в первые сутки нет, в феврале она чуть отстаёт.
		WindowKind.Month or WindowKind.MonthFeature => TimeSpan.FromDays(30),
		WindowKind.Model => null,
	};

	/// <summary>Окно на этот момент. Сброс прошёл, а свежего снимка нет — после сна вход истёк или нет сети: окно точно обнулилось.</summary>
	/// <remarks>ponytail: расход с других устройств после сброса не виден, пока не придёт новый снимок.</remarks>
	public QuotaWindow At(DateTime now) => ResetsAt?.UtcDateTime <= now ? this with { Used = 0 } : this;
}

/// <summary>Расход сверх тарифа в деньгах; Limit null — потолка нет. Team — общий пул команды (Cursor), а не свой расход.</summary>
public sealed record ExtraUsage(decimal Used, decimal? Limit, string Currency, bool Team = false);

/// <summary>Квоты одного провайдера на момент запроса.</summary>
public sealed record Snapshot(string Plan, string? Account, IReadOnlyList<QuotaWindow> Windows, ExtraUsage? Extra);

/// <summary>Темп расхода против ровного: с запасом, ровно, с опережением.</summary>
public enum Pace
{
	Spare,
	Even,
	Ahead,
}

/// <summary>Состояние квот или сервиса Anthropic.</summary>
public enum Health
{
	Unknown,
	Good,
	Warning,
	Critical,
}

public static class Quota
{
	/// <summary>Окно, которое ограничивает сильнее всех: среди блокирующих всё, а если таких нет — среди всех.</summary>
	public static QuotaWindow? Bottleneck(IReadOnlyList<QuotaWindow> windows) =>
		(windows.Any(window => window.Limiting) ? windows.Where(window => window.Limiting) : windows)
			.MaxBy(window => window.Used);

	public static Health Rate(double used, Thresholds levels) =>
		used >= levels.Critical ? Health.Critical : used >= levels.Important ? Health.Warning : Health.Good;

	/// <summary>Сколько процентов было бы потрачено к этому моменту при ровном темпе; null — длина окна неизвестна
	/// или сброс уже прошёл.</summary>
	public static double? Expected(QuotaWindow window, DateTime now)
	{
		if (window.Length is not { } span || window.ResetsAt is not { } resets)
		{
			return null;
		}

		var left = resets.UtcDateTime - now;

		return left <= TimeSpan.Zero || left > span ? null : (1 - (left / span)) * 100;
	}

	/// <summary>Когда окно кончится, если тратить, как до сих пор; null — темп не опережает ровный (тогда хватит до
	/// сброса), окно уже исчерпано или длина и сброс не известны.</summary>
	/// <remarks>ponytail: средний темп с начала окна; рывок в последние минуты виден не сразу.</remarks>
	public static DateTime? Runout(QuotaWindow window, DateTime now)
	{
		if (Expected(window, now) is not { } expected || Pace(window.Used, expected) != Quotas.Pace.Ahead || window.Used >= 100)
		{
			return null;
		}

		var elapsed = window.Length!.Value - (window.ResetsAt!.Value.UtcDateTime - now);

		return elapsed <= TimeSpan.Zero ? null : now + (elapsed * ((100 - window.Used) / window.Used));
	}

	/// <summary>Темп расхода: дальше пяти пунктов от ровного — уже не ровно.</summary>
	public static Pace Pace(double used, double expected) =>
		used > expected + 5 ? Quotas.Pace.Ahead : used < expected - 5 ? Quotas.Pace.Spare : Quotas.Pace.Even;

	/// <summary>Тариф для показа: «team» → «Team», «pro_plus» → «Pro Plus».</summary>
	public static string PlanName(string? subscription) =>
		string.IsNullOrEmpty(subscription) ? "" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(subscription.Replace('_', ' '));
}
