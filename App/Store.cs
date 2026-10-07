using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentVitals;

/// <summary>Всё, что программа помнит между запусками, в одном config.json в папке данных (Paths.Data).</summary>
internal static class Store
{
	internal sealed class Data
	{
		public string Language { get; set; } = Localization.SystemDefault();

		public ThemeMode Theme { get; set; } = ThemeMode.Auto;

		// Квоты сессии меняются за минуты: реже — и цифры в карточке отстают от того, что видно в claude.
		public int IntervalSeconds { get; set; } = 60;
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
