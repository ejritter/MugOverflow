using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Text;
using MugOverflowProject.Controls;
using Xunit;

namespace MugOverflowProject.Tests;

public sealed class CoffeeMascotTests
{
	[Theory]
	[InlineData(-10.0, false, MascotMood.Sleepy)]
	[InlineData(0.0, false, MascotMood.Sleepy)]
	[InlineData(14.9, false, MascotMood.Sleepy)]
	[InlineData(15.0, false, MascotMood.Happy)]
	[InlineData(120.0, false, MascotMood.Happy)]
	[InlineData(200.0, false, MascotMood.Happy)]
	[InlineData(200.1, false, MascotMood.Wired)]
	[InlineData(250.0, false, MascotMood.Wired)]
	[InlineData(0.0, true, MascotMood.Brewing)]
	[InlineData(14.9, true, MascotMood.Brewing)]
	[InlineData(120.0, true, MascotMood.Brewing)]
	[InlineData(250.0, true, MascotMood.Brewing)]
	public void ResolveBaselineMood_AcrossBoundaryValues_ReturnsExpectedMood(
		double activeMg,
		bool isBrewing,
		MascotMood expectedMood)
	{
		MascotMood actual = CoffeeMascotDrawable.ResolveBaselineMood(activeMg, isBrewing);
		Assert.Equal(expectedMood, actual);
	}

	[Fact]
	public void ResolveBaselineMood_WhenNaN_ReturnsSleepyOrBrewingSafely()
	{
		Assert.Equal(MascotMood.Sleepy, CoffeeMascotDrawable.ResolveBaselineMood(double.NaN, false));
		Assert.Equal(MascotMood.Brewing, CoffeeMascotDrawable.ResolveBaselineMood(double.NaN, true));
	}

	[Theory]
	[InlineData(0f, 0f)]
	[InlineData(-25f, 100f)]
	[InlineData(220f, 0f)]
	[InlineData(220f, -10f)]
	[InlineData(float.NaN, 200f)]
	[InlineData(220f, float.NaN)]
	public void Draw_ZeroOrInvalidBounds_ReturnsEarlyWithoutDrawingOrThrowing(float width, float height)
	{
		var drawable = new CoffeeMascotDrawable
		{
			ActiveCaffeineMg = 120.0,
			Mood = MascotMood.Happy,
			AnimationPhase = 1.0f,
			ReactionProgress = 0.5f
		};
		using var canvas = new FakeCanvas();

		Exception? ex = Record.Exception(() => drawable.Draw(canvas, new RectF(0f, 0f, width, height)));

		Assert.Null(ex);
		Assert.Equal(0, canvas.TotalDrawOperations);
		Assert.Equal(0, canvas.StateDepth);
	}

	[Fact]
	public void Draw_NullCanvas_DoesNotThrow()
	{
		var drawable = new CoffeeMascotDrawable();
		Exception? ex = Record.Exception(() => drawable.Draw(null!, new RectF(0f, 0f, 220f, 200f)));
		Assert.Null(ex);
	}

	public static IEnumerable<object[]> AllMoodsAndAvatarModes()
	{
		foreach (MascotMood mood in Enum.GetValues<MascotMood>())
		{
			yield return [mood, false];
			yield return [mood, true];
		}
	}

