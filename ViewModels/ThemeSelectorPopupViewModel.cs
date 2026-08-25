using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MugOverflowProject.Resources.Styles.Themes;
using MugOverflowProject.Services;

namespace MugOverflowProject.ViewModels;

public sealed partial class ThemeSelectorPopupViewModel(IDataPersistenceService dataPersistenceService) : ObservableObject
{
	[ObservableProperty]
	public partial string SelectedThemeName { get; set; } = ThemeManager.CurrentThemeName;

	public ObservableCollection<string> Themes { get; } =
	[
		"Regal Earth",
		"Wine Desert",
		"Forest Lavender",
		"Pristine Clean"
	];

	[RelayCommand]
	private void SelectTheme(string theme)
	{
		SelectedThemeName = theme;
		ThemeManager.ApplyTheme(theme);
		dataPersistenceService.SaveTheme(theme);
	}
}
