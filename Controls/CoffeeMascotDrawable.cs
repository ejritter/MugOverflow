using System;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace MugOverflowProject.Controls;

public enum MascotMood
{
	Sleepy,
	Happy,
	Wired,
	Brewing,
	CelebratingYes,
	SternNo,
	SplashSkip
}

public sealed class CoffeeMascotDrawable : IDrawable
{
	private const float HeroDesignWidth = 220f;
	private const float HeroDesignHeight = 200f;
	private const float MiniDesignSize = 38f;
	private const float TwoPi = MathF.PI * 2f;

	// Rich coffeehouse fallback palette
	private static readonly Color FallbackPrimary = Color.FromArgb("#D4975A");
	private static readonly Color FallbackSecondary = Color.FromArgb("#5C3818");
	private static readonly Color FallbackAccent = Color.FromArgb("#E8B878");
	private static readonly Color FallbackSurface = Color.FromArgb("#251E17");
	private static readonly Color FallbackPrimaryText = Color.FromArgb("#FBF8F2");
	private static readonly Color FallbackBorder = Color.FromArgb("#5C3818");

	// Fixed artisan ceramic, espresso, crema & expression inks for guaranteed contrast across all 10 themes
	private static readonly Color CeramicLight = Color.FromArgb("#FCF7EF");
	private static readonly Color CeramicMid = Color.FromArgb("#EFE3D1");
	private static readonly Color CeramicInterior = Color.FromArgb("#F4E9D8");
	private static readonly Color EspressoDeep = Color.FromArgb("#2A1509");
	private static readonly Color EspressoMid = Color.FromArgb("#5C3818");
	private static readonly Color EspressoStreamGold = Color.FromArgb("#B87435");
	private static readonly Color CremaFoamLight = Color.FromArgb("#F7E2C1");
	private static readonly Color FaceInk = Color.FromArgb("#22130B");
	private static readonly Color FaceHalo = Color.FromArgb("#FCF5EA");
	private static readonly Color CheekRosy = Color.FromArgb("#E88B78");
	private static readonly Color TongueWarm = Color.FromArgb("#E57368");
	private static readonly Color HydroBlue = Color.FromArgb("#29B6F6");
	private static readonly Color HydroDeep = Color.FromArgb("#0288D1");
	private static readonly Color StopAmberRed = Color.FromArgb("#C62828");
	private static readonly Color SaucerShadowColor = Color.FromArgb("#38000000");

	// Immutable cached canonical geometry (never mutated after construction -> safe for Win2D PlatformPath caching)
	private static readonly PathF HeroSaucerPath = BuildHeroSaucerPath();
	private static readonly PathF HeroMugBodyPath = BuildHeroMugBodyPath();
	private static readonly PathF HeroInnerWindowPath = BuildHeroInnerWindowPath();
	private static readonly PathF HeroHandleOuterPath = BuildHeroHandleOuterPath();
	private static readonly PathF HeroHandleInnerPath = BuildHeroHandleInnerPath();
	private static readonly PathF HeroSheenPath = BuildHeroSheenPath();

	private static readonly PathF MiniMugBodyPath = BuildMiniMugBodyPath();
	private static readonly PathF MiniInnerClipPath = BuildMiniInnerClipPath();
	private static readonly PathF MiniHandlePath = BuildMiniHandlePath();

	private static readonly PathF UnitBeanPath = BuildUnitBeanPath();
	private static readonly PathF UnitBeanCleftPath = BuildUnitBeanCleftPath();
	private static readonly PathF UnitHeartPath = BuildUnitHeartPath();
	private static readonly PathF UnitDropletPath = BuildUnitDropletPath();

	public double ActiveCaffeineMg { get; set; }

	public MascotMood Mood { get; set; } = MascotMood.Sleepy;

	public float AnimationPhase { get; set; }

	public float ReactionProgress { get; set; }

	public bool IsMiniAvatar { get; set; }

	public static MascotMood ResolveBaselineMood(double activeMg, bool isBrewing)
	{
		if (isBrewing)
			return MascotMood.Brewing;

		if (double.IsNaN(activeMg) || activeMg < 15.0)
			return MascotMood.Sleepy;

		if (activeMg <= 200.0)
			return MascotMood.Happy;

		return MascotMood.Wired;
	}

	public void Draw(ICanvas canvas, RectF dirtyRect)
	{
		if (canvas is null ||
		    dirtyRect.Width <= 0f ||
		    dirtyRect.Height <= 0f ||
		    float.IsNaN(dirtyRect.Width) ||
		    float.IsNaN(dirtyRect.Height))
		{
			return;
		}

		canvas.Antialias = true;

		Color primary = GetThemeColor("PrimaryColor", FallbackPrimary);
		Color secondary = GetThemeColor("SecondaryColor", FallbackSecondary);
		Color accent = GetThemeColor("AccentColor", FallbackAccent);
		Color surface = GetThemeColor("SurfaceColor", FallbackSurface);
		Color primaryText = GetThemeColor("PrimaryTextColor", FallbackPrimaryText);
		Color border = GetThemeColor("BorderColor", FallbackBorder);

		if (IsMiniAvatar)
		{
			DrawMiniAvatar(canvas, dirtyRect, primary, accent, border);
		}
		else
		{
			DrawHeroMascot(canvas, dirtyRect, primary, secondary, accent, surface, primaryText, border);
		}
	}

