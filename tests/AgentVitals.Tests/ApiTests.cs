using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AgentVitals.Tests;

public class ApiTests
{
	[Fact]
	public async Task RefusalIsAskedFirst() // 404 у Copilot — «нет Copilot», а не «нет связи»
	{
		using var http = new HttpClient(new Answer(HttpStatusCode.NotFound));
		using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");

		var error = await Assert.ThrowsAsync<SignInException>(() => Api.SendAsync(http, request, "git", CancellationToken.None,
			response => response.StatusCode == HttpStatusCode.NotFound ? new SignInException("git", SignInProblem.NoSubscription) : null));

		Assert.Equal(SignInProblem.NoSubscription, error.Problem);
	}

	[Fact]
	public async Task WithoutRefusalForbiddenStillExpires()
	{
		using var http = new HttpClient(new Answer(HttpStatusCode.Forbidden));
		using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");

		var error = await Assert.ThrowsAsync<SignInException>(() => Api.SendAsync(http, request, "claude", CancellationToken.None));

		Assert.Equal(SignInProblem.Expired, error.Problem);
	}
}
