using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MugOverflowProject.Resources.Styles.Themes;
using MugOverflowProject.Services;

namespace MugOverflowProject.ViewModels;

/// <summary>
/// ViewModel for the ThemeSelectorPopup, presenting the 6 available application palettes
/// and orchestrating theme switching and persistent storage.
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
		"Regal Earth",
		"Wine Desert",
		"Forest Lavender",
		"Pristine Clean",
		"Coffee Light",
		"Coffee Dark"
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
