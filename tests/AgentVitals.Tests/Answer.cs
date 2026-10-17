using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AgentVitals.Tests;

/// <summary>Что ушло на сервер: адрес и заголовки входа.</summary>
internal sealed record Sent(Uri Url, string? Authorization, string? Cookie);

/// <summary>Сервер, который на всё отвечает одним и тем же и запоминает, что у него спросили.</summary>
/// <param name="headers">Дописать заголовки ответа — например, остаток лимита запросов.</param>
internal sealed class Answer(HttpStatusCode status, string body = "{}", Action<HttpResponseMessage>? headers = null) : HttpMessageHandler
{
	public List<Sent> Requests { get; } = [];

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		Requests.Add(new(
			request.RequestUri!,
			request.Headers.Authorization?.ToString(),
			request.Headers.TryGetValues("Cookie", out var cookie) ? string.Join("; ", cookie) : null));

		var response = new HttpResponseMessage(status)
		{
			Content = new StringContent(body, Encoding.UTF8, "application/json"),
			RequestMessage = request,
		};

		headers?.Invoke(response);

		return Task.FromResult(response);
	}
}
