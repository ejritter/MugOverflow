using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.Maui.Controls.Shapes;
using MugOverflowProject.ViewModels;

namespace MugOverflowProject.Views;

public partial class MainPage : ContentPage
{
	private const string TypingPulseAnimName = "TypingPulse";
	private const string BubbleEntranceAnimName = "ChatBubbleEntrance";
	private const string DrawerTransitionAnimName = "CoffeeDrawerTransition";
	private const double DrawerClosedTranslationX = -324.0;
	private const double DrawerOpenBackdropOpacity = 0.48;
	private const double WideWorkspaceThresholdDp = 900.0;

	private readonly MainViewModel _viewModel;
	private readonly Dictionary<VisualElement, CancellationTokenSource> _activeDotCts = new();
	private readonly HashSet<VisualElement> _activeBubbles = new();
	private bool _isInitialized;
	private bool? _isWideWorkspace;

	public MainPage(MainViewModel viewModel)
	{
		InitializeComponent();

		_viewModel = viewModel;
		BindingContext = viewModel;

		Unloaded += OnMainPageUnloaded;
	}

	internal void TriggerOnAppearing()
	{
		OnAppearing();
	}

	internal void TriggerOnUnloaded()
	{
		OnMainPageUnloaded(this, EventArgs.Empty);
	}

	private void OnMainPageUnloaded(object? sender, EventArgs e)
	{
		_viewModel.PropertyChanged -= OnViewModelPropertyChanged;
		_viewModel.ChatMessages.CollectionChanged -= OnChatMessagesCollectionChanged;
		CleanupAllAnimations();
	}

	protected override void OnNavigatedTo(NavigatedToEventArgs args)
	{
		base.OnNavigatedTo(args);

		_viewModel.IsChatTabActive = true;
		_viewModel.IsStatsTabActive = false;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();

		_viewModel.IsChatTabActive = true;
		_viewModel.IsStatsTabActive = false;

		_viewModel.PropertyChanged -= OnViewModelPropertyChanged;
		_viewModel.PropertyChanged += OnViewModelPropertyChanged;
		_viewModel.ChatMessages.CollectionChanged -= OnChatMessagesCollectionChanged;
		_viewModel.ChatMessages.CollectionChanged += OnChatMessagesCollectionChanged;

		AnimateDrawerState(_viewModel.IsDrawerOpen, instant: true);

		if (!_isInitialized)
		{
			_isInitialized = true;
			try
			{
				await _viewModel.InitializeAsync();
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[MainPage] Error during InitializeAsync: {ex}");
			}
		}
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		_viewModel.PropertyChanged -= OnViewModelPropertyChanged;
		_viewModel.ChatMessages.CollectionChanged -= OnChatMessagesCollectionChanged;
		CleanupAllAnimations();
	}

	protected override void OnSizeAllocated(double width, double height)
	{
		base.OnSizeAllocated(width, height);

		if (width <= 0)
			return;

		if (TopBarThemeLabel != null)
		{
			TopBarThemeLabel.IsVisible = width >= 680.0;
		}

		bool shouldBeWide = width >= WideWorkspaceThresholdDp;
		if (_isWideWorkspace != shouldBeWide)
		{
			_isWideWorkspace = shouldBeWide;
			ApplyResponsiveWorkspaceLayout(shouldBeWide);
		}
	}