	private void DrawHeroMascot(
		ICanvas canvas,
		RectF dirtyRect,
		Color primary,
		Color secondary,
		Color accent,
		Color surface,
		Color primaryText,
		Color border)
	{
		float scale = MathF.Min(dirtyRect.Width / HeroDesignWidth, dirtyRect.Height / HeroDesignHeight);
		if (scale <= 0f)
			return;

		float originX = dirtyRect.X + (dirtyRect.Width - HeroDesignWidth * scale) * 0.5f;
		float originY = dirtyRect.Y + (dirtyRect.Height - HeroDesignHeight * scale) * 0.5f;

		float phase = float.IsNaN(AnimationPhase) ? 0f : AnimationPhase;
		float reaction = Math.Clamp(float.IsNaN(ReactionProgress) ? 0f : ReactionProgress, 0f, 1f);

		// Gentle 60fps breathing bob + wired horizontal micro-jitter + celebratory hop
		float breathAmp = Mood == MascotMood.Sleepy ? 1.8f : 3.0f;
		float breathBobY = MathF.Sin(phase * 2f) * breathAmp;
		if (Mood == MascotMood.CelebratingYes)
		{
			breathBobY -= MathF.Sin(reaction * MathF.PI) * 5.5f;
		}

		float jitterX = Mood == MascotMood.Wired
			? MathF.Sin(phase * 14f) * 2.5f
			: 0f;

		Color mugOutline = BlendColor(EspressoMid, border, 0.35f);
		Color ceramicTop = BlendColor(CeramicLight, primary, 0.08f);
		Color ceramicBottom = BlendColor(CeramicMid, surface, 0.10f);
		Color cremaGold = BlendColor(CremaFoamLight, accent, 0.40f);

		canvas.SaveState();
		canvas.Translate(originX, originY);
		canvas.Scale(scale, scale);

		// 1. Grounded cafe saucer shadow (breathes subtly against mug bob)
		float shadowExpand = -breathBobY * 0.7f;
		canvas.FillColor = SaucerShadowColor;
		canvas.FillEllipse(36f - shadowExpand, 171f, 136f + shadowExpand * 2f, 15f);

		// 2. Cozy ceramic saucer underneath
		var saucerPaint = new LinearGradientPaint
		{
			StartPoint = new Point(0, 0),
			EndPoint = new Point(0, 1),
			GradientStops =
			[
				new PaintGradientStop(0f, ceramicTop),
				new PaintGradientStop(1f, ceramicBottom)
			]
		};
		canvas.SetFillPaint(saucerPaint, new RectF(34f, 158f, 140f, 19f));
		canvas.FillPath(HeroSaucerPath);

		// Saucer decorative roast pin-stripe & contour
		canvas.StrokeColor = primary.WithAlpha(0.55f);
		canvas.StrokeSize = 1.8f;
		canvas.StrokeLineCap = LineCap.Round;
		canvas.DrawLine(48f, 165.5f, 160f, 165.5f);

		canvas.StrokeColor = mugOutline;
		canvas.StrokeSize = 2.2f;
		canvas.DrawPath(HeroSaucerPath);

		// 3. Background aura / energy lines when Wired
		if (Mood == MascotMood.Wired)
		{
			DrawWiredEnergySparks(canvas, phase, primary, accent, jitterX, breathBobY);
		}

		// 4. Floating Mug Group (bobs & jitters together)
		canvas.SaveState();
		canvas.Translate(jitterX, breathBobY);

		// 4a. Rising curling Bezier steam wisps above the mug
		DrawHeroSteamWisps(canvas, phase, primaryText, accent);

		// 4b. Sturdy C-shaped mug handle on the right
		canvas.FillColor = ceramicBottom;
		canvas.FillPath(HeroHandleOuterPath);

		canvas.StrokeColor = primary.WithAlpha(0.65f);
		canvas.StrokeSize = 3.2f;
		canvas.DrawArc(144f, 89f, 30f, 48f, 290f, 70f, true, false);

		canvas.StrokeColor = mugOutline;
		canvas.StrokeSize = 2.4f;
		canvas.DrawPath(HeroHandleOuterPath);
		canvas.DrawPath(HeroHandleInnerPath);

		// 4c. Rounded coffee mug ceramic body
		var mugPaint = new LinearGradientPaint
		{
			StartPoint = new Point(0.15, 0),
			EndPoint = new Point(0.85, 1),
			GradientStops =
			[
				new PaintGradientStop(0f, ceramicTop),
				new PaintGradientStop(0.7f, CeramicLight),
				new PaintGradientStop(1f, ceramicBottom)
			]
		};
		canvas.SetFillPaint(mugPaint, new RectF(52f, 58f, 104f, 106f));
		canvas.FillPath(HeroMugBodyPath);

		// Subtle cafe rim stripe near top of mug
		canvas.StrokeColor = primary.WithAlpha(0.45f);
		canvas.StrokeSize = 2.5f;
		canvas.DrawLine(56f, 67f, 152f, 67f);

		// 4d. Inner cutaway / sight-window with dynamic espresso fill & sloshing sine-wave crema top
		DrawHeroCutawayLiquid(canvas, phase, primary, secondary, cremaGold);

		// 4e. Top 3D ceramic rim & espresso cup opening
		canvas.FillColor = ceramicTop;
		canvas.FillEllipse(53f, 52f, 102f, 13f);
		canvas.StrokeColor = mugOutline;
		canvas.StrokeSize = 2.2f;
		canvas.DrawEllipse(53f, 52f, 102f, 13f);

		canvas.FillColor = EspressoDeep.WithAlpha(0.90f);
		canvas.FillEllipse(60f, 54.5f, 88f, 8f);
		canvas.FillColor = cremaGold.WithAlpha(0.75f);
		canvas.FillEllipse(66f, 56f, 76f, 5f);

		// If Brewing, draw animated golden-brown espresso stream pouring from top into mug + crema ripples
		if (Mood == MascotMood.Brewing)
		{
			DrawBrewingPourStream(canvas, phase, primary, cremaGold);
		}

		// 4f. Subtle vertical ceramic highlight sheen on left curve
		canvas.FillColor = Colors.White.WithAlpha(0.52f);
		canvas.FillPath(HeroSheenPath);
		canvas.FillEllipse(58.5f, 138f, 5f, 8.5f);

		// 4g. Outer mug contour stroke
		canvas.StrokeColor = mugOutline;
		canvas.StrokeSize = 2.5f;
		canvas.StrokeLineJoin = LineJoin.Round;
		canvas.DrawPath(HeroMugBodyPath);

		// 4h. Expressive Barista Character Face
		DrawHeroFace(canvas, phase, primary, accent);

		// 4i. Mood-specific burst particles & badges (CelebratingYes, SternNo, SplashSkip, Sleepy Zs)
		DrawHeroMoodOverlays(canvas, phase, reaction, primary, accent, primaryText);

		canvas.RestoreState(); // End floating mug group
		canvas.RestoreState(); // End normalized hero stage
	}

