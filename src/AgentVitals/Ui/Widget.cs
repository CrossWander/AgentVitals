using System;
using Avalonia;
using AgentVitals.Core.Sessions;

namespace AgentVitals;

/// <summary>Вид виджета из настроек; включён только один.</summary>
internal enum WidgetKind
{
	Off,
	Notch,
	Chip,
	Desktop,
}

/// <summary>Как раскрыт виджет: в покое (у плашки — полоска), целиком под мышью, панель после задержки.</summary>
internal enum WidgetLook
{
	Rest,
	Peek,
	Panel,
}

/// <summary>Наведение: мышь над виджетом — целиком; держится полсекунды — панель; ушла — покой. Мимоходом к вкладке
/// браузера мышь над полоской задерживается меньше — панель её не перехватит.</summary>
internal sealed class WidgetHover
{
	public static readonly TimeSpan Hold = TimeSpan.FromMilliseconds(500);

	// С какого момента мышь над виджетом; null — её там нет.
	private DateTime? _since;

	public WidgetLook Next(bool over, DateTime now)
	{
		if (!over)
		{
			_since = null;

			return WidgetLook.Rest;
		}

		_since ??= now;

		return now - _since >= Hold ? WidgetLook.Panel : WidgetLook.Peek;
	}
}

/// <summary>Окно одного вида виджета. Win32 и разметка — внутри; хозяин водит только это.</summary>
internal interface IWidgetWindow
{
	event Action<string>? AgentChosen;

	event Action<Session>? SessionChosen;

	/// <summary>Где окно сейчас, в точках экрана: над ним ли мышь, не закрыть ли его тостом.</summary>
	PixelRect Area { get; }

	/// <summary>Показать на своём месте у экрана значка трея; уже открыто — встать на место заново.</summary>
	void Open(PixelRect anchor);

	void Render(WidgetView view);

	void Show(WidgetLook look);

	/// <summary>Глаз: окно не попадает на снимки и в демонстрацию экрана.</summary>
	void Private(bool hide);

	/// <summary>Раз в 150 мс: полноэкранное приложение, Win+D, переехавшая панель задач — забота самого окна.</summary>
	void Tick();

	void Close();
}

/// <summary>Держит одно окно выбранного вида: открывает, закрывает, передаёт снимок, глаз и наведение.</summary>
/// <param name="make">Окно вида; Off сюда не приходит.</param>
internal sealed class WidgetHost(Func<WidgetKind, IWidgetWindow> make)
{
	private readonly WidgetHover _hover = new();
	private IWidgetWindow? _window;
	private WidgetView? _view;
	private bool _hide;

	public WidgetKind Kind { get; private set; }

	/// <summary>Щелчок по агенту — поповер на его вкладке.</summary>
	public event Action<string>? AgentChosen;

	/// <summary>Щелчок по сессии — её окно.</summary>
	public event Action<Session>? SessionChosen;

	/// <summary>Значок над треем на экране — тосты встают над ним; иначе null.</summary>
	public PixelRect? Chip => Kind == WidgetKind.Chip ? _window?.Area : null;

	/// <summary>Тот же вид — ничего; другой — прежнее окно закрыть, новое открыть у значка; Off — закрыть.</summary>
	/// <param name="anchor">Где значок трея: окно встаёт на его экране.</param>
	public void Apply(WidgetKind kind, PixelRect anchor)
	{
		if (kind == Kind)
		{
			return;
		}

		_window?.Close();
		_window = null;
		Kind = kind;

		if (kind == WidgetKind.Off)
		{
			return;
		}

		var window = make(kind);
		window.AgentChosen += agent => AgentChosen?.Invoke(agent);
		window.SessionChosen += session => SessionChosen?.Invoke(session);
		window.Private(_hide);

		// Снимок до показа: окно сразу своего размера и встаёт на место без скачка.
		if (_view is { } view)
		{
			window.Render(view);
		}

		window.Open(anchor);
		_window = window;
	}

	/// <param name="hide">Глаз: окно не попадает на снимки, проектов не видно (их прячет уже снимок).</param>
	public void Render(WidgetView view, bool hide)
	{
		_view = view;

		if (hide != _hide)
		{
			_hide = hide;
			_window?.Private(hide);
		}

		_window?.Render(view);
	}

	/// <summary>Экраны или DPI сменились — окно встаёт на место заново.</summary>
	public void Replace(PixelRect anchor) => _window?.Open(anchor);

	/// <summary>Тик таймера: мышь, а у «ждёт» и «закончил» плашка видна целиком и без неё.</summary>
	public void Tick(PixelPoint cursor, DateTime now)
	{
		if (_window is not { } window)
		{
			return;
		}

		var look = _hover.Next(window.Area.Contains(cursor), now);

		if (look == WidgetLook.Rest && _view is { Loud: true })
		{
			look = WidgetLook.Peek;
		}

		window.Show(look);
		window.Tick();
	}
}
