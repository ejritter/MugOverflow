using CommunityToolkit.Maui.Views;
using MugOverflowProject.ViewModels;

namespace MugOverflowProject.Views.Popups;

public partial class ThemeSelectorPopup : Popup
{
	private readonly ThemeSelectorPopupViewModel _viewModel;

	public ThemeSelectorPopup(ThemeSelectorPopupViewModel viewModel)
	{
		InitializeComponent();
		_viewModel = viewModel;
		BindingContext = viewModel;

		Closed += (s, e) =>
		{
			BindingContext = null;
		};
	}

	private async void OnCloseClicked(object? sender, EventArgs e)
	{
		await CloseAsync(CancellationToken.None);
	}
}