	private void DrawHeroCutawayLiquid(
		ICanvas canvas,
		float phase,
		Color primary,
		Color secondary,
		Color cremaGold)
	{
		const float winLeft = 61f;
		const float winTop = 71f;
		const float winW = 86f;
		const float winH = 84f;
		const float winBottom = winTop + winH;

		double safeMg = double.IsNaN(ActiveCaffeineMg) ? 0.0 : ActiveCaffeineMg;
		float fillRatio = (float)Math.Clamp(safeMg / 300.0, 0.08, 0.92);
		float liquidHeight = winH * fillRatio;
		float baseTopY = winBottom - liquidHeight;

		(float waveAmp, float waveFreq) = Mood switch
		{
			MascotMood.Sleepy => (1.3f, 1f),
			MascotMood.Wired => (4.2f, 4f),
			MascotMood.Brewing => (3.5f, 3f),
			MascotMood.CelebratingYes => (3.4f, 3f),
			_ => (2.4f, 2f)
		};

		canvas.SaveState();
		canvas.ClipPath(HeroInnerWindowPath);

		// Warm porcelain sight-window backdrop
		canvas.FillColor = CeramicInterior;
		canvas.FillRectangle(winLeft - 2f, winTop - 2f, winW + 4f, winH + 4f);

		// Build animated sine-wave sloshing espresso liquid path & crema ribbon path
		const int steps = 12;
		float startX = winLeft - 3f;
		float totalSpan = winW + 6f;
		float stepW = totalSpan / steps;

		var liquidPath = new PathF();
		var cremaPath = new PathF();

		liquidPath.MoveTo(startX, winBottom + 4f);

		for (int i = 0; i <= steps; i++)
		{
			float x = startX + i * stepW;
			float norm = (float)i / steps;
			float waveOffset =
				MathF.Sin(phase * waveFreq + norm * MathF.PI * 2.2f) * waveAmp +
				MathF.Cos(phase * waveFreq - norm * MathF.PI * 1.4f) * (waveAmp * 0.35f);
			float y = baseTopY + waveOffset;

			liquidPath.LineTo(x, y);
			if (i == 0)
				cremaPath.MoveTo(x, y);
			else
				cremaPath.LineTo(x, y);
		}

		liquidPath.LineTo(startX + totalSpan, winBottom + 4f);
		liquidPath.Close();

		for (int i = steps; i >= 0; i--)
		{
			float x = startX + i * stepW;
			float norm = (float)i / steps;
			float waveOffset =
				MathF.Sin(phase * waveFreq + norm * MathF.PI * 2.2f) * waveAmp +
				MathF.Cos(phase * waveFreq - norm * MathF.PI * 1.4f) * (waveAmp * 0.35f);
			float y = baseTopY + waveOffset + 5.8f;
			cremaPath.LineTo(x, y);
		}
		cremaPath.Close();

		Color roastTop = BlendColor(EspressoMid, secondary, 0.28f);
		var espressoPaint = new LinearGradientPaint
		{
			StartPoint = new Point(0, 0),
			EndPoint = new Point(0, 1),
			GradientStops =
			[
				new PaintGradientStop(0f, roastTop),
				new PaintGradientStop(1f, EspressoDeep)
			]
		};
		canvas.SetFillPaint(espressoPaint, new RectF(winLeft, baseTopY - 6f, winW, liquidHeight + 10f));
		canvas.FillPath(liquidPath);

		// Velvety golden crema foam crest
		canvas.FillColor = cremaGold;
		canvas.FillPath(cremaPath);

		// Bright steamed-milk shimmer along top of crema wave
		canvas.StrokeColor = Colors.White.WithAlpha(0.55f);
		canvas.StrokeSize = 1.4f;
		canvas.DrawLine(
			winLeft + 10f,
			baseTopY + MathF.Sin(phase * waveFreq + 0.6f) * (waveAmp * 0.6f),
			winLeft + winW - 14f,
			baseTopY + MathF.Sin(phase * waveFreq + 1.8f) * (waveAmp * 0.6f));

		// Tiny effervescent espresso micro-bubbles when Wired or Brewing
		if (Mood is MascotMood.Wired or MascotMood.Brewing && liquidHeight > 18f)
		{
			canvas.FillColor = cremaGold.WithAlpha(0.55f);
			for (int b = 0; b < 4; b++)
			{
				float rise = ((phase / TwoPi * 2f) + b * 0.27f) % 1f;
				float bx = winLeft + 16f + b * 18f + MathF.Sin(phase * 2f + b) * 3f;
				float by = winBottom - 4f - rise * (liquidHeight - 8f);
				canvas.FillCircle(bx, by, 1.8f + (b % 2) * 0.7f);
			}
		}

		canvas.RestoreState();

		// Subtle recessed cutaway frame stroke
		canvas.StrokeColor = BlendColor(EspressoMid, primary, 0.45f).WithAlpha(0.38f);
		canvas.StrokeSize = 1.4f;
		canvas.DrawPath(HeroInnerWindowPath);
	}

	private void DrawHeroSteamWisps(ICanvas canvas, float phase, Color primaryText, Color accent)
	{
		if (Mood == MascotMood.Brewing)
		{
			// During brewing, draw two side steam wisps leaving center clear for the espresso pour stream
			DrawSingleSteamWisp(canvas, 76f, 49f, 26f, phase * 2f, 0.42f, accent);
			DrawSingleSteamWisp(canvas, 132f, 49f, 26f, phase * 2f + 2.1f, 0.42f, accent);
			return;
		}

		(float speedMult, float height, float alpha, float sway) = Mood switch
		{
			MascotMood.Sleepy => (1f, 18f, 0.22f, 3.2f),
			MascotMood.Wired => (4f, 38f, 0.68f, 7.5f),
			MascotMood.CelebratingYes => (3f, 35f, 0.60f, 6.2f),
			_ => (2f, 30f, 0.46f, 5.2f)
		};

		Color steamTint = BlendColor(primaryText, accent, 0.35f);
		float[] baseXs = [82f, 104f, 126f];
		float[] offsets = [0f, 2.09f, 4.18f];
		float[] heights = [height * 0.82f, height, height * 0.86f];

		for (int i = 0; i < 3; i++)
		{
			float wispPhase = phase * speedMult + offsets[i];
			float x = baseXs[i];
			float bottomY = 49f;
			float topY = bottomY - heights[i];
			float c1X = x + MathF.Sin(wispPhase) * sway;
			float c2X = x - MathF.Sin(wispPhase + 0.9f) * sway;
			float tipX = x + MathF.Cos(wispPhase) * (sway * 0.55f);

			var steamPath = new PathF();
			steamPath.MoveTo(x, bottomY);
			steamPath.CurveTo(
				c1X, bottomY - heights[i] * 0.35f,
				c2X, bottomY - heights[i] * 0.70f,
				tipX, topY);

			canvas.StrokeColor = steamTint.WithAlpha(alpha);
			canvas.StrokeSize = Mood == MascotMood.Wired ? 3.0f : 2.6f;
			canvas.StrokeLineCap = LineCap.Round;
			canvas.DrawPath(steamPath);
		}
	}

	private static void DrawSingleSteamWisp(
		ICanvas canvas,
		float x,
		float bottomY,
		float height,
		float wispPhase,
		float alpha,
		Color tint)
	{
		var path = new PathF();
		path.MoveTo(x, bottomY);
		path.CurveTo(
			x + MathF.Sin(wispPhase) * 5f, bottomY - height * 0.35f,
			x - MathF.Sin(wispPhase + 1f) * 5f, bottomY - height * 0.7f,
			x + MathF.Cos(wispPhase) * 3f, bottomY - height);

		canvas.StrokeColor = tint.WithAlpha(alpha);
		canvas.StrokeSize = 2.5f;
		canvas.StrokeLineCap = LineCap.Round;
		canvas.DrawPath(path);
	}

	private static void DrawBrewingPourStream(ICanvas canvas, float phase, Color primary, Color cremaGold)
	{
		float pulse = MathF.Sin(phase * 8f) * 1.1f;
		float streamW = 6.4f + pulse;
		float centerX = 104f;

		// Rich espresso pour column from top portafilter spout into mug rim
		canvas.FillColor = EspressoMid;
		canvas.FillRoundedRectangle(centerX - streamW * 0.5f, 4f, streamW, 55f, streamW * 0.45f);

		// Golden tiger-stripe crema streak inside the pouring shot
		float innerW = MathF.Max(2f, streamW * 0.38f);
		canvas.FillColor = BlendColor(EspressoStreamGold, primary, 0.4f);
		canvas.FillRoundedRectangle(centerX - innerW * 0.35f, 6f, innerW, 52f, innerW * 0.5f);

		// Expanding concentric crema ripples in the cup opening
		for (int r = 0; r < 2; r++)
		{
			float rippleProgress = ((phase / TwoPi * 3f) + r * 0.5f) % 1f;
			float rw = 12f + rippleProgress * 46f;
			float rh = 2.5f + rippleProgress * 5.2f;
			float rAlpha = (1f - rippleProgress) * 0.85f;

			canvas.StrokeColor = cremaGold.WithAlpha(rAlpha);
			canvas.StrokeSize = 1.6f;
			canvas.DrawEllipse(centerX - rw * 0.5f, 58.5f - rh * 0.5f, rw, rh);
		}
	}

