using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentVitals.Core.Quotas;
using AgentVitals.Core.Status;
using AgentVitals.Core.Usage;
using AgentVitals.Platform;

namespace AgentVitals.Core;

/// <summary>Что известно об агенте: последний удачный снимок, последняя ошибка, когда спрашивать снова. Снимок, ошибка
/// и расписание — аккаунта в работе; остальные аккаунты агента помнятся с последними цифрами (Accounts).</summary>
public sealed class AgentState(IAgent agent)
{
	/// <summary>Как часто спрашивать статус-страницу агента.</summary>
	internal static readonly TimeSpan StatusEvery = TimeSpan.FromMinutes(5);

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

	/// <summary>Сколько выждать после записи файла входа: CLI пишет вход и того, кто вошёл, не разом, а цифры не должны
	/// лечь под имя прежнего аккаунта.</summary>
	public static readonly TimeSpan Settle = TimeSpan.FromSeconds(5);

	public IAgent Agent { get; } = agent;

	/// <summary>Агент установлен — его спрашивают. Проверяется раз в тик опроса, а не каждым читателем.</summary>
	public bool Installed { get; private set; } = agent.Installed;

	/// <summary>Пользователь выключил агента в окне «Агенты»: за ним не следят — ни запросов, ни вкладки, ни тостов.</summary>
	public bool Off { get; set; }

	/// <summary>У агента есть вкладка: он установлен, не выключен, а кому нужно подтверждение — уже ответил хотя бы раз
	/// (в этом запуске или в quota.json). Подтверждённый не пропадает и после отказа: ошибка видна на вкладке.</summary>
	public bool Visible => Installed && !Off && (!Agent.ConfirmFirst || Snapshot is not null);

	public Snapshot? Snapshot { get; set; }

	public Exception? Error { get; private set; }

	public DateTime? Updated { get; set; }

	/// <summary>Аккаунт в работе; null — ещё неизвестен: ответа не было или снимок остался от 1.3.0.</summary>
	public string? Key { get; private set; }

	/// <summary>Вошли в другой аккаунт, а его свежего ответа ещё нет: цифры — запомненные или никаких.</summary>
	public bool Pending { get; private set; }

	/// <summary>Все аккаунты агента в порядке появления; у аккаунта в работе — живые цифры. Пока аккаунт неизвестен — пусто.</summary>
	public IReadOnlyList<Account> Accounts => Key is null ? [] : [.. _accounts.Select(account => account.Key == Key ? Current() : account)];

	/// <summary>Расход по дням из логов агента; null — логов у агента нет или первое чтение ещё идёт.</summary>
	public IReadOnlyList<UsageDay>? Days { get; private set; }

	/// <summary>Статус сервиса агента; null — статус-страницы нет, её ещё не спрашивали или она не ответила.</summary>
	public ServiceStatus? Status { get; set; }

	/// <summary>Начало тика, в котором агента спрашивали последний раз.</summary>
	public DateTime Started { get; private set; } = DateTime.MinValue;

	// Когда переписали вход, на момент отказа: пока он тот же, спрашивать снова бесполезно.
	private DateTime? _rejected;

	// Отказов 429 подряд: по ним растёт пауза, если сервер просит подождать меньше.
	private int _refusals;

	private string? _usageError;

	private DateTime _nextStatus = DateTime.MinValue;

	// Аккаунты в порядке появления; у аккаунта в работе здесь — цифры на момент, когда в него вошли.
	private readonly List<Account> _accounts = [];

	// Кто вошёл, по файлам на диске, на прошлом тике; null — агент сообщает это только ответом.
	private string? _local;

	// Время записи входа в начале последнего запроса: у агента без личности на диске новый вход — это, может быть,
	// другой аккаунт, и спросить надо сразу. null — с запуска не спрашивали.
	private DateTime? _asked;

