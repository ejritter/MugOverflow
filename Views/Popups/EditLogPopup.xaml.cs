using CommunityToolkit.Maui.Views;
using MugOverflowProject.ViewModels;

namespace MugOverflowProject.Views.Popups;

public partial class EditLogPopup : Popup
{
	private readonly EditLogPopupViewModel _viewModel;

	public EditLogPopup(EditLogPopupViewModel viewModel)
	{
		InitializeComponent();
		_viewModel = viewModel;
		BindingContext = viewModel;
	}

	private async void OnCancelClicked(object? sender, EventArgs e)
	{
		await CloseAsync(CancellationToken.None);
	}

	private async void OnDeleteClicked(object? sender, EventArgs e)
	{
		_viewModel.DeleteCommand.Execute(null);
		await CloseAsync(CancellationToken.None);
	}

	private async void OnSaveClicked(object? sender, EventArgs e)
	{
		_viewModel.SaveCommand.Execute(null);
		await CloseAsync(CancellationToken.None);
	}
}