	private void DrawHeroFace(ICanvas canvas, float phase, Color primary, Color accent)
	{
		const float leftEyeX = 86f;
		const float rightEyeX = 122f;
		const float eyeY = 103f;
		const float mouthCenterX = 104f;
		const float mouthY = 122f;

		// Periodic natural blink (smooth ~220ms blink once per 2*PI cycle)
		float wrappedPhase = ((phase % TwoPi) + TwoPi) % TwoPi;
		bool isBlinking = Mood is MascotMood.Happy or MascotMood.SternNo &&
		                  wrappedPhase is >= 4.72f and <= 5.04f;

		switch (Mood)
		{
			case MascotMood.Sleepy:
			{
				// Soft muted cheeks
				DrawRosyCheeks(canvas, leftEyeX - 9f, rightEyeX + 9f, eyeY + 11f, 0.32f);

				// Half-closed droopy sleepy eyelids
				DrawHaloedArcEye(canvas, leftEyeX, eyeY + 1f, 7.5f, upwardCurve: false);
				DrawHaloedArcEye(canvas, rightEyeX, eyeY + 1f, 7.5f, upwardCurve: false);

				// Peaceful small 'o' yawn mouth
				canvas.FillColor = FaceHalo.WithAlpha(0.85f);
				canvas.FillEllipse(mouthCenterX - 6.2f, mouthY - 5.2f, 12.4f, 13.4f);
				canvas.FillColor = FaceInk;
				canvas.FillEllipse(mouthCenterX - 4.6f, mouthY - 3.6f, 9.2f, 10.2f);
				canvas.FillColor = TongueWarm.WithAlpha(0.85f);
				canvas.FillEllipse(mouthCenterX - 2.8f, mouthY + 1.5f, 5.6f, 3.8f);
				break;
			}

			case MascotMood.Happy:
			{
				DrawRosyCheeks(canvas, leftEyeX - 10f, rightEyeX + 10f, eyeY + 12f, 0.62f);

				if (isBlinking)
				{
					DrawHaloedArcEye(canvas, leftEyeX, eyeY + 1f, 7f, upwardCurve: false);
					DrawHaloedArcEye(canvas, rightEyeX, eyeY + 1f, 7f, upwardCurve: false);
				}
				else
				{
					DrawSparklingRoastEye(canvas, leftEyeX, eyeY, 6.2f, 7.2f);
					DrawSparklingRoastEye(canvas, rightEyeX, eyeY, 6.2f, 7.2f);
				}

				// Cheerful warm curved smile
				var smile = new PathF();
				smile.MoveTo(mouthCenterX - 10.5f, mouthY - 2f);
				smile.QuadTo(mouthCenterX, mouthY + 9.5f, mouthCenterX + 10.5f, mouthY - 2f);
				DrawHaloedStrokePath(canvas, smile, 3.0f);
				break;
			}

			case MascotMood.Wired:
			{
				DrawRosyCheeks(canvas, leftEyeX - 11f, rightEyeX + 11f, eyeY + 13f, 0.55f);

				// Wide double-ring caffeinated eyes
				DrawWiredDoubleRingEye(canvas, leftEyeX, eyeY, primary);
				DrawWiredDoubleRingEye(canvas, rightEyeX, eyeY, primary);

				// Raised buzzing eyebrows
				DrawHaloedLine(canvas, leftEyeX - 8f, eyeY - 12f, leftEyeX + 7f, eyeY - 15f, 2.5f);
				DrawHaloedLine(canvas, rightEyeX - 7f, eyeY - 15f, rightEyeX + 8f, eyeY - 12f, 2.5f);

				// Zig-zag buzzing grin
				var zigZag = new PathF();
				zigZag.MoveTo(mouthCenterX - 14f, mouthY);
				zigZag.LineTo(mouthCenterX - 8.5f, mouthY + 5f);
				zigZag.LineTo(mouthCenterX - 3f, mouthY - 1.5f);
				zigZag.LineTo(mouthCenterX + 3f, mouthY + 5f);
				zigZag.LineTo(mouthCenterX + 8.5f, mouthY - 1.5f);
				zigZag.LineTo(mouthCenterX + 14f, mouthY + 3.5f);
				DrawHaloedStrokePath(canvas, zigZag, 2.6f);
				break;
			}

			case MascotMood.Brewing:
			{
				DrawRosyCheeks(canvas, leftEyeX - 10f, rightEyeX + 10f, eyeY + 12f, 0.48f);

				// Focused squinted determined barista eyes (>  <)
				var leftSquint = new PathF();
				leftSquint.MoveTo(leftEyeX - 6.5f, eyeY - 4.5f);
				leftSquint.LineTo(leftEyeX + 5.5f, eyeY);
				leftSquint.LineTo(leftEyeX - 6.5f, eyeY + 4.5f);
				DrawHaloedStrokePath(canvas, leftSquint, 2.8f);

				var rightSquint = new PathF();
				rightSquint.MoveTo(rightEyeX + 6.5f, eyeY - 4.5f);
				rightSquint.LineTo(rightEyeX - 5.5f, eyeY);
				rightSquint.LineTo(rightEyeX + 6.5f, eyeY + 4.5f);
				DrawHaloedStrokePath(canvas, rightSquint, 2.8f);

				// Concentrated determined little barista smile
				var focusMouth = new PathF();
				focusMouth.MoveTo(mouthCenterX - 7.5f, mouthY + 1f);
				focusMouth.QuadTo(mouthCenterX, mouthY + 5.5f, mouthCenterX + 7.5f, mouthY - 0.5f);
				DrawHaloedStrokePath(canvas, focusMouth, 2.8f);
				break;
			}

			case MascotMood.CelebratingYes:
			{
				DrawRosyCheeks(canvas, leftEyeX - 10f, rightEyeX + 10f, eyeY + 11f, 0.75f);

				// Joyful curved happy eyes (^ ^)
				DrawHaloedArcEye(canvas, leftEyeX, eyeY + 2f, 7.5f, upwardCurve: true);
				DrawHaloedArcEye(canvas, rightEyeX, eyeY + 2f, 7.5f, upwardCurve: true);

				// Big open celebratory 'D' smile with warm tongue
				var openSmile = new PathF();
				openSmile.MoveTo(mouthCenterX - 12.5f, mouthY - 3f);
				openSmile.LineTo(mouthCenterX + 12.5f, mouthY - 3f);
				openSmile.CurveTo(
					mouthCenterX + 11.5f, mouthY + 11f,
					mouthCenterX - 11.5f, mouthY + 11f,
					mouthCenterX - 12.5f, mouthY - 3f);
				openSmile.Close();

				canvas.StrokeColor = FaceHalo.WithAlpha(0.88f);
				canvas.StrokeSize = 4.6f;
				canvas.DrawPath(openSmile);

				canvas.FillColor = FaceInk;
				canvas.FillPath(openSmile);

				canvas.FillColor = TongueWarm;
				canvas.FillEllipse(mouthCenterX - 6.5f, mouthY + 2.5f, 13f, 6.2f);
				break;
			}

			case MascotMood.SternNo:
			{
				// Attentive eyes + protective raised/angled barista eyebrows
				if (isBlinking)
				{
					DrawHaloedArcEye(canvas, leftEyeX, eyeY + 2f, 6.8f, upwardCurve: false);
					DrawHaloedArcEye(canvas, rightEyeX, eyeY + 2f, 6.8f, upwardCurve: false);
				}
				else
				{
					DrawSparklingRoastEye(canvas, leftEyeX, eyeY + 1.5f, 5.6f, 6.2f);
					DrawSparklingRoastEye(canvas, rightEyeX, eyeY + 1.5f, 5.6f, 6.2f);
				}

				// Protective angled barista brows ("hold up, friend!")
				DrawHaloedLine(canvas, leftEyeX - 8f, eyeY - 11f, leftEyeX + 6.5f, eyeY - 7.5f, 3.0f);
				DrawHaloedLine(canvas, rightEyeX - 6.5f, eyeY - 11.5f, rightEyeX + 8f, eyeY - 9f, 3.0f);

				// Firm 'hold up' mouth
				var firmMouth = new PathF();
				firmMouth.MoveTo(mouthCenterX - 9f, mouthY + 2.5f);
				firmMouth.QuadTo(mouthCenterX, mouthY - 1.5f, mouthCenterX + 9f, mouthY + 2.5f);
				DrawHaloedStrokePath(canvas, firmMouth, 2.9f);
				break;
			}

			case MascotMood.SplashSkip:
			{
				DrawRosyCheeks(canvas, leftEyeX - 10f, rightEyeX + 10f, eyeY + 12f, 0.58f);

				// Refreshed wink on left eye + sparkling right eye
				var wink = new PathF();
				wink.MoveTo(leftEyeX - 6.5f, eyeY - 4f);
				wink.LineTo(leftEyeX + 5.5f, eyeY + 0.5f);
				wink.LineTo(leftEyeX - 6.5f, eyeY + 5f);
				DrawHaloedStrokePath(canvas, wink, 2.9f);

				DrawSparklingRoastEye(canvas, rightEyeX, eyeY, 6.0f, 7.0f);

				// Crisp refreshed grin
				var freshSmile = new PathF();
				freshSmile.MoveTo(mouthCenterX - 10f, mouthY - 1f);
				freshSmile.QuadTo(mouthCenterX + 1f, mouthY + 8.5f, mouthCenterX + 11f, mouthY - 2.5f);
				DrawHaloedStrokePath(canvas, freshSmile, 2.9f);
				break;
			}
		}
	}

