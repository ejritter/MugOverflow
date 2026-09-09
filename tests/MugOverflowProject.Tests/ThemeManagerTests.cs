using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using MugOverflowProject.Controls;
using MugOverflowProject.Models;
using MugOverflowProject.Resources.Styles.Themes;
using MugOverflowProject.Services;
using MugOverflowProject.ViewModels;
using Xunit;

namespace MugOverflowProject.Tests;

public sealed class ThemeManagerTests : IDisposable
{
	private static readonly string[] StandardTokens =
	[
		"ThemeName",
		"PageBackgroundColor",
		"NavigationBarColor",
		"PrimaryColor",
		"SecondaryColor",
		"AccentColor",
		"SurfaceColor",
		"SurfaceAltColor",
		"BorderColor",
		"PrimaryTextColor",
		"SecondaryTextColor",
		"TertiaryTextColor",
		"SuccessColor",
		"WarningColor",
		"ErrorColor",
		"InfoColor",
		"DecorativeColor",
		"TransparentColor",
		"PrimaryGradientStart",
		"PrimaryGradientEnd",
		"CardShadowColor",
		"AppShadowColor"
	];

	public ThemeManagerTests()
	{
		// Ensure starting from baseline
		ThemeManager.ResetToDefault();
	}

	public void Dispose()
	{
		// Reset static state so other tests (e.g. MainViewModelTests) are not polluted
		ThemeManager.ResetToDefault();
	}

	// ==========================================
	// 1. Theme Instantiation & 22 Token Presence
	// ==========================================

	[Theory]
	[InlineData("Regal Earth", typeof(RegalEarthTheme))]
	[InlineData("Wine Desert", typeof(WineDesertTheme))]
	[InlineData("Forest Lavender", typeof(ForestLavenderTheme))]
	[InlineData("Pristine Clean", typeof(PristineCleanTheme))]
	[InlineData("Coffee Light", typeof(CoffeeLightTheme))]
	[InlineData("Coffee Dark", typeof(CoffeeDarkTheme))]
	public void ThemeDictionary_InstantiatesAndContainsAll22StandardTokens(string themeName, Type themeType)
	{
		var dict = (ResourceDictionary)Activator.CreateInstance(themeType)!;
		Assert.NotNull(dict);

		foreach (var token in StandardTokens)
		{
			Assert.True(dict.ContainsKey(token), $"Theme '{themeName}' is missing required token '{token}'.");
			Assert.NotNull(dict[token]);
		}

		Assert.Equal(themeName, dict["ThemeName"]);
	}

	// ==========================================
	// 2. Palette Values & SecondaryColor Hardening
	// ==========================================

	[Fact]
	public void ExistingThemes_DefineCorrectHardenedSecondaryColor()
	{
		var regal = new RegalEarthTheme();
		Assert.Equal(Color.FromArgb("#C89B3C"), (Color)regal["SecondaryColor"]);

		var wine = new WineDesertTheme();
		Assert.Equal(Color.FromArgb("#C28B5B"), (Color)wine["SecondaryColor"]);

		var forest = new ForestLavenderTheme();
		Assert.Equal(Color.FromArgb("#7E57C2"), (Color)forest["SecondaryColor"]);

		var pristine = new PristineCleanTheme();
		Assert.Equal(Color.FromArgb("#00897B"), (Color)pristine["SecondaryColor"]);
	}

	[Fact]
	public void CoffeeLightTheme_DefinesPaletteAndContrastTokens()
	{
		var light = new CoffeeLightTheme();
		Assert.Equal(Color.FromArgb("#FBF8F2"), (Color)light["PageBackgroundColor"]);
		Assert.Equal(Color.FromArgb("#5C3818"), (Color)light["NavigationBarColor"]);
		Assert.Equal(Color.FromArgb("#5C3818"), (Color)light["PrimaryColor"]);
		Assert.Equal(Color.FromArgb("#C4823F"), (Color)light["SecondaryColor"]);
		Assert.Equal(Color.FromArgb("#C4823F"), (Color)light["AccentColor"]);
		Assert.Equal(Color.FromArgb("#FFFFFF"), (Color)light["SurfaceColor"]);
		Assert.Equal(Color.FromArgb("#F2EBE1"), (Color)light["SurfaceAltColor"]);
		Assert.Equal(Color.FromArgb("#E5DCB8"), (Color)light["BorderColor"]);
		Assert.Equal(Color.FromArgb("#1C1510"), (Color)light["PrimaryTextColor"]);
		Assert.Equal(Color.FromArgb("#7A6A58"), (Color)light["SecondaryTextColor"]);
		Assert.Equal(Color.FromArgb("#A39281"), (Color)light["TertiaryTextColor"]);

		// Contrast tokens
		Assert.True(light.ContainsKey("PrimaryButtonTextColor"));
		Assert.Equal(Colors.White, (Color)light["PrimaryButtonTextColor"]);

		Assert.True(light.ContainsKey("AccentButtonTextColor"));
		Assert.Equal(Color.FromArgb("#1C1510"), (Color)light["AccentButtonTextColor"]);
	}

