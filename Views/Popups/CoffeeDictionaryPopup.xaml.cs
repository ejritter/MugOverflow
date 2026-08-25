using CommunityToolkit.Maui.Views;
using MugOverflowProject.ViewModels;

namespace MugOverflowProject.Views.Popups;

public partial class CoffeeDictionaryPopup : Popup
{
	public CoffeeDictionaryPopup(CoffeeDictionaryPopupViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = viewModel;
	}

	private async void OnCloseClicked(object? sender, EventArgs e)
	{
		await CloseAsync(CancellationToken.None);
	}
}
