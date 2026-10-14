using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentVitals;

/// <summary>Настройки и что о них помнить между запусками — в config.json в папке данных (Paths.Data).
/// Последний ответ о квотах — отдельно, в quota.json (QuotaMemory).</summary>
internal static class Store
{
	internal sealed class Data
	{
		public string Language { get; set; } = Localization.SystemDefault();

		public ThemeMode Theme { get; set; } = ThemeMode.Auto;

		// Сервер квот отвечает 429 уже на опрос раз в 1–2 минуты; все живые трекеры спрашивают раз в 5–15.
		// Последний ответ помнится между запусками, так что окно не пустует и при редком опросе.
		public int IntervalSeconds { get; set; } = 300;

		// Тосты о сессиях: ход закончился (не короче TurnMinutes, 0 — любой) или ждёт разрешения.
		public bool SessionToasts { get; set; } = true;

		public int TurnMinutes { get; set; } = 5;

		// Пороги квоты, %: с них тосты «важно» и «критично», жёлтый и красный.
		public int ImportantAt { get; set; } = 75;

		public int CriticalAt { get; set; } = 90;

		// Обновления: спрашивать GitHub при запуске и раз в сутки; о какой версии уже был тост;
		// какая версия работала в прошлый раз — по ней тост «обновлён до».
		public bool AutoUpdate { get; set; } = true;

		public DateTime? UpdateChecked { get; set; }

		public string? UpdateAnnounced { get; set; }

		public string? LastVersion { get; set; }

		// В файле — два числа, как их правит человек; правилам — одной парой.
		[JsonIgnore]
		public Thresholds Thresholds
		{
			get => new(ImportantAt, CriticalAt);
			set => (ImportantAt, CriticalAt) = (value.Important, value.Critical);
		}
	}

	private static readonly string _path = Path.Combine(Paths.Data, "config.json");

	// Файл открывает и правит человек: с отступами, кириллицей и терпимо к комментариям.
	private static readonly JsonSerializerOptions _format = new()
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
		Converters =
		{
			new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
		},
	};

	public static Data Current { get; } = Read();

	/// <summary>Записать файл через временный: оборванная запись не испортит прежние настройки.</summary>
	public static void Save()
	{
		var temp = _path + ".tmp";

		try
		{
			File.WriteAllText(temp, JsonSerializer.Serialize(Current, _format), new UTF8Encoding(true));
			File.Move(temp, _path, overwrite: true);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			// Настройка действует до перезапуска; почему не сохранилась — в журнале.
			Journal.Add($"config.json: {exception.Message}");
		}
	}

	private static Data Read()
	{
		try
		{
			if (File.Exists(_path))
			{
				return JsonSerializer.Deserialize<Data>(File.ReadAllText(_path), _format) ?? new Data();
			}
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
		{
			// Испорченный файл молча не прощаем: без записи это выглядело бы как забытые настройки.
			Journal.Add($"config.json: {exception.Message}");
		}

		return new Data();
	}
}
