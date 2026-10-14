using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AgentVitals;

/// <summary>Чем закончился вопрос к GitHub.</summary>
internal enum UpdateState
{
	Latest,
	Newer,
	Failed,
}

/// <summary>Что известно о последнем релизе.</summary>
/// <param name="Download">Ссылка на AgentVitals.exe в релизе; null — файла нет, ставить нечего.</param>
/// <param name="Digest">SHA-256 этого файла от GitHub, «sha256:…»; null — GitHub его не дал.</param>
internal sealed record Release(UpdateState State, Version? Version = null, string? Download = null, string? Digest = null);

/// <summary>
/// Новые версии с GitHub: проверка, загрузка и подмена себя на том же месте — туда ведут хуки Claude Code.
/// Свой exe занят, пока процесс жив, поэтому подменяет его короткий сценарий, дождавшись выхода.
/// </summary>
internal static class Updates
{
	public const string Releases = "https://github.com/CrossWander/AgentVitals/releases";

	private const string _api = "https://api.github.com/repos/CrossWander/AgentVitals/releases/latest";
	private const string _asset = "AgentVitals.exe";
	private const string _sha256 = "sha256:";

	private static readonly TimeSpan _period = TimeSpan.FromDays(1);
	private static readonly TimeSpan _askTimeout = TimeSpan.FromSeconds(10);

	public static Version Current { get; } = Three(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));

	// После Current: клиент подписывается версией. Загрузка — до пяти минут; GitHub отвечает 403 на запрос без User-Agent.
	private static readonly HttpClient _http = Client();

	/// <summary>Пора ли автопроверке: включена и с последней удачной прошли сутки.</summary>
	public static bool Due(bool enabled, DateTime? checkedAt, DateTime now) =>
		enabled && (checkedAt is not { } last || now - last >= _period);

	/// <summary>Запущена версия новее той, что работала в прошлый раз, — значит, обновились.</summary>
	public static bool Upgraded(string? last, Version current) =>
		Version.TryParse(last, out var before) && Three(before) < Three(current);

	/// <summary>Спросить GitHub о последнем релизе. Удачная проверка запоминается — от неё отсчёт суток.</summary>
	public static async Task<Release> CheckAsync()
	{
		try
		{
			using var timeout = new CancellationTokenSource(_askTimeout);
			using var response = await _http.GetAsync(_api, timeout.Token);

			// Релизов нет вовсе — 404: ставить нечего, текущая и есть последняя.
			Release release;

			if (response.StatusCode == HttpStatusCode.NotFound)
			{
				release = new Release(UpdateState.Latest);
			}
			else if (!response.IsSuccessStatusCode)
			{
				release = new Release(UpdateState.Failed);
			}
			else
			{
				using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
				release = Read(json.RootElement, Current);
			}

			if (release.State == UpdateState.Failed)
			{
				Journal.Add($"update check: HTTP {(int)response.StatusCode}, no readable release");
			}
			else
			{
				Store.Current.UpdateChecked = DateTime.UtcNow;
				Store.Save();
			}

			return release;
		}
		catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
		{
			Journal.Add($"update check: {exception.Message}");

			return new Release(UpdateState.Failed);
		}
	}

	/// <summary>Разобрать ответ GitHub о релизе: новее ли он текущей версии и что скачивать.</summary>
	public static Release Read(JsonElement release, Version current)
	{
		if (!Version.TryParse(Text(release, "tag_name")?.TrimStart('v', 'V'), out var parsed))
		{
			return new Release(UpdateState.Failed);
		}

		var latest = Three(parsed);

		if (latest <= Three(current))
		{
			return new Release(UpdateState.Latest);
		}

		var asset = release.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array
			? assets.EnumerateArray().FirstOrDefault(item => string.Equals(Text(item, "name"), _asset, StringComparison.OrdinalIgnoreCase))
			: default;

		return new Release(UpdateState.Newer, latest, Text(asset, "browser_download_url"), Text(asset, "digest"));
	}

	/// <summary>Совпал ли файл с SHA-256 от GitHub. Хеша нет — не совпал: непроверенное не ставится.</summary>
	public static bool Matches(Stream file, string? digest) =>
		digest is { } text
		&& text.StartsWith(_sha256, StringComparison.OrdinalIgnoreCase)
		&& string.Equals(Convert.ToHexString(SHA256.HashData(file)), text[_sha256.Length..], StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Скачать, сверить, оставить сценарий подмены и закрыться. Возвращает текст ошибки; при удаче программа
	/// уже закрывается, и вернуть нечего.
	/// </summary>
	public static async Task<string?> InstallAsync(Release release, Action shutdown)
	{
		if (release.Download is not { } url || Environment.ProcessPath is not { } self)
		{
			return Localization.Get("langUpdateNoAsset");
		}

		var fresh = self + ".new";

		try
		{
			// Сначала файл: нет прав на папку — понятно сразу, не дожидаясь загрузки.
			await using (var file = File.Create(fresh))
			{
				await using var download = await _http.GetStreamAsync(url);
				await download.CopyToAsync(file);
			}

			bool matches;

			await using (var file = File.OpenRead(fresh))
			{
				matches = Matches(file, release.Digest);
			}

			if (!matches)
			{
				Forget(fresh);
				Journal.Add($"update {release.Version}: SHA-256 differs from GitHub's ({release.Digest ?? "none"})");

				return Localization.Get("langUpdateBadDownload");
			}

			var script = Path.Combine(Path.GetTempPath(), $"agentvitals-update-{Environment.ProcessId}.cmd");
			File.WriteAllText(script, Script(self, fresh), new UTF8Encoding(false));

			Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
			{
				CreateNoWindow = true,
				UseShellExecute = false,
				WorkingDirectory = Path.GetTempPath(),
			});

			Journal.Add($"update: installing {release.Version}");
			shutdown();

			return null;
		}
		catch (UnauthorizedAccessException)
		{
			Forget(fresh);

			return Localization.Get("langUpdateDenied");
		}
		catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException or Win32Exception)
		{
			Forget(fresh);
			Journal.Add($"update {release.Version}: {exception.Message}");

			return Localization.Format("langUpdateFailedWith", exception.Message);
		}
	}

	/// <summary>
	/// Сценарий: ждёт, пока exe освободится (выход программы, хук на полсекунды), подменяет его и запускает —
	/// в любом исходе: не вышло за 40 секунд — поднимается старая версия, а не пропадает из трея.
	/// </summary>
	/// <remarks>CRLF обязателен: с одними LF cmd путает метки. UTF-8 и chcp — для кириллицы в пути.</remarks>
	// ponytail: «%» в пути к программе cmd раскроет как переменную; такие папки — редкость, понадобится — экранировать.
	public static string Script(string self, string fresh) => $"""
		@echo off
		chcp 65001 >nul
		set "target={self}"
		set "fresh={fresh}"
		for /l %%i in (1,1,40) do (
		  move /y "%fresh%" "%target%" >nul 2>&1 && goto start
		  ping -n 2 127.0.0.1 >nul
		)
		del "%fresh%" >nul 2>&1
		:start
		start "" "%target%"
		del "%~f0" >nul 2>&1

		""".ReplaceLineEndings("\r\n");

	private static Version Three(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0));

	private static string? Text(JsonElement item, string property) =>
		item.ValueKind == JsonValueKind.Object && item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	private static HttpClient Client()
	{
		var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
		client.DefaultRequestHeaders.UserAgent.ParseAdd($"AgentVitals/{Current}");

		return client;
	}

	private static void Forget(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			// Файла нет или он занят — убирать больше нечего.
		}
	}
}
