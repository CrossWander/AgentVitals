using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace AgentVitals;

/// <summary>Общее для запросов к API агентов: заголовки, коды входа и лимита, разбор JSON.</summary>
internal static class Api
{
	private static readonly string _agent = $"AgentVitals/{typeof(Api).Assembly.GetName().Version?.ToString(3)}";

	/// <summary>Отправить запрос и разобрать ответ; 401/403 — вход истёк, 429 — ждать.</summary>
	public static async Task<JsonNode> SendAsync(HttpClient http, HttpRequestMessage request, string cli, CancellationToken cancellation)
	{
		request.Headers.Accept.ParseAdd("application/json");
		request.Headers.UserAgent.ParseAdd(_agent);

		using var response = await http.SendAsync(request, cancellation);

		if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
		{
			throw new TokenExpiredException(cli);
		}

		if (response.StatusCode == HttpStatusCode.TooManyRequests)
		{
			throw new RateLimitedException(RetryAt(response.Headers.RetryAfter, DateTime.Now));
		}

		response.EnsureSuccessStatusCode();

		return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellation))
			?? throw new JsonException("empty response");
	}

	/// <summary>Когда можно снова: по Retry-After, а без него, с нулём или с прошедшим временем — через пять минут.</summary>
	public static DateTime RetryAt(RetryConditionHeaderValue? retry, DateTime now)
	{
		var at = retry?.Date is { } date
			? date.LocalDateTime
			: retry?.Delta is { } delta ? now + delta : now;

		return at > now ? at : now.AddMinutes(5);
	}

	public static DateTimeOffset? Date(JsonNode? node) =>
		node is JsonValue value
		&& value.TryGetValue<string>(out var text)
		&& DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
			? date
			: null;
}
