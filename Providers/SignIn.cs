using System;

namespace AgentVitals;

/// <summary>Что не так со входом агента.</summary>
public enum SignInProblem
{
	/// <summary>Вход истёк: обновит его сам CLI агента.</summary>
	Expired,

	/// <summary>Входа нет вовсе.</summary>
	Missing,

	/// <summary>Сервер вход не принял.</summary>
	Rejected,

	/// <summary>Вход есть, а самого агента у аккаунта нет — как Copilot у аккаунта GitHub без него.</summary>
	NoSubscription,
}

/// <summary>Со входом агента что-то не так; что сказать человеку, решает интерфейс по причине и агенту.</summary>
/// <param name="cli">Чем обновляют вход: команда CLI агента, у Copilot — git.</param>
/// <remarks>Токена в сообщении нет никогда: оно уходит в журнал.</remarks>
internal sealed class SignInException(string cli, SignInProblem problem = SignInProblem.Expired) : Exception($"{cli}: sign-in {problem}")
{
	public string Cli { get; } = cli;

	public SignInProblem Problem { get; } = problem;
}

/// <summary>Сервер просит подождать до указанного момента.</summary>
internal sealed class RateLimitedException(DateTime until) : Exception($"rate limited until {until.ToLocalTime():HH:mm}")
{
	public DateTime Until { get; } = until;
}
