using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace AgentVitals;

/// <summary>
/// Квоты Claude по OAuth-токену Claude Code. Токен только читается: обновляет его сам CLI, а
/// одноразовый refresh-токен, потраченный нами, выбил бы из входа сам Claude Code.
/// </summary>
internal sealed class ClaudeProvider : IProvider
{
	private const string _url = "https://api.anthropic.com/api/oauth/usage";

	private static readonly string _credentials = Path.Combine(Paths.Home, ".claude", ".credentials.json");
	private static readonly string _account = Path.Combine(Paths.Home, ".claude.json");

	public string Name => "Claude";

	public string Cli => "claude";

	public string Credentials => _credentials;

	public string? Dashboard => "https://claude.ai/settings/usage";

	public IUsageLog? Usage { get; } = new ClaudeLog();

	/// <inheritdoc/>
	public async Task<Snapshot> FetchAsync(HttpClient http, CancellationToken cancellation)
	{
		// Файл перечитывается каждый раз: Claude Code меняет токен, не спрашивая нас.
		var oauth = JsonNode.Parse(await SharedFile.ReadAllTextAsync(_credentials, cancellation))?["claudeAiOauth"]
			?? throw new TokenExpiredException(Cli);

		var token = (string?)oauth["accessToken"];
		var expires = DateTimeOffset.FromUnixTimeMilliseconds((long?)oauth["expiresAt"] ?? 0);

		// Истёкший токен сервер всё равно отклонит — незачем тратить на это запрос.
		if (string.IsNullOrEmpty(token) || expires <= DateTimeOffset.Now)
		{
			throw new TokenExpiredException(Cli);
		}

		using var request = new HttpRequestMessage(HttpMethod.Get, _url);
		request.Headers.Authorization = new("Bearer", token);
		request.Headers.Add("anthropic-beta", "oauth-2025-04-20");

		var usage = await Api.SendAsync(http, request, Cli, cancellation);

		return Parse(usage, Quota.PlanName((string?)oauth["subscriptionType"]), await ReadAccountAsync(cancellation));
	}

	/// <summary>Разобрать ответ /api/oauth/usage: окна из limits[], без них — из five_hour и seven_day.</summary>
	internal static Snapshot Parse(JsonNode usage, string plan, string? account)
	{
		List<QuotaWindow> windows = [];

		foreach (var limit in (usage["limits"] as JsonArray ?? []).OfType<JsonObject>())
		{
			var used = (double?)limit["percent"] ?? 0;
			var resets = Api.Date(limit["resets_at"]);

			switch ((string?)limit["kind"])
			{
				case "session":
					windows.Add(new(WindowKind.Session, null, used, resets, Limiting: true));
					break;

				case "weekly_all":
					windows.Add(new(WindowKind.Week, null, used, resets, Limiting: true));
					break;

				// Модельный лимит блокирует только свою модель; два лимита одной модели — первый.
				case "weekly_scoped" when Model(limit) is { } model && windows.All(window => window.Label != model):
					windows.Add(new(WindowKind.WeekModel, model, used, resets, Limiting: false));
					break;
			}
		}

		if (windows.Count == 0)
		{
			Legacy(windows, usage["five_hour"], WindowKind.Session);
			Legacy(windows, usage["seven_day"], WindowKind.Week);
		}

		var extra = Extra(usage);

		// Ответ сменил форму: ошибка разбора оставит прежний снимок и скажет об этом в окне и в журнале.
		if (windows.Count == 0 && extra is null)
		{
			throw new JsonException("usage: no quota windows");
		}

		return new(plan, account, windows, extra);
	}

	// «Fable 5» и «Fable» — одна модель: в карточке хватает первого слова.
	private static string? Model(JsonObject limit) =>
		((string?)limit["scope"]?["model"]?["display_name"])?
			.Split(' ', StringSplitOptions.RemoveEmptyEntries)
			.FirstOrDefault();

	private static void Legacy(List<QuotaWindow> windows, JsonNode? window, WindowKind kind)
	{
		if (window is JsonObject && (double?)window["utilization"] is { } used)
		{
			windows.Add(new(kind, null, used, Api.Date(window["resets_at"]), Limiting: true));
		}
	}

	private static ExtraUsage? Extra(JsonNode usage)
	{
		if (usage["spend"] is JsonObject spend && (bool?)spend["enabled"] == true)
		{
			return new(Money(spend["used"]) ?? 0, Money(spend["limit"]), (string?)spend["used"]?["currency"] ?? "USD");
		}

		if (usage["extra_usage"] is JsonObject extra && (bool?)extra["is_enabled"] == true)
		{
			var scale = Scale((int?)extra["decimal_places"] ?? 2);

			return new(
				((decimal?)extra["used_credits"] ?? 0) / scale,
				(decimal?)extra["monthly_limit"] / scale,
				(string?)extra["currency"] ?? "USD");
		}

		return null;
	}

	private static decimal? Money(JsonNode? money) =>
		money is JsonObject && (decimal?)money["amount_minor"] is { } minor
			? minor / Scale((int?)money["exponent"] ?? 2)
			: null;

	private static decimal Scale(int places) => (decimal)Math.Pow(10, places);

	// Почта аккаунта — для подписи в поповере; без неё всё работает.
	private static async Task<string?> ReadAccountAsync(CancellationToken cancellation)
	{
		try
		{
			return File.Exists(_account)
				? (string?)JsonNode.Parse(await SharedFile.ReadAllTextAsync(_account, cancellation))?["oauthAccount"]?["emailAddress"]
				: null;
		}
		catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException)
		{
			return null;
		}
	}
}
