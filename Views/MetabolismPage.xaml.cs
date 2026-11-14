using CommunityToolkit.Mvvm.Messaging;
using MugOverflowProject.Messages;
using MugOverflowProject.ViewModels;

namespace MugOverflowProject.Views;

public partial class MetabolismPage : ContentPage
{
	private const double FourColumnKpiThresholdDp = 960.0;

	private readonly MetabolismViewModel _viewModel;
	private bool _isInitialized;
	private bool? _isFourColumnKpi;

	public MetabolismPage(MetabolismViewModel viewModel)
	{
		try
		{
			InitializeComponent();
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[MetabolismPage] InitializeComponent fallback: {ex}");
		}

		_viewModel = viewModel;
		BindingContext = viewModel;

		WeakReferenceMessenger.Default.Register<MetabolismPage, InvalidateMetabolismChartMessage>(
			this, static (recipient, _) => recipient.InvalidateChartIfVisible());

		if (MetabolismChartView != null)
		{
			MetabolismChartView.Loaded += OnMetabolismChartViewLoaded;
		}

		Unloaded += OnMetabolismPageUnloaded;
	}

	internal void SetChartViewForTesting(GraphicsView chartView)
	{
		MetabolismChartView = chartView;
	}

	private void OnMetabolismChartViewLoaded(object? sender, EventArgs e)
	{
		InvalidateChartIfVisible();
	}

	private void OnMetabolismPageUnloaded(object? sender, EventArgs e)
	{
		if (MetabolismChartView != null)
		{
			MetabolismChartView.Loaded -= OnMetabolismChartViewLoaded;
		}

		WeakReferenceMessenger.Default.Unregister<InvalidateMetabolismChartMessage>(this);
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();

		if (!WeakReferenceMessenger.Default.IsRegistered<InvalidateMetabolismChartMessage>(this))
		{
			WeakReferenceMessenger.Default.Register<MetabolismPage, InvalidateMetabolismChartMessage>(
				this, static (recipient, _) => recipient.InvalidateChartIfVisible());
		}

		if (MetabolismChartView != null)
		{
			MetabolismChartView.Loaded -= OnMetabolismChartViewLoaded;
			MetabolismChartView.Loaded += OnMetabolismChartViewLoaded;
		}

		InvalidateChartIfVisible();

		if (!_isInitialized)
		{
			_isInitialized = true;
			try
			{
				await _viewModel.InitializeAsync();
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[MetabolismPage] Error during InitializeAsync: {ex}");
			}
		}
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		_viewModel.NotifyNavigatingToMainPage();
	}

	protected override void OnSizeAllocated(double width, double height)
	{
		base.OnSizeAllocated(width, height);

		if (width <= 0)
			return;

		if (MetabolismThemeLabel != null)
		{
			MetabolismThemeLabel.IsVisible = width >= 940.0;
		}

		if (MetabolismSyncLabel != null)
		{
			MetabolismSyncLabel.IsVisible = width >= 780.0;
		}

		if (MetabolismHeaderSubtitle != null)
		{
			MetabolismHeaderSubtitle.IsVisible = width >= 860.0;
		}

		bool shouldUseFourCols = width >= FourColumnKpiThresholdDp;
		if (_isFourColumnKpi != shouldUseFourCols)
		{
			_isFourColumnKpi = shouldUseFourCols;
			ApplyResponsiveKpiGrid(shouldUseFourCols);
		}

		InvalidateChartIfVisible();
	}

	private void ApplyResponsiveKpiGrid(bool fourColumns)
	{
		if (BentoKpiGrid == null || KpiTileActive == null || KpiTileToday == null || KpiTileAligned == null || KpiTileSleep == null)
			return;

		BentoKpiGrid.BatchBegin();
		try
		{
			BentoKpiGrid.RowDefinitions.Clear();
			BentoKpiGrid.ColumnDefinitions.Clear();

			if (fourColumns)
			{
				BentoKpiGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
				BentoKpiGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
				BentoKpiGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
				BentoKpiGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
				BentoKpiGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

				Grid.SetRow(KpiTileActive, 0);
				Grid.SetColumn(KpiTileActive, 0);

				Grid.SetRow(KpiTileToday, 0);
				Grid.SetColumn(KpiTileToday, 1);

				Grid.SetRow(KpiTileAligned, 0);
				Grid.SetColumn(KpiTileAligned, 2);

				Grid.SetRow(KpiTileSleep, 0);
				Grid.SetColumn(KpiTileSleep, 3);
			}
			else
			{
				BentoKpiGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
				BentoKpiGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
				BentoKpiGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
				BentoKpiGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

				Grid.SetRow(KpiTileActive, 0);
				Grid.SetColumn(KpiTileActive, 0);

				Grid.SetRow(KpiTileToday, 0);
				Grid.SetColumn(KpiTileToday, 1);

				Grid.SetRow(KpiTileAligned, 1);
				Grid.SetColumn(KpiTileAligned, 0);

				Grid.SetRow(KpiTileSleep, 1);
				Grid.SetColumn(KpiTileSleep, 1);
			}
		}
		finally
		{
			BentoKpiGrid.BatchCommit();
		}
	}

	private void InvalidateChartIfVisible()
	{
		if (MetabolismChartView != null)
		{
			try
			{
				if (MainThread.IsMainThread)
				{
					MetabolismChartView.Invalidate();
				}
				else
				{
					MainThread.BeginInvokeOnMainThread(() =>
					{
						MetabolismChartView?.Invalidate();
					});
				}
			}
			catch (Exception ex) when (ex.GetType().Name.Contains("NotImplemented"))
			{
				// Unit test environments where MainThread is not implemented
				try
				{
					MetabolismChartView.Invalidate();
				}
				catch
				{
				}
			}
			catch (Exception)
			{
				// Suppress background thread execution fallback on InvalidOperationException, COM crashes, or window deactivation
			}
		}
	}
}
