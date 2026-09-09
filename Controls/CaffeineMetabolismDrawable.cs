using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace MugOverflowProject.Controls;

public sealed class CaffeineMetabolismDrawable : IDrawable
{
	private const double TotalHours = 24.0;
	private const double HalfLifeHours = 5.0;
	private const double SleepThresholdMg = 20.0;

	private static readonly float[] SleepDashPattern = [4f, 4f];
	private static readonly Color GridStrokeColor = Color.FromArgb("#338E918F");
	private static readonly Color GridFontColor = Color.FromArgb("#8E918F");
	private static readonly Color SleepColor = Color.FromArgb("#2E7D32");
	private static readonly Color FallbackCurveColor = Color.FromArgb("#7E57C2");
	private static readonly Color FallbackPointFillColor = Color.FromArgb("#C89B3C");

	public double InitialActiveMg { get; set; } = 0.0;

	public void Draw(ICanvas canvas, RectF dirtyRect)
	{
		float width = dirtyRect.Width;
		float height = dirtyRect.Height;

		if (width <= 0 || height <= 0)
			return;

		float padLeft = 50f;
		float padRight = 20f;
		float padTop = 24f;
		float padBottom = 32f;

		float graphWidth = width - padLeft - padRight;
		float graphHeight = height - padTop - padBottom;

		if (graphWidth <= 0 || graphHeight <= 0)
			return;

		double maxY = Math.Max(200.0, Math.Ceiling((InitialActiveMg * 1.25) / 50.0) * 50.0);

		// 1. Draw Grid Lines and Y-Axis Labels
		int ySteps = 4;
		canvas.StrokeColor = GridStrokeColor;
		canvas.StrokeSize = 1f;
		canvas.FontColor = GridFontColor;
		canvas.FontSize = 11f;

		for (int i = 0; i <= ySteps; i++)
		{
			double val = (maxY / ySteps) * i;
			float y = height - padBottom - (float)((val / maxY) * graphHeight);

			canvas.DrawLine(padLeft, y, width - padRight, y);
			canvas.DrawString($"{Math.Round(val)}mg", 6f, y - 6f, padLeft - 10f, 16f, HorizontalAlignment.Right, VerticalAlignment.Center);
		}

		// 2. Draw X-Axis Hour Lines and Labels (+0h, +4h, +8h, +12h, +16h, +20h, +24h)
		for (int hr = 0; hr <= (int)TotalHours; hr += 4)
		{
			float x = padLeft + (float)((hr / TotalHours) * graphWidth);
			canvas.DrawLine(x, padTop, x, height - padBottom);
			canvas.DrawString($"+{hr}h", x - 15f, height - padBottom + 6f, 30f, 16f, HorizontalAlignment.Center, VerticalAlignment.Top);
		}

		// 3. Draw Dotted Safe Sleep Threshold Line (<20mg)
		float sleepY = height - padBottom - (float)((SleepThresholdMg / maxY) * graphHeight);
		if (sleepY >= padTop && sleepY <= height - padBottom)
		{
			canvas.StrokeColor = SleepColor;
			canvas.StrokeDashPattern = SleepDashPattern;
			canvas.StrokeSize = 1.5f;
			canvas.DrawLine(padLeft, sleepY, width - padRight, sleepY);
			canvas.StrokeDashPattern = null; // Reset dash

			canvas.FontColor = SleepColor;
			canvas.FontSize = 10f;
			canvas.DrawString("Sleep Threshold (20mg)", width - padRight - 140f, sleepY - 14f, 140f, 14f, HorizontalAlignment.Right, VerticalAlignment.Center);
		}

		if (InitialActiveMg > 0.1)
		{
			Color curveColor = GetThemeColor("PrimaryColor", FallbackCurveColor);
			Color accentColor = GetThemeColor("AccentColor", FallbackPointFillColor);

			// 4. Build Curve Path for Exponential Elimination Kinetics
			var curvePath = new PathF();
			var areaPath = new PathF();

			bool isFirst = true;
			float startX = padLeft;
			float startY = height - padBottom - (float)((InitialActiveMg / maxY) * graphHeight);

			areaPath.MoveTo(padLeft, height - padBottom);

			for (double hr = 0.0; hr <= TotalHours; hr += 0.25)
			{
				double remaining = InitialActiveMg * Math.Pow(0.5, hr / HalfLifeHours);
				float x = padLeft + (float)((hr / TotalHours) * graphWidth);
				float y = height - padBottom - (float)((Math.Min(maxY, remaining) / maxY) * graphHeight);

				if (isFirst)
				{
					curvePath.MoveTo(x, y);
					areaPath.LineTo(x, y);
					isFirst = false;
				}
				else
				{
					curvePath.LineTo(x, y);
					areaPath.LineTo(x, y);
				}
			}

			areaPath.LineTo(padLeft + graphWidth, height - padBottom);
			areaPath.Close();

			// Fill under curve with dynamic primary gradient
			var fillPaint = new LinearGradientPaint
			{
				StartPoint = new Point(0, 0),
				EndPoint = new Point(0, 1),
				GradientStops =
				[
					new PaintGradientStop(0.0f, curveColor.WithAlpha(0.25f)),
					new PaintGradientStop(1.0f, curveColor.WithAlpha(0.0f))
				]
			};

			canvas.SetFillPaint(fillPaint, new RectF(padLeft, padTop, graphWidth, graphHeight));
			canvas.FillPath(areaPath);

			// Stroke smooth curve with dynamic PrimaryColor
			canvas.StrokeColor = curveColor;
			canvas.StrokeSize = 3f;
			canvas.DrawPath(curvePath);

			// Highlight active point (+0h) with dynamic AccentColor
			canvas.FillColor = accentColor;
			canvas.FillCircle(startX, startY, 6f);
			canvas.StrokeColor = Colors.White;
			canvas.StrokeSize = 2f;
			canvas.DrawCircle(startX, startY, 6f);
		}
		else
		{
			// Zero state baseline
			canvas.FontColor = GetThemeColor("SecondaryTextColor", GridFontColor);
			canvas.FontSize = 12f;
			canvas.DrawString("No active caffeine in system", padLeft, (padTop + height - padBottom) / 2 - 10f, graphWidth, 20f, HorizontalAlignment.Center, VerticalAlignment.Center);
		}
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
				if (val is SolidColorBrush brush)
					return brush.Color;
			}
		}
		catch
		{
			// Safe headless fallback for unit test runners without an active Application
		}

		return fallback;
	}
}
