using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AgentVitals;

/// <summary>Источник квот одного AI-агента.</summary>
internal interface IProvider
{
	/// <summary>Имя агента в интерфейсе.</summary>
	string Name { get; }

	/// <summary>Команда CLI, запуск которой обновляет вход агента.</summary>
	string Cli { get; }

	/// <summary>Файл авторизации агента: его переписывает CLI, когда обновляет вход.</summary>
	string Credentials { get; }

	/// <summary>Страница расхода у самого агента, в браузере; null — такой нет.</summary>
	string? Dashboard { get; }

	/// <summary>Читатель локальных логов агента; null — статистики у агента пока нет.</summary>
	IUsageLog? Usage { get; }

	/// <summary>Страница здоровья сервиса агента; null — у агента её нет.</summary>
	IStatusPage? Status { get; }

	/// <summary>Получить текущие квоты агента.</summary>
	/// <param name="http">Общий HTTP-клиент программы.</param>
	/// <param name="cancellation">Отмена запроса.</param>
	/// <returns>Снимок квот.</returns>
	/// <exception cref="TokenExpiredException">Вход истёк или отклонён сервером.</exception>
	/// <exception cref="RateLimitedException">Сервер просит подождать.</exception>
	Task<Snapshot> FetchAsync(HttpClient http, CancellationToken cancellation);
}
