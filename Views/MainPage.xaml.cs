using MugOverflowProject.ViewModels;

namespace MugOverflowProject.Views;

public partial class MainPage : ContentPage
{
	private readonly MainViewModel _viewModel;

	public MainPage(MainViewModel viewModel)
	{
		InitializeComponent();
		_viewModel = viewModel;
		BindingContext = viewModel;

		_viewModel.RequestChartInvalidation = () =>
		{
			MainThread.BeginInvokeOnMainThread(() =>
			{
				MetabolismChartView?.Invalidate();
			});
		};
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await _viewModel.InitializeAsync();
	}
}