	private void DrawHeroMoodOverlays(
		ICanvas canvas,
		float phase,
		float reaction,
		Color primary,
		Color accent,
		Color primaryText)
	{
		switch (Mood)
		{
			case MascotMood.Sleepy:
			{
				// Floating sleepy 'z' / 'Z' letters drifting gently upward
				float drift = MathF.Sin(phase * 2f) * 3f;
				Color zColor = BlendColor(primaryText, accent, 0.45f);

				canvas.FontColor = zColor.WithAlpha(0.72f);
				canvas.FontSize = 12f;
				canvas.DrawString("z", 146f, 36f + drift, 18f, 18f, HorizontalAlignment.Center, VerticalAlignment.Center);

				canvas.FontColor = zColor.WithAlpha(0.90f);
				canvas.FontSize = 16f;
				canvas.DrawString("Z", 160f, 19f - drift * 0.7f, 22f, 22f, HorizontalAlignment.Center, VerticalAlignment.Center);
				break;
			}

			case MascotMood.CelebratingYes:
			{
				// Rising roasted coffee-bean & steam-heart particles driven by ReactionProgress
				float effectiveP = reaction <= 0.001f ? 0.45f : reaction;
				float envelope = MathF.Max(0.28f, MathF.Sin(effectiveP * MathF.PI));
				float lift = effectiveP * 34f;

				// 3 Roasted Coffee Beans rising & rotating
				DrawCoffeeBeanParticle(canvas, 34f - effectiveP * 8f, 88f - lift, -28f + effectiveP * 55f, 0.95f, envelope);
				DrawCoffeeBeanParticle(canvas, 178f + effectiveP * 8f, 82f - lift * 1.1f, 32f - effectiveP * 60f, 0.90f, envelope);
				DrawCoffeeBeanParticle(canvas, 54f - effectiveP * 5f, 44f - lift * 0.65f, 18f + effectiveP * 40f, 0.80f, envelope);

				// 2 Warm Steam Hearts floating upward
				Color heartColor = BlendColor(CheekRosy, accent, 0.25f);
				DrawHeartParticle(canvas, 152f + effectiveP * 6f, 40f - lift * 0.75f, 1.05f, heartColor, envelope);
				DrawHeartParticle(canvas, 44f - effectiveP * 4f, 62f - lift * 0.85f, 0.85f, primary, envelope);
				break;
			}

			case MascotMood.SternNo:
			{
				// Subtle barista 'hold up' stop badge driven by ReactionProgress
				float effectiveP = reaction <= 0.001f ? 0.5f : reaction;
				float popScale = effectiveP < 0.25f
					? 0.5f + (effectiveP / 0.25f) * 0.6f
					: 1.0f + MathF.Sin(effectiveP * MathF.PI) * 0.08f;
				float alpha = effectiveP > 0.88f ? (1f - (effectiveP - 0.88f) / 0.12f) * 0.8f + 0.2f : 0.95f;

				canvas.SaveState();
				canvas.Translate(160f, 52f);
				canvas.Scale(popScale, popScale);

				canvas.FillColor = FaceHalo.WithAlpha(alpha);
				canvas.FillCircle(0f, 0f, 14.5f);

				canvas.FillColor = StopAmberRed.WithAlpha(alpha);
				canvas.FillCircle(0f, 0f, 12f);

				// Crisp horizontal barista stop bar inside badge
				canvas.FillColor = Colors.White.WithAlpha(alpha);
				canvas.FillRoundedRectangle(-6.5f, -2.3f, 13f, 4.6f, 2.2f);

				canvas.RestoreState();
				break;
			}

			case MascotMood.SplashSkip:
			{
				// Cool blue-teal hydro droplets splashing outward driven by ReactionProgress
				float effectiveP = reaction <= 0.001f ? 0.45f : reaction;
				float spread = 6f + effectiveP * 26f;
				float arcY = -MathF.Sin(effectiveP * MathF.PI) * 16f + effectiveP * 8f;
				float alpha = MathF.Max(0.32f, 1f - effectiveP * 0.55f);

				DrawHydroDroplet(canvas, 42f - spread, 74f + arcY, -38f, 1.05f, alpha);
				DrawHydroDroplet(canvas, 36f - spread * 0.75f, 102f + arcY * 0.6f, -62f, 0.85f, alpha);
				DrawHydroDroplet(canvas, 168f + spread, 72f + arcY, 38f, 1.05f, alpha);
				DrawHydroDroplet(canvas, 174f + spread * 0.75f, 100f + arcY * 0.6f, 62f, 0.85f, alpha);
				break;
			}
		}
	}

