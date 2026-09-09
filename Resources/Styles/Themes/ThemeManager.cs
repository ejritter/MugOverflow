using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui.Controls;

namespace MugOverflowProject.Resources.Styles.Themes;

/// <summary>
/// Manages runtime theme switching by swapping the active theme ResourceDictionary
/// at Index 1 of Application.Current.Resources.MergedDictionaries, while safely
/// supporting headless execution and unit test isolation.
/// </summary>
public static class ThemeManager
{
	public const string DefaultTheme = "Regal Earth";

	/// <summary>
	/// Gets the name of the currently applied theme.
	/// Defaults to "Regal Earth".
	/// </summary>
	public static string CurrentThemeName { get; private set; } = DefaultTheme;

	/// <summary>
	/// Applies the requested theme to the active MAUI Application merged dictionaries.
	/// Safely updates CurrentThemeName even if Application.Current is null (headless tests).
	/// </summary>
	/// <param name="themeName">The human-readable name of the theme to apply.</param>
	public static void ApplyTheme(string? themeName)
	{
		ApplyTheme(themeName, Application.Current?.Resources?.MergedDictionaries);
	}

	/// <summary>
	/// Applies the requested theme to the specified dictionary collection, preserving
	/// the active theme at Index 1 (between Colors.xaml at 0 and Styles.xaml at 2).
	/// </summary>
	/// <param name="themeName">The human-readable name of the theme to apply.</param>
	/// <param name="dictionaries">The target merged dictionary collection, or null for headless mode.</param>
	public static void ApplyTheme(string? themeName, ICollection<ResourceDictionary>? dictionaries)
	{
		ResourceDictionary selectedTheme = CreateThemeDictionary(themeName);

		// Resolve CurrentThemeName dynamically from selectedTheme["ThemeName"]
		if (selectedTheme.TryGetValue("ThemeName", out var nameVal) &&
		    nameVal is string resolvedName &&
		    !string.IsNullOrWhiteSpace(resolvedName))
		{
			CurrentThemeName = resolvedName;
		}
		else
		{
			CurrentThemeName = DefaultTheme;
		}

		if (dictionaries is null)
		{
			return;
		}

		if (dictionaries is IList<ResourceDictionary> list)
		{
			int existingIndex = -1;
			for (int i = 0; i < list.Count; i++)
			{
				if (IsThemeDictionary(list[i]))
				{
					existingIndex = i;
					break;
				}
			}

			if (existingIndex >= 0)
			{
				list.RemoveAt(existingIndex);
				list.Insert(existingIndex, selectedTheme);

				// Clean up any extraneous legacy theme dictionaries
				for (int i = list.Count - 1; i > existingIndex; i--)
				{
					if (IsThemeDictionary(list[i]))
					{
						list.RemoveAt(i);
					}
				}
			}
			else
			{
				// If no existing theme dictionary exists, insert at Index 1 if Colors.xaml is present,
				// or clamp within bounds.
				int targetIndex = Math.Clamp(1, 0, list.Count);
				list.Insert(targetIndex, selectedTheme);
			}
		}
		else
		{
			var existingThemes = dictionaries.Where(IsThemeDictionary).ToList();
			foreach (var existing in existingThemes)
			{
				dictionaries.Remove(existing);
			}
			dictionaries.Add(selectedTheme);
		}
	}

	/// <summary>
	/// Instantiates a ResourceDictionary corresponding to the specified theme name.
	/// Supports case-insensitive matching and falls back to RegalEarthTheme.
	/// </summary>
	public static ResourceDictionary CreateThemeDictionary(string? themeName) => themeName?.Trim() switch
	{
		var name when string.Equals(name, "Coffee Light", StringComparison.OrdinalIgnoreCase) => new CoffeeLightTheme(),
		var name when string.Equals(name, "Coffee Dark", StringComparison.OrdinalIgnoreCase) => new CoffeeDarkTheme(),
		var name when string.Equals(name, "Wine Desert", StringComparison.OrdinalIgnoreCase) => new WineDesertTheme(),
		var name when string.Equals(name, "Forest Lavender", StringComparison.OrdinalIgnoreCase) => new ForestLavenderTheme(),
		var name when string.Equals(name, "Pristine Clean", StringComparison.OrdinalIgnoreCase) => new PristineCleanTheme(),
		_ => new RegalEarthTheme()
	};

	/// <summary>
	/// Determines whether a given ResourceDictionary represents an application theme dictionary.
	/// Checks concrete theme types, the "ThemeName" key, and theme source paths.
	/// </summary>
	public static bool IsThemeDictionary(ResourceDictionary? d)
	{
		if (d is null)
			return false;

		if (d is CoffeeLightTheme ||
		    d is CoffeeDarkTheme ||
		    d is RegalEarthTheme ||
		    d is WineDesertTheme ||
		    d is ForestLavenderTheme ||
		    d is PristineCleanTheme)
		{
			return true;
		}

		if (d.TryGetValue("ThemeName", out var val) &&
		    val is string name &&
		    !string.IsNullOrWhiteSpace(name))
		{
			return true;
		}

		if (d.Source is not null &&
		    d.Source.OriginalString.Contains("Theme", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		return false;
	}

	/// <summary>
	/// Resets the current theme name to the default ("Regal Earth") for testing isolation.
	/// </summary>
	public static void ResetToDefault()
	{
		CurrentThemeName = DefaultTheme;
	}
}
