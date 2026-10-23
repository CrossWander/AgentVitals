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

/// <summary>Как светятся плашка и значок на события; Off — не светятся.</summary>
internal enum GlowStyle
{
	Off,
	Halo,
	Sheen,
	Wash,
}

/// <summary>Как раскрыт виджет: в покое (у плашки — полоска), плашка целиком, панель по щелчку.</summary>
internal enum WidgetLook
{
	Rest,
	Pill,
	Panel,
}

/// <summary>Наведение: мышь задержалась над виджетом на 0,3 с — плашка выезжает и ловит щелчки; щелчок открывает и
/// закрывает панель; мышь ушла — раскрытое держится ещё полсекунды. Мимоходом к вкладке браузера мышь над полоской
/// задерживается меньше — щелчок проходит насквозь.</summary>
internal sealed class WidgetHover
{
	public static readonly TimeSpan Arm = TimeSpan.FromMilliseconds(300);

	public static readonly TimeSpan Linger = TimeSpan.FromMilliseconds(500);

	// С какого момента мышь над виджетом; null — её там нет.
	private DateTime? _over;

	// Когда мышь ушла с раскрытого; null — не уходила.
	private DateTime? _left;

	private bool _armed;
	private bool _panel;

	/// <summary>Раскрыт ли виджет мышью: тогда он нажимается, пока курсор над ним.</summary>
	public bool Armed => _armed;

	public WidgetLook Look => !_armed ? WidgetLook.Rest : _panel ? WidgetLook.Panel : WidgetLook.Pill;

	public WidgetLook Next(bool over, DateTime now)
	{
		if (over)
		{
			_left = null;
			_over ??= now;
			_armed |= now - _over >= Arm;
		}
		else
		{
			// Ушла с раскрытого только что — промах на пару точек его не закрывает; мимоходом — сразу всё заново.
			_left ??= now;

			if (!_armed || now - _left >= Linger)
			{
				_over = null;
				_left = null;
				_armed = false;
				_panel = false;
			}
		}

		return Look;
	}

	/// <summary>Щелчок по плашке или значку. Пока виджет не раскрыт, щелчки до него не доходят; запоздавший — ничего.</summary>
	public void Click()
	{
		if (_armed)
		{
			_panel = !_panel;
		}
	}
}

/// <summary>Окно одного вида виджета. Win32 и разметка — внутри; хозяин водит только это.</summary>
internal interface IWidgetWindow
{
	event Action<string>? AgentChosen;

	event Action<Session>? SessionChosen;

	/// <summary>Щелчок по плашке или значку, не по строке панели.</summary>
	event Action? Clicked;

	/// <summary>Где окно сейчас, в точках экрана: над ним ли мышь, не закрыть ли его тостом.</summary>
	PixelRect Area { get; }

	/// <summary>Показать на своём месте у экрана значка трея; уже открыто — встать на место заново.</summary>
	void Open(PixelRect anchor);

	void Render(WidgetView view);

	/// <param name="clickable">Ловит ли окно щелчки; нет — они проходят насквозь к окну под ним.</param>
	void Show(WidgetLook look, bool clickable);

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

	// Курсор над виджетом в последний тик.
	private bool _over;

	public WidgetKind Kind { get; private set; }

	/// <summary>Плашка видна целиком и в покое — настройка «В покое: плашка»; щелчки проходят, пока мышь не задержится.</summary>
	public bool Pinned { get; set; }

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
		window.Clicked += () =>
		{
			// Плашка говорит о ждущей сессии — щелчок сразу к её окну; панель — когда ждать некого.
			if (_view?.Waiting is { } waiting)
			{
				SessionChosen?.Invoke(waiting);

				return;
			}

			_hover.Click();

			// Панель — сразу, не со следующим тиком.
			if (_window == window)
			{
				Show(window);
			}
		};
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

		_over = window.Area.Contains(cursor);
		_hover.Next(_over, now);
		Show(window);
		window.Tick();
	}

	// Нажимается только раскрытый мышью виджет и только под курсором: ушла мышь — адресная строка под панелью
	// нажимается сразу, хотя панель ещё видна.
	private void Show(IWidgetWindow window)
	{
		var look = _hover.Look;

		if (look == WidgetLook.Rest && (Pinned || _view is { Loud: true }))
		{
			look = WidgetLook.Pill;
		}

		window.Show(look, _over && _hover.Armed);
	}
}
