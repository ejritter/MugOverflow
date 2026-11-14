using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MugOverflowProject.Resources.Styles.Themes;
using MugOverflowProject.Services;

namespace MugOverflowProject.ViewModels;

/// <summary>
/// ViewModel for the ThemeSelectorPopup, presenting the 10 available application palettes
/// (5 theme families, each with Light and Dark variants) and orchestrating theme switching and persistent storage.
/// </summary>
public sealed partial class ThemeSelectorPopupViewModel(IDataPersistenceService dataPersistenceService) : ObservableObject
{
	[ObservableProperty]
	public partial string SelectedThemeName { get; set; } = ThemeManager.CurrentThemeName;

	/// <summary>
	/// The collection of all selectable theme names in MugOverflow.
	/// </summary>
	public ObservableCollection<string> Themes { get; } =
	[
		"Espresso Crema Dark",
		"Steamed Oat Latte",
		"Caramel Macchiato",
		"Mocha Velvet Dark",
		"Iced Matcha Latte",
		"Dirty Matcha Dark",
		"Honey Lavender Latte",
		"Midnight Cold Brew",
		"Sweet Mint Cortado",
		"Dark Mint Espresso"
	];

	/// <summary>
	/// Applies the selected theme and persists the choice to application storage.
	/// </summary>
	[RelayCommand]
	private void SelectTheme(string? theme)
	{
		if (string.IsNullOrWhiteSpace(theme))
			return;

		ThemeManager.ApplyTheme(theme);
		SelectedThemeName = ThemeManager.CurrentThemeName;
		dataPersistenceService.SaveTheme(SelectedThemeName);
	}
}
