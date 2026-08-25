using Microsoft.Maui.Controls;
using MugOverflowProject.Resources.Styles.Themes;

namespace MugOverflowProject.Resources.Styles.Themes;

public static class ThemeManager
{
	public static string CurrentThemeName { get; private set; } = "Regal Earth";

	public static void ApplyTheme(string themeName)
	{
		var dictionaries = Application.Current?.Resources.MergedDictionaries;
		if (dictionaries is null)
			return;

		var existingTheme = dictionaries.FirstOrDefault(d =>
			d is RegalEarthTheme ||
			d is WineDesertTheme ||
			d is ForestLavenderTheme ||
			d is PristineCleanTheme);

		if (existingTheme is not null)
			dictionaries.Remove(existingTheme);

		ResourceDictionary selectedTheme = themeName switch
		{
			"Wine Desert" => new WineDesertTheme(),
			"Forest Lavender" => new ForestLavenderTheme(),
			"Pristine Clean" => new PristineCleanTheme(),
			_ => new RegalEarthTheme()
		};

		CurrentThemeName = themeName switch
		{
			"Wine Desert" => "Wine Desert",
			"Forest Lavender" => "Forest Lavender",
			"Pristine Clean" => "Pristine Clean",
			_ => "Regal Earth"
		};

		dictionaries.Add(selectedTheme);
	}
}
