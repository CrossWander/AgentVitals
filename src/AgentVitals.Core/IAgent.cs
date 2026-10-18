using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AgentVitals.Core.Quotas;
using AgentVitals.Core.Sessions;
using AgentVitals.Core.Status;
using AgentVitals.Core.Usage;

namespace AgentVitals.Core;

/// <summary>AI-агент: имя, вход, квоты, расход, статус-страница и hooks живых сессий.</summary>
public interface IAgent
{
	/// <summary>Имя агента в интерфейсе.</summary>
	string Name { get; }

	/// <summary>Команда CLI, запуск которой обновляет вход агента.</summary>
	string Cli { get; }

	/// <summary>Файл авторизации агента: его переписывает CLI, когда обновляет вход; null — вход хранится не в файле.</summary>
	string? Credentials { get; }

	/// <summary>Агент установлен — его спрашивают. Обычно это значит, что файл входа на месте.</summary>
	bool Installed => Credentials is { } file && File.Exists(file);

	/// <summary>Когда вход последний раз переписали: пока он тот же, после отказа спрашивать снова бесполезно.</summary>
	DateTime SignedIn => Credentials is { } file ? File.GetLastWriteTimeUtc(file) : DateTime.MinValue;

	/// <summary>Кто вошёл — ключ аккаунта по файлам на диске, без сети; null — агент сообщает это только ответом сервера.
	/// Не бросает: нечитаемый файл — тоже null.</summary>
	string? Identity() => null;

	/// <summary>Показывать агента только после первого удачного ответа: его признаки бывают и у тех, у кого его нет.</summary>
	bool ConfirmFirst => false;

	/// <summary>Страница расхода у самого агента, в браузере; null — такой нет.</summary>
	string? Dashboard { get; }

	/// <summary>Читатель локальных логов агента; null — статистики у агента пока нет.</summary>
	IUsageLog? Usage { get; }

	/// <summary>Страница здоровья сервиса агента; null — у агента её нет.</summary>
	IStatusPage? Status { get; }

	/// <summary>Куда подключаются hooks живых сессий агента; null — сессий агента программа не видит.</summary>
	HookTarget? Hooks { get; }

	/// <summary>Получить текущие квоты агента.</summary>
	/// <param name="http">Общий HTTP-клиент программы.</param>
	/// <param name="cancellation">Отмена запроса.</param>
	/// <returns>Снимок квот.</returns>
	/// <exception cref="SignInException">Вход истёк или отклонён сервером.</exception>
	/// <exception cref="RateLimitedException">Сервер просит подождать.</exception>
	Task<Snapshot> FetchAsync(HttpClient http, CancellationToken cancellation);
}
