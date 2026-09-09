using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Views;
using MugOverflowProject.ViewModels;

namespace MugOverflowProject.Views.Popups;

public partial class StorageSelectionPopup : Popup
{
	private readonly StorageSelectionViewModel _viewModel;
	private bool _isCleanedUp;

	public StorageSelectionPopup(StorageSelectionViewModel viewModel)
	{
		InitializeComponent();
		_viewModel = viewModel;
		BindingContext = viewModel;

		_viewModel.RequestClose = async () =>
		{
			await SafeCloseAsync();
		};

		this.Closed += (s, e) => CleanUp();
	}

	public void CleanUp()
	{
		if (_isCleanedUp)
			return;
		_isCleanedUp = true;

		if (_viewModel != null)
		{
			_viewModel.RequestClose = null;
			_viewModel.Dispose();
		}
		BindingContext = null;
	}

	private async void OnCloseClicked(object? sender, EventArgs e)
	{
		await SafeCloseAsync();
	}

	private async Task SafeCloseAsync()
	{
		try
		{
			await CloseAsync(CancellationToken.None);
		}
		catch
		{
			// Safe suppression if popup is already closed or closing
		}
	}
}
