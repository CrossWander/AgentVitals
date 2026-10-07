using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace AgentVitals;

/// <summary>Что известно о провайдере: последний удачный снимок, последняя ошибка, когда спрашивать снова.</summary>
internal sealed class ProviderState(IProvider provider)
{
	public IProvider Provider { get; } = provider;

	public Snapshot? Snapshot { get; set; }

	public Exception? Error { get; private set; }

	public DateTime? Updated { get; set; }

	/// <summary>Начало тика, в котором провайдера спрашивали последний раз.</summary>
	public DateTime Started { get; set; } = DateTime.MinValue;

	// Время записи файла входа, когда вход отклонили: пока файл тот же, спрашивать снова бесполезно.
	private DateTime? _rejected;

	/// <summary>Запомнить исход запроса; null — удача.</summary>
	/// <param name="error">Ошибка запроса или null.</param>
	/// <param name="credentials">Время записи файла входа до запроса: если CLI переписал его, пока шёл
	/// запрос, отказ относился к старому входу и новый надо попробовать.</param>
	public void Record(Exception? error, DateTime credentials)
	{
		Error = error;
		_rejected = error is TokenExpiredException ? credentials : null;
	}

	/// <summary>Время записи файла входа — снимать до запроса и передавать в <see cref="Record"/>.</summary>
	public DateTime Stamp() => File.GetLastWriteTimeUtc(Provider.Credentials);

	/// <summary>Считается при каждом тике, а не запоминается: новый интервал из настроек действует сразу.</summary>
	public DateTime NextFetch(int intervalSeconds) =>
		_rejected is { } rejected && rejected == Stamp() ? DateTime.MaxValue : Poller.Schedule(Error, intervalSeconds, Started);

	/// <summary>Истёкший вход — серое: старые проценты уже ничего не говорят о том, можно ли работать.</summary>
	public Health Health => Snapshot is null || Error is TokenExpiredException ? Health.Unknown : Quota.Rate(Snapshot.Windows);
}

/// <summary>Расписание запросов к провайдерам и статус-странице.</summary>
internal sealed class Poller
{
	private static readonly TimeSpan _statusEvery = TimeSpan.FromMinutes(5);

	private readonly HttpClient _http = new()
	{
		Timeout = TimeSpan.FromSeconds(15),
	};

	// Тик — самый короткий интервал из настроек: каждый из них кратен ему и приходится ровно на тик.
	private readonly DispatcherTimer _timer = new()
	{
		Interval = TimeSpan.FromMinutes(1),
	};

	private DateTime _nextStatus = DateTime.MinValue;
	private bool _busy;

	public IReadOnlyList<ProviderState> Providers { get; } =
		[new(new ClaudeProvider()), new(new CodexProvider()), new(new GeminiProvider())];

	public ServiceStatus? Status { get; private set; }

	/// <summary>Прошёл тик: что-то могло обновиться, а относительное время в подписях — сдвинуться.</summary>
	public event Action? Changed;

	public void Start()
	{
		_timer.Tick += async (_, _) => await TickAsync(force: false);
		_timer.Start();
		_ = TickAsync(force: false);
	}

	/// <summary>Спросить всех сейчас, кроме тех, кому сервер велел ждать.</summary>
	public Task RefreshAsync() => TickAsync(force: true);

	/// <summary>Когда спрашивать снова после этого исхода.</summary>
	internal static DateTime Schedule(Exception? error, int intervalSeconds, DateTime now) => error switch
	{
		// Чаще раза в минуту сервер отвечает 429, реже суток — уже не наблюдение; файл могли поправить руками.
		null => now.AddSeconds(Math.Clamp(intervalSeconds, 60, 86_400)),

		// Срок токена проверяется локально: тик ничего не стоит, а как только Claude Code
		// обновит вход, данные вернутся на следующем тике.
		TokenExpiredException => now,
		RateLimitedException limited => limited.Until,
		_ => now.AddMinutes(5),
	};

	private async Task TickAsync(bool force)
	{
		if (_busy)
		{
			return;
		}

		_busy = true;

		try
		{
			var now = DateTime.UtcNow;
			var due = Providers
				.Where(state => state.Provider.Available
					&& (now >= state.NextFetch(Store.Current.IntervalSeconds) || (force && state.Error is not RateLimitedException)))
				.ToList();

			// Следующий запрос считается от начала тика, а не от ответа: иначе минутный тик приходит
			// на долю секунды раньше срока и интервал молча вырастает на целую минуту.
			foreach (var state in due)
			{
				await FetchAsync(state, now);
			}

			if (force || now >= _nextStatus)
			{
				await FetchStatusAsync(now);
			}
		}
		finally
		{
			_busy = false;
			Changed?.Invoke();
		}
	}

	private async Task FetchAsync(ProviderState state, DateTime started)
	{
		var credentials = state.Stamp();

		try
		{
			state.Snapshot = await state.Provider.FetchAsync(_http, CancellationToken.None);
			state.Record(null, credentials);
			state.Updated = DateTime.UtcNow;
		}
		catch (Exception exception)
		{
			// В журнал — при смене вида ошибки, а не каждую минуту одно и то же.
			if (state.Error?.GetType() != exception.GetType())
			{
				Journal.Add($"{state.Provider.Name}: {exception.GetType().Name}: {exception.Message}");
			}

			state.Record(exception, credentials);
		}

		state.Started = started;
	}

	private async Task FetchStatusAsync(DateTime started)
	{
		try
		{
			Status = await StatusPage.FetchAsync(_http, CancellationToken.None);
		}
		catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
		{
			Status = null;
		}

		_nextStatus = started + _statusEvery;
	}
}
