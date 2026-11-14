using System;
using Microsoft.Maui;
using Microsoft.Maui.Controls;

namespace MugOverflowProject.Controls;

public sealed class CoffeeMascotView : GraphicsView
{
	public const string LoopAnimationName = "MascotLoop";
	public const string ReactionAnimationName = "MascotReaction";

	private const uint LoopDurationMs = 4000;
	private const uint ReactionDurationMs = 1400;
	private const uint FrameRateMs = 16;

	private readonly CoffeeMascotDrawable _drawable = new();
	private double _displayedCaffeineMg;
	private MascotMood? _activeReactionMood;
	private float _reactionProgress;
	private bool _isLoopRunning;
	private int _reactionToken;

	public static readonly BindableProperty ActiveCaffeineMgProperty =
		BindableProperty.Create(
			nameof(ActiveCaffeineMg),
			typeof(double),
			typeof(CoffeeMascotView),
			0.0,
			propertyChanged: OnActiveCaffeineMgChanged);

	public static readonly BindableProperty IsBrewingProperty =
		BindableProperty.Create(
			nameof(IsBrewing),
			typeof(bool),
			typeof(CoffeeMascotView),
			false,
			propertyChanged: OnIsBrewingChanged);

	public static readonly BindableProperty ReactionStateProperty =
		BindableProperty.Create(
			nameof(ReactionState),
			typeof(string),
			typeof(CoffeeMascotView),
			string.Empty,
			propertyChanged: OnReactionStateChanged);

	public static readonly BindableProperty IsMiniAvatarProperty =
		BindableProperty.Create(
			nameof(IsMiniAvatar),
			typeof(bool),
			typeof(CoffeeMascotView),
			false,
			propertyChanged: OnVisualPropertyChanged);

	public static readonly BindableProperty AnimateContinuouslyProperty =
		BindableProperty.Create(
			nameof(AnimateContinuously),
			typeof(bool),
			typeof(CoffeeMascotView),
			true,
			propertyChanged: OnAnimateContinuouslyChanged);

	public CoffeeMascotDrawable MascotDrawable => _drawable;

	public double ActiveCaffeineMg
	{
		get => (double)GetValue(ActiveCaffeineMgProperty);
		set => SetValue(ActiveCaffeineMgProperty, value);
	}

	public bool IsBrewing
	{
		get => (bool)GetValue(IsBrewingProperty);
		set => SetValue(IsBrewingProperty, value);
	}

	public string ReactionState
	{
		get => (string)GetValue(ReactionStateProperty);
		set => SetValue(ReactionStateProperty, value);
	}

	public bool IsMiniAvatar
	{
		get => (bool)GetValue(IsMiniAvatarProperty);
		set => SetValue(IsMiniAvatarProperty, value);
	}

	public bool AnimateContinuously
	{
		get => (bool)GetValue(AnimateContinuouslyProperty);
		set => SetValue(AnimateContinuouslyProperty, value);
	}

	public CoffeeMascotView()
	{
		Drawable = _drawable;
		Loaded += OnViewLoaded;
		Unloaded += OnViewUnloaded;
		SyncDrawableAndInvalidate();
	}

	private static void OnActiveCaffeineMgChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is CoffeeMascotView view)
		{
			if (!view._isLoopRunning)
			{
				view._displayedCaffeineMg = SanitizeCaffeine((double)newValue);
			}

			view.SyncDrawableAndInvalidate();
		}
	}

	private static void OnIsBrewingChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is CoffeeMascotView view)
		{
			if (newValue is true)
			{
				// Actively brewing supersedes any prior burst reaction
				++view._reactionToken;
				view.SafeAbortAnimation(ReactionAnimationName);
				view._activeReactionMood = null;
				view._reactionProgress = 0f;
			}
			else if (view._activeReactionMood == MascotMood.Brewing)
			{
				// Ensure cancelling IsBrewing returns cleanly to baseline even if ReactionState remained "Brewing"
				view._activeReactionMood = null;
			}

			view.UpdateLoopAnimationState();
			view.SyncDrawableAndInvalidate();
		}
	}

	private static void OnReactionStateChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is CoffeeMascotView view)
		{
			view.ApplyReactionState(newValue as string ?? string.Empty);
		}
	}

	private static void OnVisualPropertyChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is CoffeeMascotView view)
		{
			view.SyncDrawableAndInvalidate();
		}
	}

	private static void OnAnimateContinuouslyChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is CoffeeMascotView view)
		{
			view.UpdateLoopAnimationState();
			view.SyncDrawableAndInvalidate();
		}
	}

	private void OnViewLoaded(object? sender, EventArgs e)
	{
		_displayedCaffeineMg = SanitizeCaffeine(ActiveCaffeineMg);
		UpdateLoopAnimationState();
		SyncDrawableAndInvalidate();
	}

	private void OnViewUnloaded(object? sender, EventArgs e)
	{
		_isLoopRunning = false;
		++_reactionToken;

		SafeAbortAnimation(LoopAnimationName);
		SafeAbortAnimation(ReactionAnimationName);

		if (AnimateContinuously)
		{
			// Clear transient burst state when navigating away so re-appearing returns cleanly to baseline
			_activeReactionMood = null;
			_reactionProgress = 0f;
		}
	}

	private void ApplyReactionState(string state)
	{
		string normalized = state.Trim();
		int token = ++_reactionToken;
		SafeAbortAnimation(ReactionAnimationName);

		bool isBurstReaction = false;

		if (string.Equals(normalized, "Yes", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(normalized, "Drank", StringComparison.OrdinalIgnoreCase))
		{
			_activeReactionMood = MascotMood.CelebratingYes;
			isBurstReaction = true;
		}
		else if (string.Equals(normalized, "No", StringComparison.OrdinalIgnoreCase))
		{
			_activeReactionMood = MascotMood.SternNo;
			isBurstReaction = true;
		}
		else if (string.Equals(normalized, "Skipped", StringComparison.OrdinalIgnoreCase))
		{
			_activeReactionMood = MascotMood.SplashSkip;
			isBurstReaction = true;
		}
		else if (string.Equals(normalized, "Happy", StringComparison.OrdinalIgnoreCase))
		{
			_activeReactionMood = MascotMood.Happy;
			_reactionProgress = 0f;
		}
		else if (string.Equals(normalized, "Brewing", StringComparison.OrdinalIgnoreCase))
		{
			_activeReactionMood = MascotMood.Brewing;
			_reactionProgress = 0f;
		}
		else
		{
			_activeReactionMood = null;
			_reactionProgress = 0f;
		}

		if (isBurstReaction)
		{
			if (CanCommitAnimation() && AnimateContinuously)
			{
				_reactionProgress = 0f;
				SyncDrawableAndInvalidate();

				try
				{
					var reactionAnim = new Animation(v =>
					{
						if (token != _reactionToken)
							return;

						_reactionProgress = (float)v;
						SyncDrawableAndInvalidate();
					}, 0.0, 1.0, Easing.CubicOut);

					reactionAnim.Commit(
						this,
						ReactionAnimationName,
						rate: FrameRateMs,
						length: ReactionDurationMs,
						easing: Easing.Linear,
						finished: (_, _) =>
						{
							if (token != _reactionToken)
								return;

							_activeReactionMood = null;
							_reactionProgress = 0f;
							SyncDrawableAndInvalidate();
						},
						repeat: () => false);
				}
				catch
				{
					// Headless / detached fallback
					_reactionProgress = 0.5f;
					SyncDrawableAndInvalidate();
				}
			}
			else
			{
				// Static mini-avatar or headless test mode: display expressive reaction frame without spawning timers
				_reactionProgress = 0.5f;
				SyncDrawableAndInvalidate();
			}
		}
		else
		{
			SyncDrawableAndInvalidate();
		}
	}

	private void UpdateLoopAnimationState()
	{
		bool shouldRun = CanCommitAnimation() && (AnimateContinuously || IsBrewing);

		if (shouldRun)
		{
			if (_isLoopRunning)
				return;

			SafeAbortAnimation(LoopAnimationName);
			_isLoopRunning = true;

			try
			{
				var loopAnim = new Animation(v =>
				{
					_drawable.AnimationPhase = (float)(v * Math.PI * 2.0);
					StepDisplayedCaffeineTowardsTarget();
					SyncDrawableProperties();
					SafeInvalidate();
				}, 0.0, 1.0, Easing.Linear);

				loopAnim.Commit(
					this,
					LoopAnimationName,
					rate: FrameRateMs,
					length: LoopDurationMs,
					easing: Easing.Linear,
					finished: (_, _) =>
					{
						if (!IsLoaded || !(AnimateContinuously || IsBrewing))
						{
							_isLoopRunning = false;
						}
					},
					repeat: () => IsLoaded && (AnimateContinuously || IsBrewing));
			}
			catch
			{
				_isLoopRunning = false;
			}
		}
		else
		{
			if (_isLoopRunning)
			{
				_isLoopRunning = false;
				SafeAbortAnimation(LoopAnimationName);
			}
		}
	}

	internal void SetDisplayedCaffeineForTest(double mg)
	{
		_displayedCaffeineMg = SanitizeCaffeine(mg);
		SyncDrawableProperties();
	}

	internal void StepDisplayedCaffeineTowardsTarget()
	{
		double target = SanitizeCaffeine(ActiveCaffeineMg);
		double delta = target - _displayedCaffeineMg;

		if (Math.Abs(delta) <= 0.15)
		{
			_displayedCaffeineMg = target;
		}
		else
		{
			_displayedCaffeineMg += delta * 0.14;
		}

		SyncDrawableProperties();
	}

	private void SyncDrawableAndInvalidate()
	{
		if (!_isLoopRunning)
		{
			_displayedCaffeineMg = SanitizeCaffeine(ActiveCaffeineMg);
		}

		SyncDrawableProperties();
		SafeInvalidate();
	}

	private void SyncDrawableProperties()
	{
		double targetMg = SanitizeCaffeine(ActiveCaffeineMg);
		_drawable.ActiveCaffeineMg = _displayedCaffeineMg;
		_drawable.IsMiniAvatar = IsMiniAvatar;
		_drawable.ReactionProgress = _reactionProgress;

		if (IsBrewing)
		{
			_drawable.Mood = MascotMood.Brewing;
		}
		else if (_activeReactionMood.HasValue)
		{
			_drawable.Mood = _activeReactionMood.Value;
		}
		else
		{
			_drawable.Mood = CoffeeMascotDrawable.ResolveBaselineMood(targetMg, false);
		}
	}

	private bool CanCommitAnimation()
	{
		try
		{
			return IsLoaded && Handler is not null && Dispatcher is not null;
		}
		catch
		{
			return false;
		}
	}

	private void SafeAbortAnimation(string animationName)
	{
		try
		{
			this.AbortAnimation(animationName);
		}
		catch
		{
			// Safe when no MAUI animation ticker/dispatcher is attached in headless unit tests
		}
	}

	private void SafeInvalidate()
	{
		try
		{
			Invalidate();
		}
		catch
		{
			// Safe in headless unit tests
		}
	}

	private static double SanitizeCaffeine(double mg)
	{
		if (double.IsNaN(mg) || double.IsNegativeInfinity(mg) || mg < 0.0)
			return 0.0;
		if (double.IsPositiveInfinity(mg))
			return 300.0;
		return mg;
	}
}
