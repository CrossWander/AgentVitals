using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AgentVitals;

/// <summary>
/// Страница цен в сети и её копия на диске. Копия читается при первом обращении; страница скачивается через сутки
/// от копии, а не разобралась или нет сети — работаем по копии и спросим через час.
/// </summary>
internal sealed class PriceSource(string url, string file, Func<string, PriceList> parse)
{
	private DateTime _next = DateTime.MinValue;
	private bool _started;
	private string? _lastError;

	public PriceList? Prices { get; private set; }

	/// <summary>Цены на сейчас; null — ни копии, ни ответа сайта.</summary>
	public async Task<PriceList?> GetAsync(HttpClient http, DateTime now, CancellationToken cancellation)
	{
		if (!_started)
		{
			_started = true;
			LoadSaved();
		}

		if (now < _next)
		{
			return Prices;
		}

		try
		{
			var accepted = Accept(await http.GetStringAsync(url, cancellation));

			if (!accepted)
			{
				Report("page", "no price table on the page, keeping the saved copy");
			}

			_next = now + (accepted ? TimeSpan.FromDays(1) : TimeSpan.FromHours(1));
		}
		catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
		{
			// Нет сети или сайта — работаем по копии, спросим через час.
			_next = now + TimeSpan.FromHours(1);
		}

		return Prices;
	}

	/// <summary>Копия страницы с прошлого раза; скачивать заново — через сутки после неё.</summary>
	internal void LoadSaved()
	{
		try
		{
			if (File.Exists(file) && parse(File.ReadAllText(file)) is { Count: > 0 } prices)
			{
				Prices = prices;
				_next = File.GetLastWriteTimeUtc(file) + TimeSpan.FromDays(1);
			}
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			Report(exception.GetType().Name, exception.Message);
		}
	}

	/// <summary>Скачанная страница: разобралась — в дело и в копию, нет — остаётся прежняя.</summary>
	internal bool Accept(string page)
	{
		var prices = parse(page);

		if (prices.Count == 0)
		{
			return false;
		}

		Prices = prices;

		try
		{
			// Через временный файл, как config.json: оборванная запись не испортит прежнюю копию.
			var temp = file + ".tmp";
			File.WriteAllText(temp, page);
			File.Move(temp, file, overwrite: true);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			Report(exception.GetType().Name, exception.Message);
		}

		return true;
	}

	/// <summary>В журнал — при смене вида беды, а не каждую минуту одно и то же.</summary>
	private void Report(string kind, string message)
	{
		if (_lastError != kind)
		{
			Journal.Add($"prices: {Path.GetFileName(file)}: {message}");
			_lastError = kind;
		}
	}
}
