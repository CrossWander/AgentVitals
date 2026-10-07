using System;
using Avalonia;
using Avalonia.Styling;

namespace AgentVitals;

/// <summary>Что выбрано в настройках: «как в системе» — тоже выбор.</summary>
internal enum ThemeMode
{
	Auto,
	Light,
	Dark,
}

/// <summary>Тема: роли палитры Avalonia подставляет сама по варианту темы.</summary>
internal static class Theme
{
	public static void Apply(ThemeMode mode)
	{
		Application.Current!.RequestedThemeVariant = mode switch
		{
			ThemeMode.Auto => ThemeVariant.Default,
			ThemeMode.Light => ThemeVariant.Light,
			ThemeMode.Dark => ThemeVariant.Dark,
			_ => throw new NotImplementedException("ThemeMode switch error"),
		};

		if (Store.Current.Theme != mode)
		{
			Store.Current.Theme = mode;
			Store.Save();
		}
	}
}
