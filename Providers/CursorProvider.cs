using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace AgentVitals;

/// <summary>
/// Месячный расход Cursor по его собственному входу из state.vscdb. Не проверено на живом аккаунте: адрес, cookie
/// и поля — из ClaudeBar (cursor-usage.js) и CodexBar. База только читается; токен никуда, кроме cursor.com, не уходит.
/// </summary>
internal sealed class CursorProvider : IProvider
{
	private const string _url = "https://cursor.com/api/usage-summary";
	private const string _cli = "cursor";

	public string Name => "Cursor";

	public string Cli => _cli;

	// При новом входе Cursor переписывает базу: FileNotice по ней подходит.
	public string Credentials { get; } = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cursor", "User", "globalStorage", "state.vscdb");

	public string? Dashboard => "https://cursor.com/dashboard";

	public IUsageLog? Usage => null;

	public IStatusPage? Status { get; } = new StatusPage("Cursor", "https://status.cursor.com");

	// winsqlite3 есть только у Windows.
	public bool Installed => OperatingSystem.IsWindows() && File.Exists(Credentials);

	/// <summary>Новый вход может лечь только в -wal: его время записи тоже в счёт.</summary>
	public DateTime SignedIn
	{
		get
		{
			var file = File.GetLastWriteTimeUtc(Credentials);
			var wal = File.GetLastWriteTimeUtc(Credentials + "-wal");

			return file > wal ? file : wal;
		}
	}

	/// <inheritdoc/>
	public async Task<Snapshot> FetchAsync(HttpClient http, CancellationToken cancellation) =>
		Sqlite.Item(Credentials, "cursorAuth/accessToken") is { Length: > 0 } token
			? await AskAsync(http, token, Sqlite.Item(Credentials, "cursorAuth/cachedEmail"), cancellation)
			: throw new SignInException(Cli, SignInProblem.Missing);

	/// <summary>Кто вошёл: последняя часть sub из JWT после «|». Чужие символы в cookie не пускаем — тогда null.</summary>
	internal static string? UserId(string token) =>
		Api.Claim(token, "sub")?.Split('|')[^1] is { Length: > 0 } id && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')
			? id
			: null;

	/// <summary>Один GET с cookie сессии на самом запросе.</summary>
	/// <exception cref="JsonException">В токене нет того, кто вошёл.</exception>
	internal static async Task<Snapshot> AskAsync(HttpClient http, string token, string? email, CancellationToken cancellation)
	{
		// Без токена в тексте: сообщение уходит в журнал.
		var user = UserId(token) ?? throw new JsonException("no user id in the Cursor token");

		using var request = new HttpRequestMessage(HttpMethod.Get, _url);
		request.Headers.TryAddWithoutValidation("Cookie", $"WorkosCursorSessionToken={user}%3A%3A{token}");

		return Parse(await Api.SendAsync(http, request, _cli, cancellation, Refusal), email);
	}

	/// <summary>Окна плана: «Месяц» и его части Auto и API; сброс у всех — конец платёжного цикла.</summary>
	/// <exception cref="JsonException">Ни окна, а план не безлимитный.</exception>
	internal static Snapshot Parse(JsonNode usage, string? email)
	{
		var plan = usage["individualUsage"]?["plan"];
		var limit = Math.Max((double?)plan?["limit"] ?? 0, (double?)plan?["breakdown"]?["total"] ?? 0);
		var resets = Api.Date(usage["billingCycleEnd"]);
		List<QuotaWindow> windows = [];

		if (plan is not null && (bool?)plan["enabled"] == true && limit > 0)
		{
			// Уже проценты: 0.815 — это 0,8%.
			windows.Add(new(WindowKind.Month, null, (double?)plan["totalPercentUsed"] ?? ((double?)plan["used"] ?? 0) / limit * 100, resets, Limiting: true));

			// Auto и API — части общего лимита: тосты о них лишние.
			foreach (var (label, field) in new[] { ("Auto", "autoPercentUsed"), ("API", "apiPercentUsed") })
			{
				if ((double?)plan[field] is { } used)
				{
					windows.Add(new(WindowKind.MonthFeature, label, used, resets, Limiting: false));
				}
			}
		}

		if (windows.Count == 0 && (bool?)usage["isUnlimited"] != true)
		{
			throw new JsonException("no plan usage in the Cursor answer");
		}

		return new(Quota.PlanName((string?)usage["membershipType"]), email, windows, null);
	}

	// 401 и 403 — cookie не приняли: войти в Cursor заново.
	private static Exception? Refusal(HttpResponseMessage response) =>
		response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ? new SignInException(_cli, SignInProblem.Rejected) : null;
}
