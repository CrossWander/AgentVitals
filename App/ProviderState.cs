using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AgentVitals;

/// <summary>Что известно о провайдере: последний удачный снимок, последняя ошибка, когда спрашивать снова.</summary>
internal sealed class ProviderState(IProvider provider)
{
	public IProvider Provider { get; } = provider;

	/// <summary>Агент установлен — его спрашивают. Проверяется раз в тик опроса, а не каждым читателем.</summary>
	public bool Installed { get; private set; } = provider.Installed;

	/// <summary>У агента есть вкладка: он установлен, а кому нужно подтверждение — уже ответил хотя бы раз
	/// (в этом запуске или в quota.json). Подтверждённый не пропадает и после отказа: ошибка видна на вкладке.</summary>
	public bool Visible => Installed && (!Provider.ConfirmFirst || Snapshot is not null);

	public Snapshot? Snapshot { get; set; }

	public Exception? Error { get; private set; }

	public DateTime? Updated { get; set; }

	/// <summary>Расход по дням из логов агента; null — логов у агента нет или первое чтение ещё идёт.</summary>
	public IReadOnlyList<UsageDay>? Days { get; private set; }

	/// <summary>Статус сервиса агента; null — статус-страницы нет, её ещё не спрашивали или она не ответила.</summary>
	public ServiceStatus? Status { get; set; }

	/// <summary>Начало тика, в котором провайдера спрашивали последний раз.</summary>
	public DateTime Started { get; private set; } = DateTime.MinValue;

	// Когда переписали вход, на момент отказа: пока он тот же, спрашивать снова бесполезно.
	private DateTime? _rejected;

	// Отказов 429 подряд: по ним растёт пауза, если сервер просит подождать меньше.
	private int _refusals;

	private string? _usageError;

	private DateTime _nextStatus = DateTime.MinValue;

	/// <summary>Спросить агента и запомнить исход. Ошибка не бросается: её показывают, и по ней решают, когда спросить снова.</summary>
	/// <param name="started">Начало тика: следующий запрос считается от него, а не от ответа, — иначе минутный тик
	/// приходит на долю секунды раньше срока и интервал молча вырастает на целую минуту.</param>
	public async Task FetchAsync(HttpClient http, DateTime started)
	{
		var credentials = Stamp();

		try
		{
			Snapshot = await Provider.FetchAsync(http, CancellationToken.None);
			Record(null, credentials, started);
			Updated = DateTime.UtcNow;
		}
		catch (Exception exception)
		{
			// В журнал — при смене вида ошибки, а не каждую минуту одно и то же.
			if (Error?.GetType() != exception.GetType())
			{
				Journal.Add($"{Provider.Name}: {exception.GetType().Name}: {exception.Message}");
			}

			Record(exception, credentials, started);
		}

		Started = started;
	}

	/// <param name="error">Ошибка запроса или null — удача.</param>
	/// <param name="credentials">Время записи файла входа до запроса: если CLI переписал его, пока шёл
	/// запрос, отказ относился к старому входу и новый надо попробовать.</param>
	/// <param name="started">Начало тика: от него растущая пауза после отказов подряд.</param>
	private void Record(Exception? error, DateTime credentials, DateTime started)
	{
		// Сервер нередко отвечает «через ноль» или «через пять минут» и отказывает снова: тогда ждём дольше,
		// но не меньше, чем он назвал.
		_refusals = error is RateLimitedException ? _refusals + 1 : 0;
		Error = error is RateLimitedException limited && started + Poller.Backoff(_refusals) is var longer && longer > limited.Until
			? new RateLimitedException(longer)
			: error;
		// «Нет подписки» — не про вход: его переспрашивают раз в сутки, а не ждут, пока вход перепишут.
		_rejected = error is SignInException { Problem: not SignInProblem.NoSubscription } ? credentials : null;
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

	/// <summary>Спросить статус-страницу агента, если пора или просят «Обновить»; не ответила — «неизвестно».</summary>
	public async Task FetchStatusAsync(HttpClient http, DateTime started, bool force)
	{
		if (Provider.Status is not { } page || (!force && started < _nextStatus))
		{
			return;
		}

		try
		{
			Status = await page.FetchAsync(http, CancellationToken.None);
		}
		catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
		{
			Status = null;
		}

		_nextStatus = started + Poller.StatusEvery;
	}

	/// <summary>Агента установили или удалили, пока программа работает.</summary>
	public void CheckInstalled() => Installed = Provider.Installed;

	private DateTime Stamp() => Provider.SignedIn;

	/// <summary>Считается при каждом тике, а не запоминается: новый интервал из настроек действует сразу.</summary>
	public DateTime NextFetch(int intervalSeconds) =>
		_rejected is { } rejected && rejected == Stamp() ? DateTime.MaxValue : Poller.Schedule(Error, intervalSeconds, Started);

	/// <summary>Что помнить между запусками: последний ответ и до какого времени сервер велел ждать.</summary>
	public RememberedQuota Remember() => new(Snapshot, Updated, Error is RateLimitedException limited ? limited.Until : null);

	/// <summary>Вспомнить при запуске: окно сразу с последними цифрами, а сервер — не раньше, чем пора.</summary>
	public void Recall(RememberedQuota memory, DateTime now)
	{
		Snapshot = memory.Snapshot;
		Updated = memory.Updated;

		// Интервал считается от прошлого ответа, а не от запуска: перезапуск не спрашивает раньше срока.
		Started = memory.Updated is { } updated && updated <= now ? updated : DateTime.MinValue;
		Error = memory.RetryAt > now ? new RateLimitedException(memory.RetryAt.Value) : null;
	}

	/// <summary>Окна последнего снимка на этот момент: у прошедших сброс — 0%. Одни и те же для карточек, значка и тостов.</summary>
	public IReadOnlyList<QuotaWindow> Windows(DateTime now) => [.. Snapshot?.Windows.Select(window => window.At(now)) ?? []];

	/// <summary>Сколько израсходовано в самом узком окне; null — не известно. Истёкший вход — тоже null: старые
	/// проценты уже ничего не говорят о том, можно ли работать.</summary>
	public double? Tightest(DateTime now) => Error is SignInException ? null : Quota.Bottleneck(Windows(now))?.Used;
}
