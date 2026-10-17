using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using AgentVitals.Agents;
using AgentVitals.Core.Sessions;
using AgentVitals.Platform;

namespace AgentVitals;

internal static class Program
{
	[STAThread]
	private static int Main(string[] args)
	{
		// Hook зовут на каждое событие агента: без интерфейса и мьютекса — прочитать
		// событие, записать состояние и выйти как можно быстрее.
		if (args.FirstOrDefault() == "hook")
		{
			// «hook codex» — от Codex, просто «hook» — от Claude Code: так подключались до Codex, и эти hooks работают как были.
			var target = AgentCatalog.Hook(args.ElementAtOrDefault(1));

			// Время запуска процесса, а не входа в Main: прогрев рантайма скачет на сотни миллисекунд
			// и переставил бы события, которые агент запустил одно за другим.
			HookWriter.Run(
				Console.OpenStandardInput(),
				Paths.Sessions,
				Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks,
				AgentProcess.Find(target.Processes),
				target.Agent,
				target.Transcript);

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