	/// <summary>Спросить агента и запомнить исход. Ошибка не бросается: её показывают, и по ней решают, когда спросить снова.</summary>
	/// <param name="started">Начало тика: следующий запрос считается от него, а не от ответа, — иначе минутный тик
	/// приходит на долю секунды раньше срока и интервал молча вырастает на целую минуту.</param>
	public async Task FetchAsync(HttpClient http, DateTime started)
	{
		var credentials = Stamp();
		var who = Agent.Identity();
		_asked = credentials;

		try
		{
			var answer = await Agent.FetchAsync(http, CancellationToken.None);

			// Вход переписали, пока шёл запрос: чей это ответ — неизвестно, спросить на следующем тике.
			// ponytail: если CLI допишет ~/.claude.json позже файла входа, а запрос по интервалу попадёт ровно между,
			// ответ ляжет под прежнее имя; Poller ждёт Settle после записи входа, щель — доли секунды.
			if (Stamp() != credentials || Agent.Identity() != who)
			{
				Started = DateTime.MinValue;

				return;
			}

			Take(answer, who ?? answer.Account ?? "", DateTime.UtcNow);
			Record(null, credentials, started);
		}
		catch (Exception exception)
		{
			// В журнал — при смене вида ошибки, а не каждую минуту одно и то же.
			if (Error?.GetType() != exception.GetType())
			{
				Journal.Add($"{Agent.Name}: {exception.GetType().Name}: {exception.Message}");
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
		Error = error is RateLimitedException limited && started + Backoff(_refusals) is var longer && longer > limited.Until
			? new RateLimitedException(longer)
			: error;
		// «Нет подписки» — не про вход: его переспрашивают раз в сутки, а не ждут, пока вход перепишут.
		_rejected = error is SignInException { Problem: not SignInProblem.NoSubscription } ? credentials : null;
	}

	/// <summary>Свежий ответ аккаунта key: другой аккаунт — переключиться, первый — назвать.</summary>
	private void Take(Snapshot answer, string key, DateTime now)
	{
		if (Key is null)
		{
			// Снимок из quota.json без опознания: свой — заменится, чужой — отброшен, под другим именем его не покажем.
			if (Snapshot?.Account is { } old && old != answer.Account)
			{
				Journal.Add($"{Agent.Name}: quota.json: last numbers belonged to another account, dropped");
			}

			Key = key;

			if (!_accounts.Exists(account => account.Key == key))
			{
				_accounts.Add(new(key, null, null, null));
			}
		}
		else if (key != Key)
		{
			Switch(key, now);
		}

		Snapshot = answer;
		Updated = now;
		Pending = false;
	}

	/// <summary>Вошли в другой аккаунт: прежний запомнить с его цифрами и паузой после 429, новый — вспомнить и спросить сразу.</summary>
	private void Switch(string key, DateTime now)
	{
		var index = _accounts.FindIndex(account => account.Key == Key);

		if (index >= 0)
		{
			// Аккаунт без единого ответа помнить нечем.
			if (Snapshot is null)
			{
				_accounts.RemoveAt(index);
			}
			else
			{
				_accounts[index] = Current();
			}
		}

		var known = _accounts.Find(account => account.Key == key);

		if (known is null)
		{
			_accounts.Add(known = new(key, null, null, null));
		}

		Key = key;
		Snapshot = known.Snapshot;
		Updated = known.Updated;
		Error = known.RetryAt > now ? new RateLimitedException(known.RetryAt.Value) : null;
		_refusals = 0;
		_rejected = null;
		Started = DateTime.MinValue;
		Pending = true;
	}

	private Account Current() => new(Key!, Snapshot, Updated, Error is RateLimitedException limited ? limited.Until : null);

	/// <summary>Посмотреть, кто вошёл, по файлам агента: после /login новый аккаунт — сразу в работе, прежний — запомнен.</summary>
	public void CheckAccount(DateTime now)
	{
		_local = Agent.Identity();

		if (Key is not null && _local is not null && _local != Key)
		{
			Switch(_local, now);
		}
	}

	/// <summary>Забыть запомненный аккаунт; аккаунт в работе не забывается.</summary>
	public void Forget(string key)
	{
		if (key != Key)
		{
			_accounts.RemoveAll(account => account.Key == key);
		}
	}

	/// <summary>Спрашивать ли сейчас: пора по расписанию, просят «Обновить» или у агента без личности на диске
	/// переписали вход. Пауза после 429 держит и то, и другое.</summary>
	public bool Due(int intervalSeconds, DateTime now, bool force) =>
		now >= NextFetch(intervalSeconds)
		|| (Error is not RateLimitedException && (force || (_local is null && _asked is { } asked && Stamp() != asked)));

	/// <summary>Дочитать логи агента. Статистика вторична: её сбой не должен ронять тик опроса — он async void.</summary>
	public async Task ReadUsageAsync(HttpClient http, DateTime now)
	{
		if (Agent.Usage is not { } log)
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
				Journal.Add($"{Agent.Name} usage: {exception.GetType().Name}: {exception.Message}");
			}

			_usageError = exception.GetType().Name;
		}
	}

	/// <summary>Спросить статус-страницу агента, если пора или просят «Обновить»; не ответила — «неизвестно».</summary>
	public async Task FetchStatusAsync(HttpClient http, DateTime started, bool force)
	{
		if (Agent.Status is not { } page || (!force && started < _nextStatus))
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

		_nextStatus = started + StatusEvery;
	}

	/// <summary>Агента установили или удалили, пока программа работает.</summary>
	public void CheckInstalled() => Installed = Agent.Installed;

	private DateTime Stamp() => Agent.SignedIn;

	/// <summary>Считается при каждом тике, а не запоминается: новый интервал из настроек действует сразу.</summary>
	public DateTime NextFetch(int intervalSeconds) =>
		_rejected is { } rejected && rejected == Stamp() ? DateTime.MaxValue : Schedule(Error, intervalSeconds, Started);

	/// <summary>Что помнить между запусками: последний ответ и до какого времени сервер велел ждать.</summary>
	public RememberedQuota Remember() => new(Snapshot, Updated, Error is RateLimitedException limited ? limited.Until : null, Key, Key is null ? null : Accounts);

	/// <summary>Вспомнить при запуске: окно сразу с последними цифрами, а сервер — не раньше, чем пора.</summary>
	public void Recall(RememberedQuota memory, DateTime now)
	{
		Snapshot = memory.Snapshot;
		Updated = memory.Updated;

		// Интервал считается от прошлого ответа, а не от запуска: перезапуск не спрашивает раньше срока.
		Started = memory.Updated is { } updated && updated <= now ? updated : DateTime.MinValue;
		Error = memory.RetryAt > now ? new RateLimitedException(memory.RetryAt.Value) : null;
		Key = memory.Key;
		Pending = false;
		_asked = null;
		_accounts.Clear();
		_accounts.AddRange(memory.Accounts ?? []);

		if (Key is not null && !_accounts.Exists(account => account.Key == Key))
		{
			_accounts.Add(Current());
		}
	}

	/// <summary>Окна последнего снимка на этот момент: у прошедших сброс — 0%. Одни и те же для карточек, значка и тостов.</summary>
	public IReadOnlyList<QuotaWindow> Windows(DateTime now) => [.. Snapshot?.Windows.Select(window => window.At(now)) ?? []];

	/// <summary>Сколько израсходовано в самом узком окне; null — не известно. Истёкший вход — тоже null: старые
	/// проценты уже ничего не говорят о том, можно ли работать.</summary>
	public double? Tightest(DateTime now) => Error is SignInException ? null : Quota.Bottleneck(Windows(now))?.Used;
}