	[Fact]
	public void CoffeeDarkTheme_DefinesPaletteAndContrastTokens()
	{
		var dark = new CoffeeDarkTheme();
		Assert.Equal(Color.FromArgb("#1C1510"), (Color)dark["PageBackgroundColor"]);
		Assert.Equal(Color.FromArgb("#140F0B"), (Color)dark["NavigationBarColor"]);
		Assert.Equal(Color.FromArgb("#D4975A"), (Color)dark["PrimaryColor"]);
		Assert.Equal(Color.FromArgb("#E8B878"), (Color)dark["SecondaryColor"]);
		Assert.Equal(Color.FromArgb("#E8B878"), (Color)dark["AccentColor"]);
		Assert.Equal(Color.FromArgb("#251E17"), (Color)dark["SurfaceColor"]);
		Assert.Equal(Color.FromArgb("#322920"), (Color)dark["SurfaceAltColor"]);
		Assert.Equal(Color.FromArgb("#44372B"), (Color)dark["BorderColor"]);
		Assert.Equal(Color.FromArgb("#FBF8F2"), (Color)dark["PrimaryTextColor"]);
		Assert.Equal(Color.FromArgb("#C4B5A5"), (Color)dark["SecondaryTextColor"]);
		Assert.Equal(Color.FromArgb("#8E7E70"), (Color)dark["TertiaryTextColor"]);

		// Contrast tokens
		Assert.True(dark.ContainsKey("PrimaryButtonTextColor"));
		Assert.Equal(Color.FromArgb("#1C1510"), (Color)dark["PrimaryButtonTextColor"]);

		Assert.True(dark.ContainsKey("AccentButtonTextColor"));
		Assert.Equal(Color.FromArgb("#1C1510"), (Color)dark["AccentButtonTextColor"]);
	}

	// ==========================================
	// 3. ThemeSelectorPopupViewModel Tests
	// ==========================================

	[Fact]
	public void ThemeSelectorPopupViewModel_ContainsAllSixThemes()
	{
		var persistence = new TestDataPersistenceService();
		var vm = new ThemeSelectorPopupViewModel(persistence);

		Assert.Equal(6, vm.Themes.Count);
		Assert.Contains("Regal Earth", vm.Themes);
		Assert.Contains("Wine Desert", vm.Themes);
		Assert.Contains("Forest Lavender", vm.Themes);
		Assert.Contains("Pristine Clean", vm.Themes);
		Assert.Contains("Coffee Light", vm.Themes);
		Assert.Contains("Coffee Dark", vm.Themes);
	}

	[Theory]
	[InlineData("Coffee Light")]
	[InlineData("Coffee Dark")]
	[InlineData("Wine Desert")]
	[InlineData("Forest Lavender")]
	[InlineData("Pristine Clean")]
	[InlineData("Regal Earth")]
	public void ThemeSelectorPopupViewModel_SelectThemeCommand_AppliesAndPersistsTheme(string targetTheme)
	{
		var persistence = new TestDataPersistenceService();
		var vm = new ThemeSelectorPopupViewModel(persistence);

		vm.SelectThemeCommand.Execute(targetTheme);

		Assert.Equal(targetTheme, vm.SelectedThemeName);
		Assert.Equal(targetTheme, persistence.StoredTheme);
		Assert.Equal(targetTheme, ThemeManager.CurrentThemeName);
	}

	[Fact]
	public void ThemeSelectorPopupViewModel_SelectThemeCommand_NullOrEmpty_Ignored()
	{
		var persistence = new TestDataPersistenceService();
		var vm = new ThemeSelectorPopupViewModel(persistence);
		string initialTheme = vm.SelectedThemeName;

		vm.SelectThemeCommand.Execute(null);
		Assert.Equal(initialTheme, vm.SelectedThemeName);

		vm.SelectThemeCommand.Execute("   ");
		Assert.Equal(initialTheme, vm.SelectedThemeName);
	}

	// ==========================================
	// 4. Headless Safety & Index Preservation
	// ==========================================

