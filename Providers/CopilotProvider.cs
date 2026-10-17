using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace AgentVitals;

/// <summary>
/// Месячные квоты GitHub Copilot по входу GitHub, который уже хранит git в «Диспетчере учётных данных».
/// Токен читается заново перед каждым запросом и уходит в один-единственный адрес; нигде не хранится и не пишется.
/// Адрес недокументированный — так его зовёт сам ClaudeBar.
/// </summary>
internal sealed class CopilotProvider : IProvider
{
	/// <summary>Запись Git Credential Manager для github.com. Других записей не читаем и не перебираем.</summary>
	internal const string Target = "git:https://github.com";

	private const string _url = "https://api.github.com/copilot_internal/user";
	private const string _cli = "git";

	// Порядок карточек: премиум-запросы — главное на платных планах; незнакомые снимки — после, как пришли.
	private static readonly string[] _order = ["premium_interactions", "chat", "completions"];

	// Признаки клиентов Copilot: VS Code, Visual Studio и JetBrains, Copilot CLI.
	private static readonly string[] _signs =
	[
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Code", "User", "globalStorage", "github.copilot-chat"),
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "github-copilot"),
		Path.Combine(Paths.Home, ".copilot"),
	];

	public string Name => "Copilot";

	public string Cli => _cli;

	// Вход не в файле, а в «Диспетчере учётных данных»: следить не за чем.
	public string? Credentials => null;

	public string? Dashboard => "https://github.com/settings/copilot";

	public IUsageLog? Usage => null;

	public IStatusPage? Status { get; } = new GitHubStatus();

	/// <summary>Спрашивать, когда есть клиент Copilot: без входа git ответом будет «Нет входа GitHub», и следующий
	/// вопрос — только когда вход появится. «Диспетчер учётных данных» есть лишь у Windows.</summary>
	public bool Installed => OperatingSystem.IsWindows() && _signs.Any(Directory.Exists);

	/// <summary>Git перезаписал вход — пора спросить снова; сам токен для этого не хранится.</summary>
	public DateTime SignedIn => CredentialManager.Written(Target) ?? DateTime.MinValue;

	// Copilot Chat встроен в VS Code, так что папка-признак есть почти у каждого: вкладка — только после ответа GitHub.
	public bool ConfirmFirst => true;

	/// <inheritdoc/>
	public async Task<Snapshot> FetchAsync(HttpClient http, CancellationToken cancellation) =>
		CredentialManager.Read(Target) is { } login
			? await AskAsync(http, login.Secret, cancellation)
			: throw new SignInException(Cli, SignInProblem.Missing);

	/// <summary>Один GET с токеном на самом запросе — не в общем клиенте: в другие запросы он не попадёт, а при
	/// редиректе .NET заголовок входа не переносит.</summary>
	internal static async Task<Snapshot> AskAsync(HttpClient http, string token, CancellationToken cancellation)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, _url);
		request.Headers.Authorization = new("token", token);

		return Parse(await Api.SendAsync(http, request, _cli, cancellation, Refusal));
	}

	/// <summary>Отказы GitHub: 401 — вход отклонён, 403 и 404 — Copilot у аккаунта нет, а 403 с исчерпанным лимитом
	/// запросов — ждать, как 429. null — не отказ.</summary>
	internal static Exception? Refusal(HttpResponseMessage response) => response.StatusCode switch
	{
		HttpStatusCode.Unauthorized => new SignInException(_cli, SignInProblem.Rejected),
		HttpStatusCode.Forbidden when response.Headers.TryGetValues("X-RateLimit-Remaining", out var left) && left.FirstOrDefault() == "0" =>
			new RateLimitedException(Reset(response) ?? Api.RetryAt(response.Headers.RetryAfter, DateTime.UtcNow)),
		HttpStatusCode.Forbidden or HttpStatusCode.NotFound => new SignInException(_cli, SignInProblem.NoSubscription),
		_ => null,
	};

	/// <summary>Когда GitHub вернёт лимит запросов: X-RateLimit-Reset в секундах Unix; нет или уже прошёл — null.</summary>
	private static DateTime? Reset(HttpResponseMessage response) =>
		response.Headers.TryGetValues("X-RateLimit-Reset", out var values)
		&& long.TryParse(values.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
		&& DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime is var at
		&& at > DateTime.UtcNow
			? at
			: null;

	/// <summary>Окно на каждый ограниченный снимок квоты. Ни окна, ни платного плана — Copilot у аккаунта нет.</summary>
	/// <exception cref="SignInException">GitHub не подтвердил Copilot.</exception>
	internal static Snapshot Parse(JsonNode user)
	{
		// Дата без времени — полночь UTC: так GitHub и сбрасывает.
		var resets = Api.Date(user["quota_reset_date_utc"]) ?? Api.Date(user["quota_reset_date"], DateTimeStyles.AssumeUniversal);

		var windows = ((user["quota_snapshots"] as JsonObject) ?? [])
			.Select(pair => (Name: pair.Key, Quota: pair.Value as JsonObject))
			.Where(snapshot => snapshot.Quota is not null && (bool?)snapshot.Quota["unlimited"] != true && ((double?)snapshot.Quota["entitlement"] ?? 0) > 0)
			.Select(snapshot => (snapshot.Name, Left: Left(snapshot.Quota!)))
			.Where(snapshot => snapshot.Left is not null)
			.OrderBy(snapshot => Array.IndexOf(_order, snapshot.Name) is var at and >= 0 ? at : _order.Length)
			.Select(snapshot => new QuotaWindow(WindowKind.MonthFeature, snapshot.Name, 100 - snapshot.Left!.Value, resets, Limiting: true))
			.ToList();

		var sku = (string?)user["access_type_sku"] ?? "";
		var free = sku.StartsWith("free", StringComparison.OrdinalIgnoreCase);

		if (windows.Count == 0 && (free || sku.Length == 0))
		{
			throw new SignInException(_cli, SignInProblem.NoSubscription);
		}

		return new(free ? "Free" : Quota.PlanName((string?)user["copilot_plan"]), (string?)user["login"], windows, null);
	}

	/// <summary>Сколько процентов осталось: из ответа, а без него — из остатка; при перерасходе меньше нуля.</summary>
	private static double? Left(JsonObject quota) =>
		(double?)quota["percent_remaining"] ?? (double?)quota["remaining"] / (double?)quota["entitlement"] * 100;
}
