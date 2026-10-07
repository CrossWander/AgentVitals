using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace AgentVitals;

/// <summary>
/// Квоты Codex (ChatGPT) по токену Codex CLI. Не проверено на живом аккаунте: эндпоинт и поля —
/// из ClaudeBar (codex.json). Пока файла авторизации нет, провайдер скрыт.
/// </summary>
internal sealed class CodexProvider : IProvider
{
	private const string _url = "https://chatgpt.com/backend-api/wham/usage";

	public string Name => "Codex";

	public string Cli => "codex";

	public string Credentials => Auth;

	private static string Auth => Path.Combine(
		Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } home ? home : Path.Combine(Paths.Home, ".codex"),
		"auth.json");

	/// <inheritdoc/>
	public async Task<Snapshot> FetchAsync(HttpClient http, CancellationToken cancellation)
	{
		var tokens = JsonNode.Parse(await SharedFile.ReadAllTextAsync(Auth, cancellation))?["tokens"];

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

		return Parse(await Api.SendAsync(http, request, Cli, cancellation));
	}

	internal static Snapshot Parse(JsonNode usage)
	{
		List<QuotaWindow> windows = [];
		Add(windows, usage["rate_limit"]?["primary_window"], "langWindowSession");
		Add(windows, usage["rate_limit"]?["secondary_window"], "langWindowWeek");

		return new(Quota.PlanName((string?)usage["plan_type"]), null, windows, null);
	}

	private static void Add(List<QuotaWindow> windows, JsonNode? window, string key)
	{
		if (window is not JsonObject)
		{
			return;
		}

		DateTimeOffset? resets = (long?)window["reset_at"] is { } at ? DateTimeOffset.FromUnixTimeSeconds(at) : null;
		windows.Add(new(key, null, (double?)window["used_percent"] ?? 0, resets, Limiting: true));
	}
}