	[Fact]
	public void ThemeManager_ApplyTheme_HeadlessNullSafety_UpdatesCurrentThemeName()
	{
		// In xUnit testhost, Application.Current is null
		ThemeManager.ApplyTheme("Coffee Light");
		Assert.Equal("Coffee Light", ThemeManager.CurrentThemeName);

		ThemeManager.ApplyTheme("Coffee Dark");
		Assert.Equal("Coffee Dark", ThemeManager.CurrentThemeName);

		ThemeManager.ApplyTheme("Unknown Theme");
		Assert.Equal("Regal Earth", ThemeManager.CurrentThemeName);

		ThemeManager.ApplyTheme(null);
		Assert.Equal("Regal Earth", ThemeManager.CurrentThemeName);
	}

	[Fact]
	public void ThemeManager_ApplyTheme_CaseInsensitiveAndTrims()
	{
		ThemeManager.ApplyTheme("  coffee dark  ");
		Assert.Equal("Coffee Dark", ThemeManager.CurrentThemeName);

		ThemeManager.ApplyTheme("COFFEE LIGHT");
		Assert.Equal("Coffee Light", ThemeManager.CurrentThemeName);
	}

	[Fact]
	public void ThemeManager_ApplyTheme_PreservesMergedDictionariesIndex()
	{
		var colors = new ResourceDictionary { { "White", Colors.White } };
		var initialTheme = new RegalEarthTheme();
		var styles = new ResourceDictionary();

		var merged = new ObservableCollection<ResourceDictionary>
		{
			colors,       // Index 0
			initialTheme, // Index 1
			styles        // Index 2
		};

		ThemeManager.ApplyTheme("Coffee Light", merged);

		Assert.Equal(3, merged.Count);
		Assert.Same(colors, merged[0]);
		Assert.IsType<CoffeeLightTheme>(merged[1]);
		Assert.Same(styles, merged[2]);
		Assert.Equal("Coffee Light", ThemeManager.CurrentThemeName);
	}

	[Fact]
	public void ThemeManager_ApplyTheme_InsertsAtClampedIndex_WhenNoExistingTheme()
	{
		var colors = new ResourceDictionary { { "White", Colors.White } };
		var styles = new ResourceDictionary();

		var merged = new ObservableCollection<ResourceDictionary>
		{
			colors, // Index 0
			styles  // Index 1
		};

		ThemeManager.ApplyTheme("Coffee Dark", merged);

		Assert.Equal(3, merged.Count);
		Assert.Same(colors, merged[0]);
		Assert.IsType<CoffeeDarkTheme>(merged[1]);
		Assert.Same(styles, merged[2]);
	}

	[Fact]
	public void ThemeManager_IsThemeDictionary_RecognizesAllThemes()
	{
		Assert.True(ThemeManager.IsThemeDictionary(new RegalEarthTheme()));
		Assert.True(ThemeManager.IsThemeDictionary(new WineDesertTheme()));
		Assert.True(ThemeManager.IsThemeDictionary(new ForestLavenderTheme()));
		Assert.True(ThemeManager.IsThemeDictionary(new PristineCleanTheme()));
		Assert.True(ThemeManager.IsThemeDictionary(new CoffeeLightTheme()));
		Assert.True(ThemeManager.IsThemeDictionary(new CoffeeDarkTheme()));

		var customThemeDict = new ResourceDictionary { { "ThemeName", "Custom Theme" } };
		Assert.True(ThemeManager.IsThemeDictionary(customThemeDict));

		var dummyDict = new ResourceDictionary { { "SomeKey", "SomeValue" } };
		Assert.False(ThemeManager.IsThemeDictionary(dummyDict));
		Assert.False(ThemeManager.IsThemeDictionary(null));
	}

	// ==========================================
	// 5. CaffeineMetabolismDrawable Dynamic Colors
	// ==========================================

	[Fact]
	public void MetabolismDrawable_Draw_HeadlessSafety_DoesNotThrow()
	{
		var drawable = new CaffeineMetabolismDrawable { InitialActiveMg = 150.0 };
		var canvas = new FakeCanvas();
		var dirtyRect = new RectF(0, 0, 400, 200);

		var exception = Record.Exception(() => drawable.Draw(canvas, dirtyRect));
		Assert.Null(exception);
	}

	[Fact]
	public void MetabolismDrawable_ZeroState_HeadlessSafety_DoesNotThrow()
	{
		var drawable = new CaffeineMetabolismDrawable { InitialActiveMg = 0.0 };
		var canvas = new FakeCanvas();
		var dirtyRect = new RectF(0, 0, 400, 200);

		var exception = Record.Exception(() => drawable.Draw(canvas, dirtyRect));
		Assert.Null(exception);
	}

