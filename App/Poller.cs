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

	/// <summary>Агент установлен: его файл входа на месте. Проверяется раз в тик опроса, а не каждым читателем.</summary>
	public bool Installed { get; private set; } = File.Exists(provider.Credentials);

	public Snapshot? Snapshot { get; set; }

	public Exception? Error { get; private set; }

	public DateTime? Updated { get; set; }

	/// <summary>Расход по дням из логов агента; null — логов у агента нет или первое чтение ещё идёт.</summary>
	public IReadOnlyList<UsageDay>? Days { get; private set; }

	/// <summary>Начало тика, в котором провайдера спрашивали последний раз.</summary>
	public DateTime Started { get; set; } = DateTime.MinValue;

	// Время записи файла входа, когда вход отклонили: пока файл тот же, спрашивать снова бесполезно.
	private DateTime? _rejected;

	private string? _usageError;

	/// <summary>Запомнить исход запроса; null — удача.</summary>
	/// <param name="error">Ошибка запроса или null.</param>
	/// <param name="credentials">Время записи файла входа до запроса: если CLI переписал его, пока шёл
	/// запрос, отказ относился к старому входу и новый надо попробовать.</param>
	public void Record(Exception? error, DateTime credentials)
	{
		Error = error;
		_rejected = error is TokenExpiredException ? credentials : null;
	}

	/// <summary>Дочитать логи агента. Статистика вторична: её сбой не должен ронять тик опроса — он async void.</summary>
	public async Task ReadUsageAsync(HttpClient http, DateTime now)
	{
		if (Provider.Usage is not { } log)
		{
			return;
		}

		try
		{
			Days = await log.ReadAsync(http, now, CancellationToken.None);
		}
		catch (Exception exception)
		{
			if (_usageError != exception.GetType().Name)
			{
				Journal.Add($"{Provider.Name} usage: {exception.GetType().Name}: {exception.Message}");
			}

			_usageError = exception.GetType().Name;
		}
	}

	/// <summary>Агента установили или удалили, пока программа работает.</summary>
	public void CheckInstalled() => Installed = File.Exists(Provider.Credentials);

	/// <summary>Время записи файла входа — снимать до запроса и передавать в <see cref="Record"/>.</summary>
	public DateTime Stamp() => File.GetLastWriteTimeUtc(Provider.Credentials);

	/// <summary>Считается при каждом тике, а не запоминается: новый интервал из настроек действует сразу.</summary>
	public DateTime NextFetch(int intervalSeconds) =>
		_rejected is { } rejected && rejected == Stamp() ? DateTime.MaxValue : Poller.Schedule(Error, intervalSeconds, Started);

	/// <summary>Окна последнего снимка на этот момент: у прошедших сброс — 0%. Одни и те же для карточек, значка и тостов.</summary>
	public IReadOnlyList<QuotaWindow> Windows(DateTime now) => [.. Snapshot?.Windows.Select(window => window.At(now)) ?? []];

	/// <summary>Сколько израсходовано в самом узком окне; null — не известно. Истёкший вход — тоже null: старые
	/// проценты уже ничего не говорят о том, можно ли работать.</summary>
	public double? Tightest(DateTime now) => Error is TokenExpiredException ? null : Quota.Bottleneck(Windows(now))?.Used;
}

/// <summary>Расписание запросов к провайдерам и статус-странице.</summary>
internal sealed class Poller
{
	private static readonly TimeSpan _statusEvery = TimeSpan.FromMinutes(5);

	private static readonly TimeSpan[] _afterWake = [TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(45)];

	private readonly HttpClient _http = new()
	{
		Timeout = TimeSpan.FromSeconds(15),
	};

	// Тик — самый короткий интервал из настроек: каждый из них кратен ему и приходится ровно на тик.
	private readonly DispatcherTimer _timer = new()
	{
		Interval = TimeSpan.FromMinutes(1),
	};

	private readonly List<FileNotice> _notices = [];

	private DateTime _nextStatus = DateTime.MinValue;
	private bool _busy;

	// Тик попросили, пока шёл другой: null — никто, true — «Обновить», который не ждёт срока.
	private bool? _again;

	public IReadOnlyList<ProviderState> Providers { get; } =
		[new(new ClaudeProvider()), new(new CodexProvider()), new(new GeminiProvider())];

	/// <summary>Установленные агенты: только их показывают и спрашивают.</summary>
	public IEnumerable<ProviderState> Installed => Providers.Where(state => state.Installed);

	/// <summary>Самое узкое окно среди агентов; null — ни у кого нет свежих данных.</summary>
	public static double? Tightest(IEnumerable<ProviderState> agents, DateTime now) => agents.Max(state => state.Tightest(now));

	/// <summary>Общее состояние квот — по самому узкому окну.</summary>
	public static Health Health(IEnumerable<ProviderState> agents, DateTime now, Thresholds levels) =>
		Tightest(agents, now) is { } used ? Quota.Rate(used, levels) : AgentVitals.Health.Unknown;

	public ServiceStatus? Status { get; private set; }

	/// <summary>Прошёл тик: что-то могло обновиться, а относительное время в подписях — сдвинуться.</summary>
	public event Action? Changed;

	public void Start()
	{
		_timer.Tick += async (_, _) => await TickAsync(force: false);
		_timer.Start();
		_ = TickAsync(force: false);

		// После сна вход истёк, пока CLI агента его не обновит: новый файл входа — сразу тик, а не через минуту.
		foreach (var state in Providers)
		{
			_notices.Add(new FileNotice(state.Provider.Credentials, () => Dispatcher.UIThread.Post(() => _ = TickAsync(force: false))));
		}
	}

	/// <summary>Спросить всех сейчас, кроме тех, кому сервер велел ждать.</summary>
	public Task RefreshAsync() => TickAsync(force: true);

	/// <summary>Компьютер проснулся: спросить сразу. Сеть после сна поднимается не сразу, а обычный повтор после
	/// сетевой ошибки — через пять минут; поэтому ещё пара попыток, пока ответ не придёт.</summary>
	public async Task WokeAsync()
	{
		foreach (var delay in _afterWake)
		{
			await Task.Delay(delay);
			await TickAsync(force: true);

			if (!Offline(Installed, Status))
			{
				return;
			}
		}
	}

	/// <summary>Кто-то не дозвался до сервера: ни ответа, ни отказа.</summary>
	internal static bool Offline(IEnumerable<ProviderState> states, ServiceStatus? status) =>
		status is null || states.Any(state => state.Error is HttpRequestException or TaskCanceledException);

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
		// Первое чтение логов идёт секунды, запрос при плохой сети — до 15 с: «Обновить» и новый файл входа
		// в это время не теряются, а выполняются сразу после — один раз, сколько бы их ни пришло.
		if (_busy)
		{
			_again = _again == true || force;

			return;
		}

		_busy = true;

		try
		{
			var now = DateTime.UtcNow;

			foreach (var state in Providers)
			{
				state.CheckInstalled();
			}

			var due = Installed
				.Where(state => now >= state.NextFetch(Store.Current.IntervalSeconds) || (force && state.Error is not RateLimitedException))
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

			// Статистика — после квот и статуса: первое чтение логов занимает секунды.
			foreach (var state in Installed)
			{
				await state.ReadUsageAsync(_http, now);
			}
		}
		finally
		{
			_busy = false;
			Changed?.Invoke();
		}

		if (_again is { } again)
		{
			_again = null;
			await TickAsync(again);
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
