using System.ComponentModel;
using Microsoft.Maui.Controls.Shapes;
using MugOverflowProject.ViewModels;

namespace MugOverflowProject.Views;

public partial class MainPage : ContentPage
{
	private readonly MainViewModel _viewModel;
	private readonly Dictionary<VisualElement, CancellationTokenSource> _activeDotCts = new();
	private bool _isInitialized;

	public MainPage(MainViewModel viewModel)
	{
		InitializeComponent();
		_viewModel = viewModel;
		BindingContext = viewModel;

		_viewModel.RequestChartInvalidation = InvalidateChart;
		Unloaded += OnMainPageUnloaded;
	}

	private void OnMainPageUnloaded(object? sender, EventArgs e)
	{
		_viewModel.PropertyChanged -= OnViewModelPropertyChanged;
		_viewModel.RequestChartInvalidation = null;
		CleanupAllAnimations();
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		_viewModel.RequestChartInvalidation = InvalidateChart;
		_viewModel.PropertyChanged -= OnViewModelPropertyChanged;
		_viewModel.PropertyChanged += OnViewModelPropertyChanged;
		InvalidateChart();

		if (!_isInitialized)
		{
			_isInitialized = true;
			await _viewModel.InitializeAsync();
		}
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		_viewModel.PropertyChanged -= OnViewModelPropertyChanged;
		_viewModel.RequestChartInvalidation = null;
		CleanupAllAnimations();
	}

	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(MainViewModel.IsOracleThinking) && !_viewModel.IsOracleThinking)
		{
			CleanupAllAnimations();
		}
	}

	private void InvalidateChart()
	{
		if (MainThread.IsMainThread)
		{
			MetabolismChartView?.Invalidate();
		}
		else
		{
			MainThread.BeginInvokeOnMainThread(() =>
			{
				MetabolismChartView?.Invalidate();
			});
		}
	}

	private void CleanupStaleDots()
	{
		if (_activeDotCts.Count == 0) return;

		var staleKeys = new List<VisualElement>();
		foreach (var (dot, cts) in _activeDotCts)
		{
			if (!dot.IsLoaded || dot.Window == null)
			{
				staleKeys.Add(dot);
				try
				{
					cts.Cancel();
					cts.Dispose();
				}
				catch
				{
					// Safe CTS disposal
				}
				dot.AbortAnimation("TypingPulse");
			}
		}

		foreach (var stale in staleKeys)
		{
			_activeDotCts.Remove(stale);
		}
	}

	private void OnTypingIndicatorLoaded(object? sender, EventArgs e)
	{
		if (sender is not VisualElement root)
			return;

		CleanupStaleDots();

		var dots = FindDots(root);
		for (int i = 0; i < dots.Count; i++)
		{
			var dot = dots[i];
			int delayMs = i * 180;

			if (_activeDotCts.Remove(dot, out var existingCts))
			{
				try
				{
					existingCts.Cancel();
					existingCts.Dispose();
				}
				catch
				{
					// Safe CTS disposal
				}
			}
			dot.AbortAnimation("TypingPulse");

			var cts = new CancellationTokenSource();
			_activeDotCts[dot] = cts;
			_ = AnimateDotAsync(dot, delayMs, cts.Token);
		}
	}

	private void OnTypingIndicatorUnloaded(object? sender, EventArgs e)
	{
		if (sender is not VisualElement root)
			return;

		var dots = FindDots(root);
		foreach (var dot in dots)
		{
			if (_activeDotCts.Remove(dot, out var cts))
			{
				try
				{
					cts.Cancel();
					cts.Dispose();
				}
				catch
				{
					// Safe CTS disposal
				}
			}
			dot.AbortAnimation("TypingPulse");
			dot.Opacity = 0.3;
		}
	}

	private void CleanupAllAnimations()
	{
		foreach (var (dot, cts) in _activeDotCts)
		{
			try
			{
				cts.Cancel();
				cts.Dispose();
			}
			catch
			{
				// Safe cancellation
			}
			dot.AbortAnimation("TypingPulse");
		}
		_activeDotCts.Clear();
	}

	private static async Task AnimateDotAsync(VisualElement dot, int delayMs, CancellationToken token)
	{
		try
		{
			if (delayMs > 0)
			{
				await Task.Delay(delayMs, token).ConfigureAwait(true);
			}

			if (token.IsCancellationRequested || !dot.IsLoaded)
				return;

			dot.AbortAnimation("TypingPulse");

			var pulseAnimation = new Animation
			{
				{ 0.0, 0.5, new Animation(v => dot.Opacity = v, 0.25, 1.0, Easing.CubicInOut) },
				{ 0.5, 1.0, new Animation(v => dot.Opacity = v, 1.0, 0.25, Easing.CubicInOut) }
			};

			pulseAnimation.Commit(
				dot,
				"TypingPulse",
				length: 900,
				repeat: () => !token.IsCancellationRequested && dot.IsLoaded);
		}
		catch (OperationCanceledException)
		{
			// Expected on cancellation/unloading
		}
		catch
		{
			// Safe fall-through
		}
	}

	private static List<VisualElement> FindDots(VisualElement root)
	{
		var results = new List<VisualElement>();
		CollectDots(root, results);
		return results;
	}

	private static void CollectDots(Element element, List<VisualElement> results)
	{
		if (element is Ellipse ellipse)
		{
			results.Add(ellipse);
		}
		else if (element is Layout layout)
		{
			foreach (var child in layout.Children)
			{
				if (child is Element childElement)
				{
					CollectDots(childElement, results);
				}
			}
		}
		else if (element is Border border && border.Content is Element borderContent)
		{
			CollectDots(borderContent, results);
		}
		else if (element is ContentView contentView && contentView.Content is Element cvContent)
		{
			CollectDots(cvContent, results);
		}
	}
}