	private void ApplyResponsiveWorkspaceLayout(bool isWide)
	{
		if (WorkspaceGrid == null || HeroStageCard == null || ChatStageGrid == null)
			return;

		WorkspaceGrid.BatchBegin();
		try
		{
			WorkspaceGrid.RowDefinitions.Clear();
			WorkspaceGrid.ColumnDefinitions.Clear();

			if (isWide)
			{
				WorkspaceGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(320)));
				WorkspaceGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
				WorkspaceGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
				WorkspaceGrid.ColumnSpacing = 18;
				WorkspaceGrid.RowSpacing = 0;
				WorkspaceGrid.Padding = new Thickness(20, 14, 20, 18);

				Grid.SetRow(HeroStageCard, 0);
				Grid.SetColumn(HeroStageCard, 0);

				Grid.SetRow(ChatStageGrid, 0);
				Grid.SetColumn(ChatStageGrid, 1);

				if (HeroMascotView != null)
				{
					HeroMascotView.WidthRequest = 220;
					HeroMascotView.HeightRequest = 200;
				}
			}
			else
			{
				WorkspaceGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
				WorkspaceGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
				WorkspaceGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
				WorkspaceGrid.ColumnSpacing = 0;
				WorkspaceGrid.RowSpacing = 12;
				WorkspaceGrid.Padding = new Thickness(14, 10, 14, 14);

				Grid.SetRow(HeroStageCard, 0);
				Grid.SetColumn(HeroStageCard, 0);

				Grid.SetRow(ChatStageGrid, 1);
				Grid.SetColumn(ChatStageGrid, 0);

				if (HeroMascotView != null)
				{
					HeroMascotView.WidthRequest = 160;
					HeroMascotView.HeightRequest = 140;
				}
			}
		}
		finally
		{
			WorkspaceGrid.BatchCommit();
		}
	}

	private void OnChatMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.Action == NotifyCollectionChangedAction.Add && ChatMessagesCollectionView != null)
		{
			MainThread.BeginInvokeOnMainThread(async () =>
			{
				await Task.Delay(60).ConfigureAwait(true);
				if (_viewModel.ChatMessages.Count > 0 && ChatMessagesCollectionView != null)
				{
					try
					{
						ChatMessagesCollectionView.ScrollTo(
							_viewModel.ChatMessages.Count - 1,
							position: ScrollToPosition.End,
							animate: true);
					}
					catch
					{
						// Catch transient layout race conditions
					}
				}
			});
		}
		else if (e.Action == NotifyCollectionChangedAction.Remove && ChatMessagesCollectionView != null && _viewModel.ChatMessages.Count == 1)
		{
			MainThread.BeginInvokeOnMainThread(() =>
			{
				try
				{
					ChatMessagesCollectionView.ScrollTo(
						0,
						position: ScrollToPosition.Start,
						animate: true);
				}
				catch
				{
					// Catch transient layout race conditions
				}
			});
		}
		else if (e.Action == NotifyCollectionChangedAction.Reset && ChatMessagesCollectionView != null)
		{
			MainThread.BeginInvokeOnMainThread(() =>
			{
				try
				{
					var items = ChatMessagesCollectionView.ItemsSource;
					ChatMessagesCollectionView.ItemsSource = null;
					ChatMessagesCollectionView.ItemsSource = items;
				}
				catch
				{
					// Catch layout race conditions
				}
			});
		}
	}

	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(MainViewModel.IsOracleThinking) && !_viewModel.IsOracleThinking)
		{
			CleanupDotAnimations();
		}
		else if (e.PropertyName == nameof(MainViewModel.IsDrawerOpen))
		{
			MainThread.BeginInvokeOnMainThread(() =>
			{
				AnimateDrawerState(_viewModel.IsDrawerOpen, instant: false);
			});
		}
	}

	private void AnimateDrawerState(bool isOpen, bool instant)
	{
		if (DrawerOverlayGrid == null || DrawerBackdropBox == null || DrawerSheetBorder == null)
			return;

		this.AbortAnimation(DrawerTransitionAnimName);

		bool canAnimate = !instant && Window != null && IsLoaded;
		if (!canAnimate)
		{
			if (isOpen)
			{
				DrawerOverlayGrid.IsVisible = true;
				DrawerOverlayGrid.InputTransparent = false;
				DrawerBackdropBox.Opacity = DrawerOpenBackdropOpacity;
				DrawerSheetBorder.TranslationX = 0.0;
			}
			else
			{
				DrawerBackdropBox.Opacity = 0.0;
				DrawerSheetBorder.TranslationX = DrawerClosedTranslationX;
				DrawerOverlayGrid.InputTransparent = true;
				DrawerOverlayGrid.IsVisible = false;
			}
			return;
		}

		double startOpacity = DrawerBackdropBox.Opacity;
		double startTranslationX = DrawerSheetBorder.TranslationX;

		if (isOpen)
		{
			DrawerOverlayGrid.IsVisible = true;
			DrawerOverlayGrid.InputTransparent = false;

			var openAnim = new Animation
			{
				{ 0.0, 1.0, new Animation(v => DrawerBackdropBox.Opacity = v, startOpacity, DrawerOpenBackdropOpacity, Easing.CubicOut) },
				{ 0.0, 1.0, new Animation(v => DrawerSheetBorder.TranslationX = v, startTranslationX, 0.0, Easing.CubicOut) }
			};

			openAnim.Commit(
				this,
				DrawerTransitionAnimName,
				rate: 16,
				length: 240,
				easing: Easing.CubicOut,
				finished: (_, _) =>
				{
					if (_viewModel.IsDrawerOpen && DrawerBackdropBox != null && DrawerSheetBorder != null)
					{
						DrawerBackdropBox.Opacity = DrawerOpenBackdropOpacity;
						DrawerSheetBorder.TranslationX = 0.0;
					}
				});
		}
		else
		{
			DrawerOverlayGrid.InputTransparent = true;

			var closeAnim = new Animation
			{
				{ 0.0, 1.0, new Animation(v => DrawerBackdropBox.Opacity = v, startOpacity, 0.0, Easing.CubicOut) },
				{ 0.0, 1.0, new Animation(v => DrawerSheetBorder.TranslationX = v, startTranslationX, DrawerClosedTranslationX, Easing.CubicOut) }
			};

			closeAnim.Commit(
				this,
				DrawerTransitionAnimName,
				rate: 16,
				length: 210,
				easing: Easing.CubicOut,
				finished: (_, _) =>
				{
					if (!_viewModel.IsDrawerOpen && DrawerOverlayGrid != null && DrawerBackdropBox != null && DrawerSheetBorder != null)
					{
						DrawerBackdropBox.Opacity = 0.0;
						DrawerSheetBorder.TranslationX = DrawerClosedTranslationX;
						DrawerOverlayGrid.IsVisible = false;
					}
				});
		}
	}

	private void OnChatBubbleLoaded(object? sender, EventArgs e)
	{
		if (sender is not VisualElement bubble || !bubble.IsVisible)
			return;

		bubble.AbortAnimation(BubbleEntranceAnimName);

		if (bubble.Window == null || !bubble.IsLoaded)
		{
			bubble.Opacity = 1.0;
			bubble.TranslationY = 0.0;
			bubble.Scale = 1.0;
			return;
		}

		_activeBubbles.Add(bubble);
		bubble.Opacity = 0.0;
		bubble.TranslationY = 14.0;
		bubble.Scale = 0.97;

		var entranceAnimation = new Animation
		{
			{ 0.0, 1.0, new Animation(v => bubble.Opacity = v, 0.0, 1.0, Easing.CubicOut) },
			{ 0.0, 1.0, new Animation(v => bubble.TranslationY = v, 14.0, 0.0, Easing.CubicOut) },
			{ 0.0, 1.0, new Animation(v => bubble.Scale = v, 0.97, 1.0, Easing.CubicOut) }
		};

		entranceAnimation.Commit(
			bubble,
			BubbleEntranceAnimName,
			rate: 16,
			length: 240,
			easing: Easing.CubicOut,
			finished: (_, _) =>
			{
				bubble.Opacity = 1.0;
				bubble.TranslationY = 0.0;
				bubble.Scale = 1.0;
			});
	}

	private void OnChatBubbleUnloaded(object? sender, EventArgs e)
	{
		if (sender is not VisualElement bubble)
			return;

		_activeBubbles.Remove(bubble);
		bubble.AbortAnimation(BubbleEntranceAnimName);
		bubble.Opacity = 1.0;
		bubble.TranslationY = 0.0;
		bubble.Scale = 1.0;
	}

	private void CleanupStaleDots()
	{
		if (_activeDotCts.Count == 0) return;

		var staleKeys = new List<VisualElement>();
		foreach (var (dot, _) in _activeDotCts)
		{
			if (!dot.IsLoaded || !dot.IsVisible || dot.Window == null)
			{
				staleKeys.Add(dot);
			}
		}

		foreach (var stale in staleKeys)
		{
			stale.PropertyChanged -= OnDotPropertyChanged;
			CancelAndDisposeDot(stale);
		}
	}

	private void OnTypingIndicatorLoaded(object? sender, EventArgs e)
	{
		if (sender is not VisualElement root || !root.IsVisible)
			return;

		CleanupStaleDots();

		var dots = FindDots(root);
		for (int i = 0; i < dots.Count; i++)
		{
			var dot = dots[i];
			int delayMs = i * 180;

			dot.PropertyChanged -= OnDotPropertyChanged;
			dot.PropertyChanged += OnDotPropertyChanged;

			CancelAndDisposeDot(dot);

			var cts = new CancellationTokenSource();
			_activeDotCts[dot] = cts;
			_ = AnimateDotAsync(dot, delayMs, cts.Token);
		}
	}

	private void OnDotPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (sender is VisualElement dot && (e.PropertyName == nameof(VisualElement.IsVisible) || e.PropertyName == nameof(VisualElement.IsLoaded)))
		{
			if (!dot.IsVisible || !dot.IsLoaded)
			{
				dot.PropertyChanged -= OnDotPropertyChanged;
				CancelAndDisposeDot(dot);
			}
		}
	}

	private void CancelAndDisposeDot(VisualElement dot)
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
		dot.AbortAnimation(TypingPulseAnimName);
		dot.Opacity = 0.3;
	}

	private void OnTypingIndicatorUnloaded(object? sender, EventArgs e)
	{
		if (sender is not VisualElement root)
			return;

		var dots = FindDots(root);
		foreach (var dot in dots)
		{
			dot.PropertyChanged -= OnDotPropertyChanged;
			CancelAndDisposeDot(dot);
		}
	}

	private void CleanupDotAnimations()
	{
		foreach (var (dot, cts) in _activeDotCts)
		{
			dot.PropertyChanged -= OnDotPropertyChanged;
			try
			{
				cts.Cancel();
				cts.Dispose();
			}
			catch
			{
				// Safe cancellation
			}
			dot.AbortAnimation(TypingPulseAnimName);
		}
		_activeDotCts.Clear();
	}

	private void CleanupAllAnimations()
	{
		this.AbortAnimation(DrawerTransitionAnimName);

		CleanupDotAnimations();

		foreach (var bubble in _activeBubbles)
		{
			bubble.AbortAnimation(BubbleEntranceAnimName);
			bubble.Opacity = 1.0;
			bubble.TranslationY = 0.0;
			bubble.Scale = 1.0;
		}
		_activeBubbles.Clear();
	}

	private static async Task AnimateDotAsync(VisualElement dot, int delayMs, CancellationToken token)
	{
		try
		{
			if (delayMs > 0)
			{
				await Task.Delay(delayMs, token).ConfigureAwait(true);
			}

			if (token.IsCancellationRequested || !dot.IsLoaded || !dot.IsVisible)
				return;

			dot.AbortAnimation(TypingPulseAnimName);

			var pulseAnimation = new Animation
			{
				{ 0.0, 0.5, new Animation(v => dot.Opacity = v, 0.25, 1.0, Easing.CubicInOut) },
				{ 0.5, 1.0, new Animation(v => dot.Opacity = v, 1.0, 0.25, Easing.CubicInOut) }
			};

			pulseAnimation.Commit(
				dot,
				TypingPulseAnimName,
				rate: 16,
				length: 900,
				repeat: () => !token.IsCancellationRequested && dot.IsLoaded && dot.IsVisible);
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