	[Theory]
	[MemberData(nameof(AllMoodsAndAvatarModes))]
	public void Draw_AllMoods_HeroAndMiniAvatar_RendersCleanlyWithBalancedCanvasState(
		MascotMood mood,
		bool isMiniAvatar)
	{
		var drawable = new CoffeeMascotDrawable
		{
			ActiveCaffeineMg = mood == MascotMood.Wired ? 260.0 : mood == MascotMood.Sleepy ? 4.0 : 125.0,
			Mood = mood,
			AnimationPhase = 1.35f,
			ReactionProgress = 0.5f,
			IsMiniAvatar = isMiniAvatar
		};

		RectF bounds = isMiniAvatar
			? new RectF(0f, 0f, 38f, 38f)
			: new RectF(0f, 0f, 220f, 200f);

		using var fakeCanvas = new FakeCanvas();
		Exception? fakeEx = Record.Exception(() => drawable.Draw(fakeCanvas, bounds));

		Assert.Null(fakeEx);
		Assert.True(fakeCanvas.TotalDrawOperations > 0, $"Expected draw calls for {mood} (Mini={isMiniAvatar}).");
		Assert.True(fakeCanvas.SaveStateCount > 0);
		Assert.Equal(fakeCanvas.SaveStateCount, fakeCanvas.RestoreStateCount);
		Assert.Equal(0, fakeCanvas.StateDepth);

		// Also verify against Microsoft.Maui.Graphics.PictureCanvas
		using var pictureCanvas = new PictureCanvas(0f, 0f, bounds.Width, bounds.Height);
		Exception? picEx = Record.Exception(() => drawable.Draw(pictureCanvas, bounds));
		Assert.Null(picEx);
	}

	[Fact]
	public void Draw_HeroSleepy_DrawsFloatingSleepZs_AndBlinkWindowDoesNotThrow()
	{
		var sleepyDrawable = new CoffeeMascotDrawable
		{
			ActiveCaffeineMg = 0.0,
			Mood = MascotMood.Sleepy,
			AnimationPhase = 0.8f,
			IsMiniAvatar = false
		};

		using var sleepyCanvas = new FakeCanvas();
		sleepyDrawable.Draw(sleepyCanvas, new RectF(0f, 0f, 220f, 200f));

		Assert.Contains("z", sleepyCanvas.DrawnStrings);
		Assert.Contains("Z", sleepyCanvas.DrawnStrings);
		Assert.Equal(0, sleepyCanvas.StateDepth);

		// Exercise periodic eye-blink phase window [4.72..5.04] in Happy & SternNo moods
		var blinkingDrawable = new CoffeeMascotDrawable
		{
			ActiveCaffeineMg = 95.0,
			Mood = MascotMood.Happy,
			AnimationPhase = 4.85f,
			IsMiniAvatar = false
		};

		using var blinkCanvas = new FakeCanvas();
		Exception? ex = Record.Exception(() => blinkingDrawable.Draw(blinkCanvas, new RectF(0f, 0f, 220f, 200f)));
		Assert.Null(ex);
		Assert.Equal(0, blinkCanvas.StateDepth);
	}

	[Theory]
	[InlineData(-50.0, 0f)]
	[InlineData(0.0, 0f)]
	[InlineData(95.0, 0.25f)]
	[InlineData(300.0, 0.75f)]
	[InlineData(1200.0, 1.0f)]
	[InlineData(double.NaN, float.NaN)]
	[InlineData(double.PositiveInfinity, 0.5f)]
	public void Draw_ExtremeCaffeineAndReactionBoundaries_ClampsSafelyWithoutThrowing(
		double caffeineMg,
		float reactionProgress)
	{
		foreach (MascotMood mood in new[] { MascotMood.CelebratingYes, MascotMood.SternNo, MascotMood.SplashSkip, MascotMood.Brewing })
		{
			var drawable = new CoffeeMascotDrawable
			{
				ActiveCaffeineMg = caffeineMg,
				Mood = mood,
				AnimationPhase = 2.4f,
				ReactionProgress = reactionProgress
			};

			using var canvas = new FakeCanvas();
			Exception? ex = Record.Exception(() => drawable.Draw(canvas, new RectF(0f, 0f, 220f, 200f)));
			Assert.Null(ex);
			Assert.Equal(0, canvas.StateDepth);
		}
	}

