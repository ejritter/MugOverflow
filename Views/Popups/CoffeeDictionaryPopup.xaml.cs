using CommunityToolkit.Maui.Views;
using MugOverflowProject.ViewModels;

namespace MugOverflowProject.Views.Popups;

public partial class CoffeeDictionaryPopup : Popup
{
	public CoffeeDictionaryPopup(CoffeeDictionaryPopupViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = viewModel;

		viewModel.RequestClose = async () =>
		{
			viewModel.RequestClose = null;
			await CloseAsync(CancellationToken.None);
		};

		Closed += (s, e) =>
		{
			viewModel.RequestClose = null;
			BindingContext = null;
		};
	}

	private async void OnCloseClicked(object? sender, EventArgs e)
	{
		if (BindingContext is CoffeeDictionaryPopupViewModel vm)
		{
			vm.RequestClose = null;
		}
		await CloseAsync(CancellationToken.None);
	}
}
