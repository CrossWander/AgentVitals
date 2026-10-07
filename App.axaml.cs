using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace AgentVitals;

internal partial class App : Application
{
	private TrayApp? _tray;

	public override void Initialize() => AvaloniaXamlLoader.Load(this);

	public override void OnFrameworkInitializationCompleted()
	{
		// Программа живёт в трее неделями: случайная ошибка в обработчике не должна её закрывать.
		Dispatcher.UIThread.UnhandledException += (_, e) =>
		{
			Journal.Add($"unhandled: {e.Exception}");
			e.Handled = true;
		};

		Journal.Trim();
		Localization.Apply(Store.Current.Language);
		Theme.Apply(Store.Current.Theme);

		_tray = new TrayApp();
		_tray.Start();

		base.OnFrameworkInitializationCompleted();
	}
}
