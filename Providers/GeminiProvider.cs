using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace AgentVitals;

/// <summary>
/// Квоты Gemini CLI по его OAuth-токену. Не проверено на живом аккаунте: эндпоинты и поля — из
/// ClaudeBar (gemini.json). Свой refresh не делаем: токен обновляет сам gemini.
/// </summary>
internal sealed class GeminiProvider : IProvider
{
	private const string _api = "https://cloudcode-pa.googleapis.com/v1internal:";

	private static readonly string _credentials = Path.Combine(Paths.Home, ".gemini", "oauth_creds.json");

	public string Name => "Gemini";

	public string Cli => "gemini";

	public string Credentials => _credentials;

	public bool Available => File.Exists(_credentials);

	/// <inheritdoc/>
	public async Task<Snapshot> FetchAsync(HttpClient http, CancellationToken cancellation)
	{
		if ((string?)JsonNode.Parse(await SharedFile.ReadAllTextAsync(_credentials, cancellation))?["access_token"] is not { Length: > 0 } token)
		{
			throw new TokenExpiredException(Cli);
		}

		var load = await PostAsync(http, token, "loadCodeAssist", new JsonObject
		{
			["metadata"] = new JsonObject
			{
				["pluginType"] = "GEMINI",
			},
		}, cancellation);

		// Без проекта Google отвечает заглушками «100%», поэтому сначала узнаём его.
		var project = load["cloudaicompanionProject"] switch
		{
			JsonValue value => (string?)value,
			JsonObject inner => (string?)inner["id"],
			_ => null,
		};

		var body = project is null
			? new JsonObject()
			: new JsonObject
			{
				["project"] = project,
			};

		return Parse(await PostAsync(http, token, "retrieveUserQuota", body, cancellation), (string?)load["currentTier"]?["name"]);
	}

	internal static Snapshot Parse(JsonNode quota, string? tier)
	{
		var windows = (quota["buckets"] as JsonArray ?? [])
			.OfType<JsonObject>()
			.Select(bucket => (
				Tier: Tier((string?)bucket["modelId"] ?? ""),
				Left: (double?)bucket["remainingFraction"] ?? 1,
				Resets: Api.Date(bucket["resetTime"])))
			.GroupBy(bucket => bucket.Tier)
			.Select(group => group.MinBy(bucket => bucket.Left))
			.Select(bucket => new QuotaWindow("langWindowModel", bucket.Tier, (1 - bucket.Left) * 100, bucket.Resets, Limiting: true))
			.OrderByDescending(window => window.Used)
			.ToList();

		return new(tier ?? "", null, windows, null);
	}

	private static string Tier(string model) =>
		model.Contains("flash-lite", StringComparison.OrdinalIgnoreCase) ? "Flash Lite"
		: model.Contains("flash", StringComparison.OrdinalIgnoreCase) ? "Flash"
		: model.Contains("pro", StringComparison.OrdinalIgnoreCase) ? "Pro"
		: model;

	private async Task<JsonNode> PostAsync(HttpClient http, string token, string method, JsonObject body, CancellationToken cancellation)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, _api + method)
		{
			Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
		};

		request.Headers.Authorization = new("Bearer", token);

		return await Api.SendAsync(http, request, Cli, cancellation);
	}
}