	private static void DrawWiredEnergySparks(
		ICanvas canvas,
		float phase,
		Color primary,
		Color accent,
		float jitterX,
		float breathBobY)
	{
		Color sparkColor = BlendColor(accent, Colors.Gold, 0.45f);
		float pulse = 0.55f + 0.35f * MathF.Abs(MathF.Sin(phase * 6f));

		canvas.StrokeColor = sparkColor.WithAlpha(pulse);
		canvas.StrokeSize = 2.3f;
		canvas.StrokeLineCap = LineCap.Round;
		canvas.StrokeLineJoin = LineJoin.Round;

		// Left lightning bolt
		var leftBolt = new PathF();
		leftBolt.MoveTo(34f - jitterX * 0.5f, 82f + breathBobY);
		leftBolt.LineTo(24f - jitterX * 0.5f, 94f + breathBobY);
		leftBolt.LineTo(32f - jitterX * 0.5f, 96f + breathBobY);
		leftBolt.LineTo(22f - jitterX * 0.5f, 110f + breathBobY);
		canvas.DrawPath(leftBolt);

		// Right lightning bolt
		var rightBolt = new PathF();
		rightBolt.MoveTo(186f - jitterX * 0.5f, 80f + breathBobY);
		rightBolt.LineTo(196f - jitterX * 0.5f, 92f + breathBobY);
		rightBolt.LineTo(188f - jitterX * 0.5f, 94f + breathBobY);
		rightBolt.LineTo(198f - jitterX * 0.5f, 108f + breathBobY);
		canvas.DrawPath(rightBolt);

		// Vibrating motion lines near mug sides
		canvas.StrokeColor = primary.WithAlpha(0.55f);
		canvas.StrokeSize = 1.8f;
		canvas.DrawArc(39f, 92f, 10f, 34f, 120f, 240f, false, false);
		canvas.DrawArc(176f, 92f, 10f, 34f, 300f, 60f, true, false);
	}

	private void DrawMiniAvatar(
		ICanvas canvas,
		RectF dirtyRect,
		Color primary,
		Color accent,
		Color border)
	{
		float scale = MathF.Min(dirtyRect.Width / MiniDesignSize, dirtyRect.Height / MiniDesignSize);
		if (scale <= 0f)
			return;

		float originX = dirtyRect.X + (dirtyRect.Width - MiniDesignSize * scale) * 0.5f;
		float originY = dirtyRect.Y + (dirtyRect.Height - MiniDesignSize * scale) * 0.5f;

		float phase = float.IsNaN(AnimationPhase) ? 0f : AnimationPhase;
		float bobY = Mood == MascotMood.Brewing ? MathF.Sin(phase * 4f) * 0.9f : 0f;
		Color mugOutline = BlendColor(EspressoMid, border, 0.3f);

		canvas.SaveState();
		canvas.Translate(originX, originY);
		canvas.Scale(scale, scale);

		// Soft circular barista badge ring behind mini mug
		canvas.FillColor = primary.WithAlpha(0.18f);
		canvas.FillCircle(19f, 19f, 18f);
		canvas.StrokeColor = primary.WithAlpha(0.42f);
		canvas.StrokeSize = 1.2f;
		canvas.DrawCircle(19f, 19f, 18f);

		canvas.SaveState();
		canvas.Translate(0f, bobY);

		// Bold C-handle on right
		canvas.StrokeColor = mugOutline;
		canvas.StrokeSize = 3.2f;
		canvas.StrokeLineCap = LineCap.Round;
		canvas.DrawPath(MiniHandlePath);
		canvas.StrokeColor = CeramicLight;
		canvas.StrokeSize = 1.4f;
		canvas.DrawPath(MiniHandlePath);

		// Compact high-contrast ceramic mug body
		canvas.FillColor = CeramicLight;
		canvas.FillPath(MiniMugBodyPath);

		// Subtle espresso band at bottom of mini mug
		canvas.SaveState();
		canvas.ClipPath(MiniInnerClipPath);
		canvas.FillColor = BlendColor(EspressoMid, primary, 0.35f).WithAlpha(0.28f);
		canvas.FillRectangle(6f, 25.5f, 23f, 7f);
		canvas.FillColor = primary.WithAlpha(0.75f);
		canvas.FillRectangle(6f, 9.5f, 23f, 3.2f);
		canvas.RestoreState();

		canvas.StrokeColor = mugOutline;
		canvas.StrokeSize = 1.9f;
		canvas.DrawPath(MiniMugBodyPath);

		// Top steam puff or brewing espresso drop
		if (Mood == MascotMood.Brewing)
		{
			canvas.FillColor = EspressoStreamGold;
			canvas.FillRoundedRectangle(16f, 2.5f, 3f, 8.5f, 1.5f);
		}
		else
		{
			canvas.StrokeColor = accent.WithAlpha(0.80f);
			canvas.StrokeSize = 1.6f;
			canvas.DrawLine(14.5f, 7.2f, 15.2f, 4.0f);
			canvas.DrawLine(20.0f, 7.2f, 20.8f, 3.6f);
		}

		// Crisp adorable mini facial expression matching Mood
		const float lx = 13.0f;
		const float rx = 21.4f;
		const float ey = 18.8f;
		const float mx = 17.2f;
		const float my = 24.4f;

		switch (Mood)
		{
			case MascotMood.Sleepy:
				canvas.StrokeColor = FaceInk;
				canvas.StrokeSize = 1.8f;
				canvas.DrawLine(lx - 2.2f, ey, lx + 2.2f, ey);
				canvas.DrawLine(rx - 2.2f, ey, rx + 2.2f, ey);
				canvas.FillColor = FaceInk;
				canvas.FillCircle(mx, my, 1.8f);
				break;

			case MascotMood.Wired:
				canvas.FillColor = Colors.White;
				canvas.FillCircle(lx, ey, 2.8f);
				canvas.FillCircle(rx, ey, 2.8f);
				canvas.StrokeColor = FaceInk;
				canvas.StrokeSize = 1.3f;
				canvas.DrawCircle(lx, ey, 2.8f);
				canvas.DrawCircle(rx, ey, 2.8f);
				canvas.FillColor = FaceInk;
				canvas.FillCircle(lx, ey, 1.2f);
				canvas.FillCircle(rx, ey, 1.2f);
				canvas.DrawLine(mx - 3.5f, my, mx + 3.5f, my);
				break;

			case MascotMood.Brewing:
				canvas.StrokeColor = FaceInk;
				canvas.StrokeSize = 1.8f;
				canvas.DrawLine(lx - 2f, ey - 1.5f, lx + 1.8f, ey);
				canvas.DrawLine(lx + 1.8f, ey, lx - 2f, ey + 1.5f);
				canvas.DrawLine(rx + 2f, ey - 1.5f, rx - 1.8f, ey);
				canvas.DrawLine(rx - 1.8f, ey, rx + 2f, ey + 1.5f);
				canvas.DrawLine(mx - 2.4f, my + 0.5f, mx + 2.4f, my + 0.5f);
				break;

			case MascotMood.CelebratingYes:
				canvas.FillColor = CheekRosy.WithAlpha(0.65f);
				canvas.FillCircle(lx - 2.2f, ey + 3.2f, 2.0f);
				canvas.FillCircle(rx + 2.2f, ey + 3.2f, 2.0f);
				DrawMiniHappyArc(canvas, lx, ey + 0.6f);
				DrawMiniHappyArc(canvas, rx, ey + 0.6f);
				canvas.FillColor = FaceInk;
				canvas.FillArc(mx - 3.6f, my - 2.5f, 7.2f, 6.2f, 180f, 360f, true);
				break;

			case MascotMood.SternNo:
				canvas.FillColor = FaceInk;
				canvas.FillCircle(lx, ey + 0.5f, 1.8f);
				canvas.FillCircle(rx, ey + 0.5f, 1.8f);
				canvas.StrokeColor = FaceInk;
				canvas.StrokeSize = 1.7f;
				canvas.DrawLine(lx - 2.4f, ey - 2.8f, lx + 2.0f, ey - 1.5f);
				canvas.DrawLine(rx - 2.0f, ey - 1.5f, rx + 2.4f, ey - 2.8f);
				canvas.DrawLine(mx - 2.8f, my + 0.5f, mx + 2.8f, my + 0.5f);
				canvas.FillColor = StopAmberRed;
				canvas.FillCircle(30.5f, 9.5f, 3.8f);
				canvas.StrokeColor = Colors.White;
				canvas.StrokeSize = 1.5f;
				canvas.DrawLine(28.7f, 9.5f, 32.3f, 9.5f);
				break;

			case MascotMood.SplashSkip:
				canvas.StrokeColor = FaceInk;
				canvas.StrokeSize = 1.8f;
				canvas.DrawLine(lx - 2f, ey - 1.4f, lx + 1.8f, ey);
				canvas.DrawLine(lx + 1.8f, ey, lx - 2f, ey + 1.4f);
				canvas.FillColor = FaceInk;
				canvas.FillCircle(rx, ey, 1.9f);
				DrawMiniSmile(canvas, mx, my);
				canvas.FillColor = HydroBlue;
				canvas.FillCircle(31.2f, 10.0f, 2.8f);
				break;

			default: // Happy
				canvas.FillColor = CheekRosy.WithAlpha(0.6f);
				canvas.FillCircle(lx - 2.2f, ey + 3.2f, 1.9f);
				canvas.FillCircle(rx + 2.2f, ey + 3.2f, 1.9f);
				canvas.FillColor = FaceInk;
				canvas.FillCircle(lx, ey, 2.0f);
				canvas.FillCircle(rx, ey, 2.0f);
				canvas.FillColor = Colors.White;
				canvas.FillCircle(lx - 0.6f, ey - 0.7f, 0.7f);
				canvas.FillCircle(rx - 0.6f, ey - 0.7f, 0.7f);
				DrawMiniSmile(canvas, mx, my);
				break;
		}

		canvas.RestoreState();
		canvas.RestoreState();
	}

