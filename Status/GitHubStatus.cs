using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace AgentVitals;

/// <summary>
/// Статус Copilot на www.githubstatus.com (Statuspage). Общий индикатор GitHub говорит и об Actions, Pages и прочем,
/// поэтому берётся только компонент «Copilot». Компонента нет — общий статус GitHub.
/// </summary>
internal sealed class GitHubStatus : IStatusPage
{
	private const string _component = "Copilot";

	// Запасной путь пишется в журнал один раз, а не каждые пять минут.
	private bool _fellBack;

	public string Name => _component;

	public string Url => "https://www.githubstatus.com";

	public async Task<ServiceStatus> FetchAsync(HttpClient http, CancellationToken cancellation)
	{
		var summary = JsonNode.Parse(await http.GetStringAsync(Url + "/api/v2/summary.json", cancellation));

		if (Parse(summary) is { } copilot)
		{
			_fellBack = false;

			return copilot;
		}

		if (!_fellBack)
		{
			Journal.Add("status: no Copilot component on www.githubstatus.com, showing the whole GitHub page");
			_fellBack = true;
		}

		return StatusPage.Parse(summary, "GitHub");
	}

	/// <summary>Статус компонента Copilot и инцидент о нём; null — компонента на странице нет.</summary>
	internal static ServiceStatus? Parse(JsonNode? summary)
	{
		var component = ((summary?["components"] as JsonArray) ?? []).FirstOrDefault(component => (string?)component?["name"] == _component);

		if (component is null)
		{
			return null;
		}

		var id = (string?)component["id"];
		var (indicator, description) = Of((string?)component["status"]);
		var incident = ((summary?["incidents"] as JsonArray) ?? [])
			.FirstOrDefault(incident => ((incident?["components"] as JsonArray) ?? []).Any(affected => id is not null && (string?)affected?["id"] == id));

		return new(indicator, description, (string?)incident?["name"], _component);
	}

	/// <summary>Состояние компонента Statuspage — в индикатор страницы и его слова.</summary>
	private static (string Indicator, string Description) Of(string? status) => status switch
	{
		"degraded_performance" => ("minor", "Degraded Performance"),
		"partial_outage" => ("major", "Partial Outage"),
		"major_outage" => ("critical", "Major Outage"),
		"under_maintenance" => ("maintenance", "Under Maintenance"),
		_ => ("none", ""),
	};
}
