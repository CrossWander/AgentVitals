using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AgentVitals.Core.Quotas;

namespace AgentVitals.Core.Status;

/// <summary>Состояние сервисов со статус-страницы. Name — чьё: «Anthropic», «Codex» или «OpenAI».</summary>
public sealed record ServiceStatus(string Indicator, string Description, string? Incident, string Name = "")
{
	public Health Health => Indicator switch
	{
		"none" => Health.Good,
		"minor" or "maintenance" => Health.Warning,
		"major" or "critical" => Health.Critical,
		_ => Health.Unknown,
	};
}

/// <summary>Страница здоровья сервиса агента.</summary>
public interface IStatusPage
{
	/// <summary>Чей статус — в подписях и тостах, в том числе когда страница не ответила.</summary>
	string Name { get; }

	/// <summary>Что открыть по щелчку на строке статуса.</summary>
	string Url { get; }

	/// <exception cref="HttpRequestException">Страница не ответила.</exception>
	Task<ServiceStatus> FetchAsync(HttpClient http, CancellationToken cancellation);
}

/// <summary>Страница на Statuspage (API v2), как status.claude.com; status.anthropic.com редиректит туда.</summary>
public sealed class StatusPage(string name, string url) : IStatusPage
{
	public string Name => name;

	public string Url => url;

	public async Task<ServiceStatus> FetchAsync(HttpClient http, CancellationToken cancellation) =>
		Parse(JsonNode.Parse(await http.GetStringAsync(url + "/api/v2/summary.json", cancellation)), name);

	public static ServiceStatus Parse(JsonNode? summary, string name) => new(
		(string?)summary?["status"]?["indicator"] ?? "none",
		(string?)summary?["status"]?["description"] ?? "",
		(string?)(summary?["incidents"] as JsonArray)?.FirstOrDefault()?["name"],
		name);
}