	[Fact]
	public void CoffeeMascotView_BindableProperties_UpdateDrawableStateSafelyInHeadlessMode()
	{
		var view = new CoffeeMascotView();

		Assert.Same(view.MascotDrawable, view.Drawable);
		Assert.Equal(0.0, view.ActiveCaffeineMg);
		Assert.False(view.IsBrewing);
		Assert.Equal(string.Empty, view.ReactionState);
		Assert.False(view.IsMiniAvatar);
		Assert.True(view.AnimateContinuously);
		Assert.Equal(MascotMood.Sleepy, view.MascotDrawable.Mood);

		// 1. ActiveCaffeineMg updates baseline mood & liquid level
		view.ActiveCaffeineMg = 120.0;
		Assert.Equal(120.0, view.MascotDrawable.ActiveCaffeineMg);
		Assert.Equal(MascotMood.Happy, view.MascotDrawable.Mood);

		view.ActiveCaffeineMg = 245.0;
		Assert.Equal(245.0, view.MascotDrawable.ActiveCaffeineMg);
		Assert.Equal(MascotMood.Wired, view.MascotDrawable.Mood);

		// 2. IsBrewing overrides mood to Brewing and restores baseline when false
		view.IsBrewing = true;
		Assert.Equal(MascotMood.Brewing, view.MascotDrawable.Mood);

		view.IsBrewing = false;
		Assert.Equal(MascotMood.Wired, view.MascotDrawable.Mood);

		// 3. ReactionState transitions ("Yes", "Drank", "No", "Skipped", "Happy", "Brewing", "")
		view.ReactionState = "Yes";
		Assert.Equal(MascotMood.CelebratingYes, view.MascotDrawable.Mood);
		Assert.True(view.MascotDrawable.ReactionProgress > 0f);

		view.ReactionState = "No";
		Assert.Equal(MascotMood.SternNo, view.MascotDrawable.Mood);
		Assert.True(view.MascotDrawable.ReactionProgress > 0f);

		view.ReactionState = "Drank";
		Assert.Equal(MascotMood.CelebratingYes, view.MascotDrawable.Mood);

		view.ReactionState = "Skipped";
		Assert.Equal(MascotMood.SplashSkip, view.MascotDrawable.Mood);

		view.ReactionState = "Happy";
		Assert.Equal(MascotMood.Happy, view.MascotDrawable.Mood);

		view.ReactionState = "Brewing";
		Assert.Equal(MascotMood.Brewing, view.MascotDrawable.Mood);

		view.ReactionState = string.Empty;
		Assert.Equal(MascotMood.Wired, view.MascotDrawable.Mood);
		Assert.Equal(0f, view.MascotDrawable.ReactionProgress);

		// 4. Mini avatar & continuous animation flags
		view.IsMiniAvatar = true;
		view.AnimateContinuously = false;
		Assert.True(view.MascotDrawable.IsMiniAvatar);
		Assert.False(view.AnimateContinuously);
	}

	[Fact]
	public void CoffeeMascotView_StepDisplayedCaffeineTowardsTarget_InterpolatesSmoothlyToTarget()
	{
		var view = new CoffeeMascotView
		{
			ActiveCaffeineMg = 180.0
		};
		view.SetDisplayedCaffeineForTest(0.0);
		Assert.Equal(0.0, view.MascotDrawable.ActiveCaffeineMg);

		// First frame step moves partially toward 180 mg (~25.2 mg) rather than jumping instantly
		view.StepDisplayedCaffeineTowardsTarget();
		Assert.InRange(view.MascotDrawable.ActiveCaffeineMg, 20.0, 30.0);

		// Subsequent 60fps steps converge smoothly to target
		for (int i = 0; i < 65; i++)
		{
			view.StepDisplayedCaffeineTowardsTarget();
		}

		Assert.Equal(180.0, view.MascotDrawable.ActiveCaffeineMg);
	}

	private sealed class FakeCanvas : ICanvas, IDisposable
	{
		public int SaveStateCount { get; private set; }
		public int RestoreStateCount { get; private set; }
		public int StateDepth => SaveStateCount - RestoreStateCount;
		public int TotalDrawOperations { get; private set; }
		public List<string> DrawnStrings { get; } = [];

