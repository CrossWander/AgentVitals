using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;

namespace AgentVitals;

internal static class Program
{
	[STAThread]
	private static int Main(string[] args)
	{
		// Hook зовут на каждое событие Claude Code: без интерфейса и мьютекса — прочитать
		// событие, записать состояние и выйти как можно быстрее.
		if (args.FirstOrDefault() == "hook")
		{
			// Время запуска процесса, а не входа в Main: прогрев рантайма скачет на сотни миллисекунд
			// и переставил бы события, которые Claude Code запустил одно за другим.
			HookWriter.Run(Console.OpenStandardInput(), Paths.Sessions, Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks);

			return 0;
		}

		using var single = new Mutex(true, "AgentVitals", out var owned);
		if (!owned)
		{
			return 0;
		}

		return AppBuilder.Configure<App>()
			.UsePlatformDetect()
			.LogToTrace()
			.StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
	}
}