	private static void DrawMiniHappyArc(ICanvas canvas, float cx, float cy)
	{
		var p = new PathF();
		p.MoveTo(cx - 2.4f, cy + 1.0f);
		p.QuadTo(cx, cy - 2.2f, cx + 2.4f, cy + 1.0f);
		canvas.StrokeColor = FaceInk;
		canvas.StrokeSize = 1.8f;
		canvas.StrokeLineCap = LineCap.Round;
		canvas.DrawPath(p);
	}

	private static void DrawMiniSmile(ICanvas canvas, float mx, float my)
	{
		var p = new PathF();
		p.MoveTo(mx - 3.2f, my - 0.5f);
		p.QuadTo(mx, my + 2.8f, mx + 3.2f, my - 0.5f);
		canvas.StrokeColor = FaceInk;
		canvas.StrokeSize = 1.8f;
		canvas.StrokeLineCap = LineCap.Round;
		canvas.DrawPath(p);
	}

	private static void DrawSparklingRoastEye(ICanvas canvas, float cx, float cy, float rx, float ry)
	{
		// Crisp warm-cream halo so eye stays razor-sharp against high espresso levels
		canvas.FillColor = FaceHalo.WithAlpha(0.90f);
		canvas.FillEllipse(cx - rx - 1.6f, cy - ry - 1.6f, (rx + 1.6f) * 2f, (ry + 1.6f) * 2f);

		canvas.FillColor = FaceInk;
		canvas.FillEllipse(cx - rx, cy - ry, rx * 2f, ry * 2f);

		// Main & secondary white catchlights
		canvas.FillColor = Colors.White;
		canvas.FillCircle(cx - rx * 0.32f, cy - ry * 0.34f, rx * 0.36f);
		canvas.FillCircle(cx + rx * 0.30f, cy + ry * 0.28f, rx * 0.18f);
	}

	private static void DrawWiredDoubleRingEye(ICanvas canvas, float cx, float cy, Color primary)
	{
		// Wide bright sclera
		canvas.FillColor = Colors.White;
		canvas.FillCircle(cx, cy, 9.2f);
		canvas.StrokeColor = FaceInk;
		canvas.StrokeSize = 2.1f;
		canvas.DrawCircle(cx, cy, 9.2f);

		// Outer caffeinated amber/roast iris ring
		canvas.StrokeColor = BlendColor(EspressoStreamGold, primary, 0.5f);
		canvas.StrokeSize = 2.0f;
		canvas.DrawCircle(cx, cy, 5.4f);

		// Tight hyper-focused espresso pupil + twin catchlights
		canvas.FillColor = FaceInk;
		canvas.FillCircle(cx, cy, 3.0f);
		canvas.FillColor = Colors.White;
		canvas.FillCircle(cx - 1.4f, cy - 1.4f, 1.2f);
		canvas.FillCircle(cx + 1.5f, cy + 1.2f, 0.8f);
	}

	private static void DrawHaloedArcEye(ICanvas canvas, float cx, float cy, float halfWidth, bool upwardCurve)
	{
		float ctrlOffset = upwardCurve ? -7.2f : 6.2f;
		var path = new PathF();
		path.MoveTo(cx - halfWidth, cy);
		path.QuadTo(cx, cy + ctrlOffset, cx + halfWidth, cy);
		DrawHaloedStrokePath(canvas, path, 2.9f);
	}

	private static void DrawHaloedStrokePath(ICanvas canvas, PathF path, float strokeWidth)
	{
		canvas.StrokeLineCap = LineCap.Round;
		canvas.StrokeLineJoin = LineJoin.Round;

		canvas.StrokeColor = FaceHalo.WithAlpha(0.84f);
		canvas.StrokeSize = strokeWidth + 2.8f;
		canvas.DrawPath(path);

		canvas.StrokeColor = FaceInk;
		canvas.StrokeSize = strokeWidth;
		canvas.DrawPath(path);
	}

	private static void DrawHaloedLine(ICanvas canvas, float x1, float y1, float x2, float y2, float strokeWidth)
	{
		canvas.StrokeLineCap = LineCap.Round;
		canvas.StrokeColor = FaceHalo.WithAlpha(0.84f);
		canvas.StrokeSize = strokeWidth + 2.6f;
		canvas.DrawLine(x1, y1, x2, y2);

		canvas.StrokeColor = FaceInk;
		canvas.StrokeSize = strokeWidth;
		canvas.DrawLine(x1, y1, x2, y2);
	}

