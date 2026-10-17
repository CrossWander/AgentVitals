using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace AgentVitals;

/// <summary>
/// Квоты Codex (ChatGPT) по токену Codex CLI: wham/usage, как у самого Codex (codex-rs/backend-client).
/// Пока файла авторизации нет, провайдер скрыт.
/// </summary>
internal sealed class CodexProvider : IProvider
{
	private const string _url = "https://chatgpt.com/backend-api/wham/usage";

	public string Name => "Codex";

	public string Cli => "codex";

	/// <summary>Папка Codex: $CODEX_HOME, а без неё ~/.codex — как у самого Codex CLI.</summary>
	internal static string Home { get; } =
		Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } home ? home : Path.Combine(Paths.Home, ".codex");

	public string Credentials { get; } = Path.Combine(Home, "auth.json");

	public string? Dashboard => "https://chatgpt.com/codex/settings/usage";

	public IUsageLog? Usage { get; } = new CodexLog();

	public IStatusPage? Status { get; } = new OpenAiStatus();

	/// <inheritdoc/>
	public async Task<Snapshot> FetchAsync(HttpClient http, CancellationToken cancellation)
	{
		var tokens = JsonNode.Parse(await SharedFile.ReadAllTextAsync(Credentials, cancellation))?["tokens"];

		if ((string?)tokens?["access_token"] is not { Length: > 0 } token)
		{
			throw new TokenExpiredException(Cli);
		}

		using var request = new HttpRequestMessage(HttpMethod.Get, _url);
		request.Headers.Authorization = new("Bearer", token);

		if ((string?)tokens["account_id"] is { Length: > 0 } account)
		{
			request.Headers.Add("ChatGPT-Account-Id", account);
		}

		return Parse(await Api.SendAsync(http, request, Cli, cancellation)) with { Account = Email((string?)tokens["id_token"]) };
	}

	/// <summary>Почта из id_token Codex CLI. Не разобралась — null, квоты и без неё.</summary>
	internal static string? Email(string? idToken) => Api.Claim(idToken, "email");

	internal static Snapshot Parse(JsonNode usage)
	{
		List<QuotaWindow> windows = [];
		Add(windows, usage["rate_limit"]?["primary_window"], WindowKind.Session);
		Add(windows, usage["rate_limit"]?["secondary_window"], WindowKind.Week);

		return new(Quota.PlanName((string?)usage["plan_type"]), null, windows, null);
	}

	/// <param name="fallback">Вид окна, если сервер не сказал его длину: первое — сессия, второе — неделя.</param>
	private static void Add(List<QuotaWindow> windows, JsonNode? window, WindowKind fallback)
	{
		if (window is not JsonObject)
		{
			return;
		}

		DateTimeOffset? resets = (long?)window["reset_at"] is { } at ? DateTimeOffset.FromUnixTimeSeconds(at) : null;
		windows.Add(new(Named(window["limit_window_seconds"]) ?? fallback, null, (double?)window["used_percent"] ?? 0, resets, Limiting: true));
	}

	/// <summary>Вид окна по его длине, с допуском ±5%, как в самом Codex: у бесплатного плана первое окно — 30 дней,
	/// у Pro пятичасового нет вовсе, так что место окна в ответе ничего не говорит.</summary>
	private static WindowKind? Named(JsonNode? seconds) =>
		(double?)seconds is { } length
			? new[] { WindowKind.Session, WindowKind.Day, WindowKind.Week, WindowKind.Month }
				.Where(kind => QuotaWindow.LengthOf(kind) is { } known && Math.Abs(length - known.TotalSeconds) <= known.TotalSeconds * 0.05)
				.Cast<WindowKind?>()
				.FirstOrDefault()
			: null;
}
