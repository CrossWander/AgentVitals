using System;
using Avalonia;
using Avalonia.Controls;

namespace AgentVitals;

internal static class Program
{
	[STAThread]
	private static int Main(string[] args) =>
		AppBuilder.Configure<App>()
			.UsePlatformDetect()
			.LogToTrace()
			.StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
}
