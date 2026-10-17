using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace AgentVitals;

/// <summary>Расписание запросов к провайдерам и их статус-страницам.</summary>
internal sealed class Poller
{
	internal static readonly TimeSpan StatusEvery = TimeSpan.FromMinutes(5);

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

	private bool _busy;

	// Тик попросили, пока шёл другой: null — никто, true — «Обновить», который не ждёт срока.
	private bool? _again;

	public IReadOnlyList<ProviderState> Providers { get; } =
		[new(new ClaudeProvider()), new(new CodexProvider()), new(new GeminiProvider()), new(new CopilotProvider()), new(new CursorProvider())];

	/// <summary>Установленные агенты: только их спрашивают.</summary>
	public IEnumerable<ProviderState> Installed => Providers.Where(state => state.Installed);

	/// <summary>Агенты с вкладкой: их показывают, о них тосты, значок и статус-страницы.</summary>
	public IEnumerable<ProviderState> Visible => Installed.Where(state => state.Visible);

	/// <summary>Самое узкое окно среди агентов; null — ни у кого нет свежих данных.</summary>
	public static double? Tightest(IEnumerable<ProviderState> agents, DateTime now) => agents.Max(state => state.Tightest(now));

	/// <summary>Общее состояние квот — по самому узкому окну.</summary>
	public static Health Health(IEnumerable<ProviderState> agents, DateTime now, Thresholds levels) =>
		Tightest(agents, now) is { } used ? Quota.Rate(used, levels) : AgentVitals.Health.Unknown;

	/// <summary>Прошёл тик: что-то могло обновиться, а относительное время в подписях — сдвинуться.</summary>
	public event Action? Changed;

	public void Start()
	{
		var memory = QuotaMemory.Load(QuotaMemory.Location);

		foreach (var state in Providers)
		{
			if (memory.TryGetValue(state.Provider.Name, out var remembered))
			{
				state.Recall(remembered, DateTime.UtcNow);
			}
		}

		_timer.Tick += async (_, _) => await TickAsync(force: false);
		_timer.Start();
		_ = TickAsync(force: false);

		// После сна вход истёк, пока CLI агента его не обновит: новый файл входа — сразу тик, а не через минуту.
		foreach (var file in Providers.Select(state => state.Provider.Credentials).OfType<string>())
		{
			_notices.Add(new FileNotice(file, () => Dispatcher.UIThread.Post(() => _ = TickAsync(force: false))));
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

			if (!Offline(Visible))
			{
				return;
			}
		}
	}

	/// <summary>Кто-то не дозвался до сервера: ни ответа, ни отказа, — или статус-страница агента молчит.</summary>
	internal static bool Offline(IEnumerable<ProviderState> states) =>
		states.Any(state => state.Error is HttpRequestException or TaskCanceledException || (state.Provider.Status is not null && state.Status is null));

	/// <summary>Пауза после стольких отказов 429 подряд: 5, 10, 20 минут, дальше — 30.</summary>
	internal static TimeSpan Backoff(int refusals) => TimeSpan.FromMinutes(Math.Min(30, 5 << Math.Clamp(refusals - 1, 0, 3)));

	/// <summary>Когда спрашивать снова после этого исхода.</summary>
	internal static DateTime Schedule(Exception? error, int intervalSeconds, DateTime now) => error switch
	{
		// Чаще раза в минуту сервер отвечает 429, реже суток — уже не наблюдение; файл могли поправить руками.
		null => now.AddSeconds(Math.Clamp(intervalSeconds, 60, 86_400)),

		// Срок токена проверяется локально: тик ничего не стоит, а как только Claude Code
		// обновит вход, данные вернутся на следующем тике.
		// Подписку могли оформить, пока программа работает, а вход git от этого не меняется.
		SignInException { Problem: SignInProblem.NoSubscription } => now.AddDays(1),
		SignInException => now,
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

			foreach (var state in due)
			{
				await state.FetchAsync(_http, now);
			}

			if (due.Count > 0)
			{
				QuotaMemory.Save(QuotaMemory.Location, Installed.ToDictionary(state => state.Provider.Name, state => state.Remember()));
			}

			// Статус — только у агентов с вкладкой: Copilot без подтверждения его не показывает.
			foreach (var state in Visible)
			{
				await state.FetchStatusAsync(_http, now, force);
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
}