		public float DisplayScale { get; set; } = 1f;
		public float StrokeSize { get; set; } = 1f;
		public float MiterLimit { get; set; } = 10f;
		public Color StrokeColor { get; set; } = Colors.Black;
		public LineCap StrokeLineCap { get; set; } = LineCap.Butt;
		public LineJoin StrokeLineJoin { get; set; } = LineJoin.Miter;
		public float[]? StrokeDashPattern { get; set; }
		public float StrokeDashOffset { get; set; }
		public Color FillColor { get; set; } = Colors.White;
		public Color FontColor { get; set; } = Colors.Black;
		public IFont Font { get; set; } = Microsoft.Maui.Graphics.Font.Default;
		public float FontSize { get; set; } = 12f;
		public float Alpha { get; set; } = 1f;
		public bool Antialias { get; set; } = true;
		public BlendMode BlendMode { get; set; } = BlendMode.Normal;

		public void DrawPath(PathF path) => TotalDrawOperations++;

		public void FillPath(PathF path, WindingMode windingMode) => TotalDrawOperations++;

		public void SubtractFromClip(float x, float y, float width, float height) { }

		public void ClipPath(PathF path, WindingMode windingMode = WindingMode.NonZero) { }

		public void ClipRectangle(float x, float y, float width, float height) { }

		public void DrawLine(float x1, float y1, float x2, float y2) => TotalDrawOperations++;

		public void DrawArc(float x, float y, float width, float height, float startAngle, float endAngle, bool clockwise, bool closed)
			=> TotalDrawOperations++;

		public void FillArc(float x, float y, float width, float height, float startAngle, float endAngle, bool clockwise)
			=> TotalDrawOperations++;

		public void DrawRectangle(float x, float y, float width, float height) => TotalDrawOperations++;

		public void FillRectangle(float x, float y, float width, float height) => TotalDrawOperations++;

		public void DrawRoundedRectangle(float x, float y, float width, float height, float cornerRadius) => TotalDrawOperations++;

		public void FillRoundedRectangle(float x, float y, float width, float height, float cornerRadius) => TotalDrawOperations++;

		public void DrawEllipse(float x, float y, float width, float height) => TotalDrawOperations++;

		public void FillEllipse(float x, float y, float width, float height) => TotalDrawOperations++;

		public void DrawString(string value, float x, float y, HorizontalAlignment horizontalAlignment)
		{
			TotalDrawOperations++;
			DrawnStrings.Add(value);
		}

		public void DrawString(
			string value,
			float x,
			float y,
			float width,
			float height,
			HorizontalAlignment horizontalAlignment,
			VerticalAlignment verticalAlignment,
			TextFlow textFlow = TextFlow.ClipBounds,
			float lineSpacingAdjustment = 0f)
		{
			TotalDrawOperations++;
			DrawnStrings.Add(value);
		}

		public void DrawText(IAttributedText value, float x, float y, float width, float height) => TotalDrawOperations++;

		public void Rotate(float degrees, float x, float y) { }

		public void Rotate(float degrees) { }

		public void Scale(float sx, float sy) { }

		public void Translate(float tx, float ty) { }

		public void ConcatenateTransform(Matrix3x2 transform) { }

		public void SaveState() => SaveStateCount++;

		public bool RestoreState()
		{
			RestoreStateCount++;
			return RestoreStateCount <= SaveStateCount;
		}

		public void ResetState() { }

		public void SetShadow(SizeF offset, float blur, Color color) { }

		public void SetFillPaint(Paint paint, RectF rectangle) { }

		public void DrawImage(Microsoft.Maui.Graphics.IImage image, float x, float y, float width, float height) => TotalDrawOperations++;

		public SizeF GetStringSize(string value, IFont font, float fontSize) => new(value.Length * fontSize * 0.5f, fontSize);

		public SizeF GetStringSize(
			string value,
			IFont font,
			float fontSize,
			HorizontalAlignment horizontalAlignment,
			VerticalAlignment verticalAlignment) => new(value.Length * fontSize * 0.5f, fontSize);

		public void Dispose() { }
	}
}