	private static void DrawRosyCheeks(ICanvas canvas, float leftX, float rightX, float y, float alpha)
	{
		canvas.FillColor = CheekRosy.WithAlpha(alpha);
		canvas.FillEllipse(leftX - 6.5f, y - 3.8f, 13f, 7.6f);
		canvas.FillEllipse(rightX - 6.5f, y - 3.8f, 13f, 7.6f);
	}

	private static void DrawCoffeeBeanParticle(
		ICanvas canvas,
		float x,
		float y,
		float angleDeg,
		float scale,
		float alpha)
	{
		canvas.SaveState();
		canvas.Translate(x, y);
		canvas.Rotate(angleDeg);
		canvas.Scale(scale, scale);

		canvas.FillColor = EspressoMid.WithAlpha(alpha);
		canvas.FillPath(UnitBeanPath);
		canvas.StrokeColor = EspressoDeep.WithAlpha(alpha);
		canvas.StrokeSize = 1.3f;
		canvas.DrawPath(UnitBeanPath);

		canvas.StrokeColor = CremaFoamLight.WithAlpha(alpha * 0.85f);
		canvas.StrokeSize = 1.2f;
		canvas.StrokeLineCap = LineCap.Round;
		canvas.DrawPath(UnitBeanCleftPath);

		canvas.RestoreState();
	}

	private static void DrawHeartParticle(
		ICanvas canvas,
		float x,
		float y,
		float scale,
		Color color,
		float alpha)
	{
		canvas.SaveState();
		canvas.Translate(x, y);
		canvas.Scale(scale, scale);

		canvas.FillColor = color.WithAlpha(alpha);
		canvas.FillPath(UnitHeartPath);

		canvas.RestoreState();
	}

	private static void DrawHydroDroplet(
		ICanvas canvas,
		float x,
		float y,
		float angleDeg,
		float scale,
		float alpha)
	{
		canvas.SaveState();
		canvas.Translate(x, y);
		canvas.Rotate(angleDeg);
		canvas.Scale(scale, scale);

		canvas.FillColor = HydroBlue.WithAlpha(alpha);
		canvas.FillPath(UnitDropletPath);
		canvas.StrokeColor = HydroDeep.WithAlpha(alpha);
		canvas.StrokeSize = 1.2f;
		canvas.DrawPath(UnitDropletPath);

		canvas.FillColor = Colors.White.WithAlpha(alpha * 0.85f);
		canvas.FillCircle(-1.5f, 1.5f, 1.3f);

		canvas.RestoreState();
	}

	private static Color GetThemeColor(string key, Color fallback)
	{
		try
		{
			if (Application.Current?.Resources is { } resources &&
			    resources.TryGetValue(key, out var val))
			{
				if (val is Color color)
					return color;
				if (val is SolidColorBrush { Color: { } brushColor })
					return brushColor;
			}
		}
		catch
		{
			// Safe headless fallback for unit test runners without an active Application
		}

		return fallback;
	}

	private static Color BlendColor(Color a, Color b, float t)
	{
		t = Math.Clamp(t, 0f, 1f);
		return new Color(
			a.Red + (b.Red - a.Red) * t,
			a.Green + (b.Green - a.Green) * t,
			a.Blue + (b.Blue - a.Blue) * t,
			a.Alpha + (b.Alpha - a.Alpha) * t);
	}

	private static PathF BuildHeroSaucerPath()
	{
		var p = new PathF();
		p.MoveTo(35f, 162f);
		p.LineTo(173f, 162f);
		p.CurveTo(170f, 174f, 142f, 177f, 104f, 177f);
		p.CurveTo(66f, 177f, 38f, 174f, 35f, 162f);
		p.Close();
		return p;
	}

	private static PathF BuildHeroMugBodyPath()
	{
		var p = new PathF();
		p.AppendRoundedRectangle(52f, 58f, 104f, 106f, 14f, 14f, 36f, 36f);
		return p;
	}

	private static PathF BuildHeroInnerWindowPath()
	{
		var p = new PathF();
		p.AppendRoundedRectangle(61f, 71f, 86f, 84f, 10f, 10f, 28f, 28f);
		return p;
	}

	private static PathF BuildHeroHandleOuterPath()
	{
		var p = new PathF();
		p.MoveTo(153f, 80f);
		p.CurveTo(189f, 78f, 190f, 146f, 151f, 144f);
		p.LineTo(151f, 132f);
		p.CurveTo(175f, 133f, 175f, 92f, 153f, 92f);
		p.Close();
		return p;
	}

	private static PathF BuildHeroHandleInnerPath()
	{
		var p = new PathF();
		p.MoveTo(154f, 92f);
		p.CurveTo(175f, 92f, 175f, 133f, 152f, 132f);
		return p;
	}

	private static PathF BuildHeroSheenPath()
	{
		var p = new PathF();
		p.AppendRoundedRectangle(57.5f, 74f, 5.5f, 58f, 2.75f);
		return p;
	}

	private static PathF BuildMiniMugBodyPath()
	{
		var p = new PathF();
		p.AppendRoundedRectangle(6.5f, 9.5f, 21.5f, 21.5f, 3.5f, 3.5f, 8.5f, 8.5f);
		return p;
	}

	private static PathF BuildMiniInnerClipPath()
	{
		var p = new PathF();
		p.AppendRoundedRectangle(7.2f, 10.2f, 20.1f, 20.1f, 3.0f, 3.0f, 7.8f, 7.8f);
		return p;
	}

	private static PathF BuildMiniHandlePath()
	{
		var p = new PathF();
		p.MoveTo(27.5f, 14.0f);
		p.CurveTo(33.5f, 14.0f, 33.5f, 25.5f, 27.2f, 25.5f);
		return p;
	}

	private static PathF BuildUnitBeanPath()
	{
		var p = new PathF();
		p.AppendEllipse(-5.5f, -7.5f, 11f, 15f);
		return p;
	}

	private static PathF BuildUnitBeanCleftPath()
	{
		var p = new PathF();
		p.MoveTo(-0.5f, -6.2f);
		p.CurveTo(2.4f, -2.0f, -2.4f, 2.0f, 0.5f, 6.2f);
		return p;
	}

	private static PathF BuildUnitHeartPath()
	{
		var p = new PathF();
		p.MoveTo(0f, 6.5f);
		p.CurveTo(-9f, 0.5f, -8f, -7.5f, 0f, -3.2f);
		p.CurveTo(8f, -7.5f, 9f, 0.5f, 0f, 6.5f);
		p.Close();
		return p;
	}

	private static PathF BuildUnitDropletPath()
	{
		var p = new PathF();
		p.MoveTo(0f, -7.5f);
		p.CurveTo(5.2f, -1.0f, 5.2f, 5.8f, 0f, 5.8f);
		p.CurveTo(-5.2f, 5.8f, -5.2f, -1.0f, 0f, -7.5f);
		p.Close();
		return p;
	}
}
