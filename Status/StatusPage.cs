using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace AgentVitals;

/// <summary>Состояние сервисов Anthropic со статус-страницы.</summary>
internal sealed record ServiceStatus(string Indicator, string Description, string? Incident)
{
	public Health Health => Indicator switch
	{
		"none" => Health.Good,
		"minor" or "maintenance" => Health.Warning,
		"major" or "critical" => Health.Critical,
		_ => Health.Unknown,
	};
}

/// <summary>status.claude.com (Statuspage API); status.anthropic.com редиректит сюда.</summary>
internal static class StatusPage
{
	public const string Url = "https://status.claude.com";

	public static async Task<ServiceStatus> FetchAsync(HttpClient http, CancellationToken cancellation) =>
		Parse(JsonNode.Parse(await http.GetStringAsync(Url + "/api/v2/summary.json", cancellation)));

	internal static ServiceStatus Parse(JsonNode? summary) => new(
		(string?)summary?["status"]?["indicator"] ?? "none",
		(string?)summary?["status"]?["description"] ?? "",
		(string?)(summary?["incidents"] as JsonArray)?.FirstOrDefault()?["name"]);
}