	[Fact]
	public void MetabolismDrawable_ZeroRect_ReturnsEarlyWithoutThrowing()
	{
		var drawable = new CaffeineMetabolismDrawable { InitialActiveMg = 100.0 };
		var canvas = new FakeCanvas();
		var dirtyRect = new RectF(0, 0, 0, 0);

		var exception = Record.Exception(() => drawable.Draw(canvas, dirtyRect));
		Assert.Null(exception);
	}

	// ==========================================
	// Test Helpers
	// ==========================================

	private sealed class TestDataPersistenceService : IDataPersistenceService
	{
		public string StoredTheme { get; set; } = "Regal Earth";

		public Task<List<OracleDecisionLog>> LoadLogsAsync(CancellationToken cancellationToken = default)
			=> Task.FromResult(new List<OracleDecisionLog>());

		public Task SaveLogsAsync(IEnumerable<OracleDecisionLog> logs, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public string GetSavedTheme() => StoredTheme;

		public void SaveTheme(string themeName) => StoredTheme = themeName;
	}

	private sealed class FakeCanvas : ICanvas
	{
		public float DisplayScale { get; set; } = 1f;
		public float StrokeSize { get; set; }
		public Color StrokeColor { get; set; } = Colors.Black;
		public LineCap StrokeLineCap { get; set; }
		public LineJoin StrokeLineJoin { get; set; }
		public float MiterLimit { get; set; }
		public Color FillColor { get; set; } = Colors.White;
		public Color FontColor { get; set; } = Colors.Black;
		public IFont Font { get; set; } = Microsoft.Maui.Graphics.Font.Default;
		public float FontSize { get; set; }
		public float Alpha { get; set; } = 1f;
		public bool Antialias { get; set; }
		public BlendMode BlendMode { get; set; }
		public float[]? StrokeDashPattern { get; set; }
		public float StrokeDashOffset { get; set; }

		public void DrawArc(float x, float y, float width, float height, float startAngle, float endAngle, bool clockwise, bool closed) { }
		public void FillArc(float x, float y, float width, float height, float startAngle, float endAngle, bool clockwise) { }
		public void DrawEllipse(float x, float y, float width, float height) { }
		public void FillEllipse(float x, float y, float width, float height) { }
		public void DrawCircle(float centerX, float centerY, float radius) { }
		public void FillCircle(float centerX, float centerY, float radius) { }
		public void DrawLine(float x1, float y1, float x2, float y2) { }
		public void DrawPath(PathF path) { }
		public void FillPath(PathF path, WindingMode windingMode) { }
		public void DrawRectangle(float x, float y, float width, float height) { }
		public void FillRectangle(float x, float y, float width, float height) { }
		public void DrawRoundedRectangle(float x, float y, float width, float height, float cornerRadius) { }
		public void FillRoundedRectangle(float x, float y, float width, float height, float cornerRadius) { }
		public void DrawString(string? value, float x, float y, HorizontalAlignment horizontalAlignment) { }
		public void DrawString(string? value, float x, float y, float width, float height, HorizontalAlignment horizontalAlignment, VerticalAlignment verticalAlignment, TextFlow textFlow = TextFlow.ClipBounds, float lineSpacingAdjustment = 0) { }
		public void DrawText(Microsoft.Maui.Graphics.Text.IAttributedText? value, float x, float y, float width, float height) { }
		public SizeF GetStringSize(string value, IFont font, float fontSize) => new(10, 10);
		public SizeF GetStringSize(string value, IFont font, float fontSize, HorizontalAlignment horizontalAlignment, VerticalAlignment verticalAlignment) => new(10, 10);
		public void SetFillPaint(Paint paint, RectF rectangle) { }
		public void SetShadow(SizeF offset, float blur, Color color) { }
		public void SubtractFromClip(float x, float y, float width, float height) { }
		public void ClipPath(PathF path, WindingMode windingMode = WindingMode.NonZero) { }
		public void ClipRectangle(float x, float y, float width, float height) { }
		public void ResetStroke() { }
		public void ConcatenateTransform(System.Numerics.Matrix3x2 transform) { }
		public void Rotate(float degrees, float x, float y) { }
		public void Rotate(float degrees) { }
		public void Scale(float sx, float sy) { }
		public void Translate(float tx, float ty) { }
		public void SaveState() { }
		public bool RestoreState() => true;
		public void ResetState() { }
		public void DrawImage(Microsoft.Maui.Graphics.IImage image, float x, float y, float width, float height) { }
	}
}
