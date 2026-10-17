using System.Text;
using Xunit;

namespace AgentVitals.Tests;

/// <summary>Только разбор пароля: настоящий «Диспетчер учётных данных» тесты не трогают.</summary>
public class CredentialManagerTests
{
	[Fact]
	public void DecodesUtf8() => Assert.Equal("{\"a\":\"ё\"}", CredentialManager.Decode(Encoding.UTF8.GetBytes("{\"a\":\"ё\"}")));

	[Fact]
	public void DecodesUtf16WhenItHasZeros() => Assert.Equal("gho_abc", CredentialManager.Decode(Encoding.Unicode.GetBytes("gho_abc")));

	[Fact]
	public void EmptyBlobIsNoSecret() => Assert.Null(CredentialManager.Decode([])); // пустой токен ушёл бы на сервер и вернулся 401
}
