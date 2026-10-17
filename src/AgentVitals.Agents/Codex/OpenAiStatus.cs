using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AgentVitals.Core.Quotas;
using AgentVitals.Core.Status;
using AgentVitals.Platform;

namespace AgentVitals.Agents.Codex;

/// <summary>
/// Статус Codex на status.openai.com (incident.io). Общий индикатор OpenAI говорит и о сбоях ChatGPT, картинок,
/// Compliance API, поэтому берётся только группа «Codex» из фида страницы. Группы нет — общий статус OpenAI.
/// </summary>
internal sealed class OpenAiStatus : IStatusPage
{
	private const string _group = "Codex";

	// Запасной путь пишется в журнал один раз, а не каждые пять минут.
	private bool _fellBack;

	public string Name => _group;

	public string Url => "https://status.openai.com";

	public async Task<ServiceStatus> FetchAsync(HttpClient http, CancellationToken cancellation)
	{
		if (Parse(JsonNode.Parse(await http.GetStringAsync(Url + "/proxy/status.openai.com", cancellation))) is { } codex)
		{
			_fellBack = false;

			return codex;
		}

		if (!_fellBack)
		{
			Journal.Add("status: no Codex group on status.openai.com, showing the whole OpenAI page");
			_fellBack = true;
		}

		return StatusPage.Parse(JsonNode.Parse(await http.GetStringAsync(Url + "/api/v2/summary.json", cancellation)), "OpenAI");
	}

	/// <summary>Статус группы Codex: худший из её компонентов и инцидент о ней; null — группы в фиде нет.</summary>
	internal static ServiceStatus? Parse(JsonNode? feed)
	{
		var summary = feed?["summary"];
		var group = (summary?["structure"]?["items"] as JsonArray)?
			.Select(item => item?["group"])
			.FirstOrDefault(group => (string?)group?["name"] == _group);

		if (group?["components"] is not JsonArray components)
		{
			return null;
		}

		var ids = components.Select(component => (string?)component?["component_id"]).OfType<string>().ToHashSet();
		bool Ours(JsonNode? affected) => ids.Contains((string?)affected?["component_id"] ?? "");

		// «Всё в порядке» — первым: худшее его перебьёт, а без затронутых компонентов оно и останется.
		var (indicator, description) = ((summary?["affected_components"] as JsonArray) ?? [])
			.Where(Ours)
			.Select(affected => Of((string?)affected?["status"]))
			.Prepend(Of(null))
			.MaxBy(of => new ServiceStatus(of.Indicator, "", null).Health);

		var incident = ((summary?["ongoing_incidents"] as JsonArray) ?? [])
			.FirstOrDefault(incident => ((incident?["affected_components"] as JsonArray) ?? []).Any(Ours));

		return new(indicator, description, (string?)incident?["name"], _group);
	}

	/// <summary>Состояние компонента incident.io — в индикатор Statuspage и его слова.</summary>
	private static (string Indicator, string Description) Of(string? status) => status switch
	{
		"degraded_performance" => ("minor", "Degraded Performance"),
		"under_maintenance" => ("maintenance", "Under Maintenance"),
		"partial_outage" => ("major", "Partial Outage"),
		"full_outage" => ("critical", "Major Outage"),
		_ => ("none", ""),
	};
}
