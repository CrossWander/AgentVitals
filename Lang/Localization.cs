using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Markup.Xaml.Styling;

namespace AgentVitals;

/// <summary>Язык интерфейса: словарь строк подменяется целиком, DynamicResource перечитывает подписи сам.</summary>
internal static class Localization
{
	public static readonly string[] Codes = ["en", "ru", "uk"];

	private static ResourceInclude? _strings;

	public static string Current { get; private set; } = "en";

	public static CultureInfo Culture => CultureInfo.GetCultureInfo(Current);

	/// <summary>Язык системы, если он среди наших, иначе английский.</summary>
	public static string SystemDefault() =>
		Codes.Contains(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)
			? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
			: "en";

	public static void Apply(string code)
	{
		if (!Codes.Contains(code))
		{
			code = "en";
		}

		var strings = new ResourceInclude(new Uri("avares://AgentVitals/"))
		{
			Source = new Uri($"avares://AgentVitals/Lang/{code}.axaml"),
		};

		var dictionaries = Application.Current!.Resources.MergedDictionaries;
		if (_strings is not null)
		{
			dictionaries.Remove(_strings);
		}

		dictionaries.Add(strings);
		_strings = strings;
		Current = code;
	}

	public static string Get(string key) =>
		_strings is not null && _strings.TryGetResource(key, null, out var value) && value is string text ? text : key;

	/// <summary>Есть ли такая строка: у агента бывает своя, а нет — берётся общая.</summary>
	public static bool Has(string key) => _strings is not null && _strings.TryGetResource(key, null, out var value) && value is string;

	public static string Format(string key, params object?[] arguments) => string.Format(Culture, Get(key), arguments);
}
