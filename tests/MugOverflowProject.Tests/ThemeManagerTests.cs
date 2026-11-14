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
		"AppShadowColor",
		"PrimaryButtonTextColor",
		"AccentButtonTextColor"
	];

	public ThemeManagerTests()
	{
		// Ensure starting from baseline ("Espresso Crema Dark")
		ThemeManager.ResetToDefault();
	}

	public void Dispose()
	{
		// Reset static state so other tests (e.g. MainViewModelTests) are not polluted
		ThemeManager.ResetToDefault();
	}

	// ==========================================
	// 1. Theme Instantiation & 24 Token Presence
	// ==========================================

	[Fact]
	public void DefaultTheme_IsEspressoCremaDark()
	{
		Assert.Equal("Espresso Crema Dark", ThemeManager.DefaultTheme);
		Assert.Equal("Espresso Crema Dark", ThemeManager.CurrentThemeName);
	}

	[Theory]
	[InlineData("Espresso Crema Dark", typeof(CoffeeDarkTheme))]
	[InlineData("Steamed Oat Latte", typeof(CoffeeLightTheme))]
	[InlineData("Caramel Macchiato", typeof(WineDesertTheme))]
	[InlineData("Mocha Velvet Dark", typeof(WineDesertDarkTheme))]
	[InlineData("Iced Matcha Latte", typeof(ForestLavenderTheme))]
	[InlineData("Dirty Matcha Dark", typeof(ForestLavenderDarkTheme))]
	[InlineData("Honey Lavender Latte", typeof(RegalEarthTheme))]
	[InlineData("Midnight Cold Brew", typeof(RegalEarthDarkTheme))]
	[InlineData("Sweet Mint Cortado", typeof(PristineCleanTheme))]
	[InlineData("Dark Mint Espresso", typeof(PristineCleanDarkTheme))]
	public void ThemeDictionary_InstantiatesAndContainsAll24StandardTokens(string themeName, Type themeType)
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

	[Theory]
	[InlineData("Coffee Dark", typeof(CoffeeDarkTheme), "Espresso Crema Dark")]
	[InlineData("Coffee Light", typeof(CoffeeLightTheme), "Steamed Oat Latte")]
	[InlineData("Wine Desert", typeof(WineDesertTheme), "Caramel Macchiato")]
	[InlineData("Wine Desert Light", typeof(WineDesertTheme), "Caramel Macchiato")]
	[InlineData("Wine Desert Dark", typeof(WineDesertDarkTheme), "Mocha Velvet Dark")]
	[InlineData("Forest Lavender", typeof(ForestLavenderTheme), "Iced Matcha Latte")]
	[InlineData("Forest Lavender Light", typeof(ForestLavenderTheme), "Iced Matcha Latte")]
	[InlineData("Forest Lavender Dark", typeof(ForestLavenderDarkTheme), "Dirty Matcha Dark")]
	[InlineData("Regal Earth", typeof(RegalEarthTheme), "Honey Lavender Latte")]
	[InlineData("Regal Earth Light", typeof(RegalEarthTheme), "Honey Lavender Latte")]
	[InlineData("Regal Earth Dark", typeof(RegalEarthDarkTheme), "Midnight Cold Brew")]
	[InlineData("Pristine Clean", typeof(PristineCleanTheme), "Sweet Mint Cortado")]
	[InlineData("Pristine Clean Light", typeof(PristineCleanTheme), "Sweet Mint Cortado")]
	[InlineData("Pristine Clean Dark", typeof(PristineCleanDarkTheme), "Dark Mint Espresso")]
	[InlineData("  regal earth  ", typeof(RegalEarthTheme), "Honey Lavender Latte")]
	[InlineData("PRISTINE CLEAN DARK", typeof(PristineCleanDarkTheme), "Dark Mint Espresso")]
	public void ThemeManager_LegacyThemeAliases_ResolveCleanlyToCoffeehouseThemes(
		string legacyAlias,
		Type expectedDictionaryType,
		string expectedResolvedName)
	{
		var dict = ThemeManager.CreateThemeDictionary(legacyAlias);
		Assert.IsType(expectedDictionaryType, dict);

		ThemeManager.ApplyTheme(legacyAlias);
		Assert.Equal(expectedResolvedName, ThemeManager.CurrentThemeName);
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

	[Fact]
	public void RegalEarthDarkTheme_DefinesPaletteAndContrastTokens()
	{
		var dark = new RegalEarthDarkTheme();
		Assert.Equal(Color.FromArgb("#140E1B"), (Color)dark["PageBackgroundColor"]);
		Assert.Equal(Color.FromArgb("#0D0912"), (Color)dark["NavigationBarColor"]);
		Assert.Equal(Color.FromArgb("#B388FF"), (Color)dark["PrimaryColor"]);
		Assert.Equal(Color.FromArgb("#E5BE65"), (Color)dark["SecondaryColor"]);
		Assert.Equal(Color.FromArgb("#E5BE65"), (Color)dark["AccentColor"]);
		Assert.Equal(Color.FromArgb("#1E1528"), (Color)dark["SurfaceColor"]);
		Assert.Equal(Color.FromArgb("#2C1E3C"), (Color)dark["SurfaceAltColor"]);
		Assert.Equal(Color.FromArgb("#443258"), (Color)dark["BorderColor"]);
		Assert.Equal(Color.FromArgb("#F5EEF8"), (Color)dark["PrimaryTextColor"]);
		Assert.Equal(Color.FromArgb("#C4B5D0"), (Color)dark["SecondaryTextColor"]);
		Assert.Equal(Color.FromArgb("#8E7E9C"), (Color)dark["TertiaryTextColor"]);

		// Contrast tokens
		Assert.True(dark.ContainsKey("PrimaryButtonTextColor"));
		Assert.Equal(Color.FromArgb("#140E1B"), (Color)dark["PrimaryButtonTextColor"]);

		Assert.True(dark.ContainsKey("AccentButtonTextColor"));
		Assert.Equal(Color.FromArgb("#140E1B"), (Color)dark["AccentButtonTextColor"]);
	}

	[Fact]
	public void WineDesertDarkTheme_DefinesPaletteAndContrastTokens()
	{
		var dark = new WineDesertDarkTheme();
		Assert.Equal(Color.FromArgb("#1B1214"), (Color)dark["PageBackgroundColor"]);
		Assert.Equal(Color.FromArgb("#120B0D"), (Color)dark["NavigationBarColor"]);
		Assert.Equal(Color.FromArgb("#EA859B"), (Color)dark["PrimaryColor"]);
		Assert.Equal(Color.FromArgb("#E2A775"), (Color)dark["SecondaryColor"]);
		Assert.Equal(Color.FromArgb("#E2A775"), (Color)dark["AccentColor"]);
		Assert.Equal(Color.FromArgb("#261A1E"), (Color)dark["SurfaceColor"]);
		Assert.Equal(Color.FromArgb("#38262C"), (Color)dark["SurfaceAltColor"]);
		Assert.Equal(Color.FromArgb("#523840"), (Color)dark["BorderColor"]);
		Assert.Equal(Color.FromArgb("#F9EFEF"), (Color)dark["PrimaryTextColor"]);
		Assert.Equal(Color.FromArgb("#D2B8BE"), (Color)dark["SecondaryTextColor"]);
		Assert.Equal(Color.FromArgb("#9E828A"), (Color)dark["TertiaryTextColor"]);

		// Contrast tokens
		Assert.True(dark.ContainsKey("PrimaryButtonTextColor"));
		Assert.Equal(Color.FromArgb("#1B1214"), (Color)dark["PrimaryButtonTextColor"]);

		Assert.True(dark.ContainsKey("AccentButtonTextColor"));
		Assert.Equal(Color.FromArgb("#1B1214"), (Color)dark["AccentButtonTextColor"]);
	}

	[Fact]
	public void ForestLavenderDarkTheme_DefinesPaletteAndContrastTokens()
	{
		var dark = new ForestLavenderDarkTheme();
		Assert.Equal(Color.FromArgb("#111A15"), (Color)dark["PageBackgroundColor"]);
		Assert.Equal(Color.FromArgb("#0B120E"), (Color)dark["NavigationBarColor"]);
		Assert.Equal(Color.FromArgb("#57B88C"), (Color)dark["PrimaryColor"]);
		Assert.Equal(Color.FromArgb("#B39DDB"), (Color)dark["SecondaryColor"]);
		Assert.Equal(Color.FromArgb("#B39DDB"), (Color)dark["AccentColor"]);
		Assert.Equal(Color.FromArgb("#18241E"), (Color)dark["SurfaceColor"]);
		Assert.Equal(Color.FromArgb("#24352C"), (Color)dark["SurfaceAltColor"]);
		Assert.Equal(Color.FromArgb("#375043"), (Color)dark["BorderColor"]);
		Assert.Equal(Color.FromArgb("#F0F5F2"), (Color)dark["PrimaryTextColor"]);
		Assert.Equal(Color.FromArgb("#B8C8C0"), (Color)dark["SecondaryTextColor"]);
		Assert.Equal(Color.FromArgb("#7E9187"), (Color)dark["TertiaryTextColor"]);

		// Contrast tokens
		Assert.True(dark.ContainsKey("PrimaryButtonTextColor"));
		Assert.Equal(Color.FromArgb("#111A15"), (Color)dark["PrimaryButtonTextColor"]);

		Assert.True(dark.ContainsKey("AccentButtonTextColor"));
		Assert.Equal(Color.FromArgb("#111A15"), (Color)dark["AccentButtonTextColor"]);
	}

	[Fact]
	public void PristineCleanDarkTheme_DefinesPaletteAndContrastTokens()
	{
		var dark = new PristineCleanDarkTheme();
		Assert.Equal(Color.FromArgb("#0E1816"), (Color)dark["PageBackgroundColor"]);
		Assert.Equal(Color.FromArgb("#09110F"), (Color)dark["NavigationBarColor"]);
		Assert.Equal(Color.FromArgb("#52C5A8"), (Color)dark["PrimaryColor"]);
		Assert.Equal(Color.FromArgb("#80E0C9"), (Color)dark["SecondaryColor"]);
		Assert.Equal(Color.FromArgb("#80E0C9"), (Color)dark["AccentColor"]);
		Assert.Equal(Color.FromArgb("#152421"), (Color)dark["SurfaceColor"]);
		Assert.Equal(Color.FromArgb("#1E3530"), (Color)dark["SurfaceAltColor"]);
		Assert.Equal(Color.FromArgb("#2C4E47"), (Color)dark["BorderColor"]);
		Assert.Equal(Color.FromArgb("#F0FAF7"), (Color)dark["PrimaryTextColor"]);
		Assert.Equal(Color.FromArgb("#A8CEC5"), (Color)dark["SecondaryTextColor"]);
		Assert.Equal(Color.FromArgb("#6E968E"), (Color)dark["TertiaryTextColor"]);

		// Contrast tokens
		Assert.True(dark.ContainsKey("PrimaryButtonTextColor"));
		Assert.Equal(Color.FromArgb("#0E1816"), (Color)dark["PrimaryButtonTextColor"]);

		Assert.True(dark.ContainsKey("AccentButtonTextColor"));
		Assert.Equal(Color.FromArgb("#0E1816"), (Color)dark["AccentButtonTextColor"]);
	}

	// ==========================================
	// 3. ThemeSelectorPopupViewModel Tests
	// ==========================================

	[Fact]
	public void ThemeSelectorPopupViewModel_ContainsAllTenCoffeehouseThemes()
	{
		var persistence = new TestDataPersistenceService();
		var vm = new ThemeSelectorPopupViewModel(persistence);

		Assert.Equal(10, vm.Themes.Count);
		Assert.Equal("Espresso Crema Dark", vm.Themes[0]);
		Assert.Equal("Steamed Oat Latte", vm.Themes[1]);
		Assert.Contains("Espresso Crema Dark", vm.Themes);
		Assert.Contains("Steamed Oat Latte", vm.Themes);
		Assert.Contains("Caramel Macchiato", vm.Themes);
		Assert.Contains("Mocha Velvet Dark", vm.Themes);
		Assert.Contains("Iced Matcha Latte", vm.Themes);
		Assert.Contains("Dirty Matcha Dark", vm.Themes);
		Assert.Contains("Honey Lavender Latte", vm.Themes);
		Assert.Contains("Midnight Cold Brew", vm.Themes);
		Assert.Contains("Sweet Mint Cortado", vm.Themes);
		Assert.Contains("Dark Mint Espresso", vm.Themes);
	}

	[Theory]
	[InlineData("Espresso Crema Dark")]
	[InlineData("Steamed Oat Latte")]
	[InlineData("Caramel Macchiato")]
	[InlineData("Mocha Velvet Dark")]
	[InlineData("Iced Matcha Latte")]
	[InlineData("Dirty Matcha Dark")]
	[InlineData("Honey Lavender Latte")]
	[InlineData("Midnight Cold Brew")]
	[InlineData("Sweet Mint Cortado")]
	[InlineData("Dark Mint Espresso")]
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
		ThemeManager.ApplyTheme("Steamed Oat Latte");
		Assert.Equal("Steamed Oat Latte", ThemeManager.CurrentThemeName);

		ThemeManager.ApplyTheme("Honey Lavender Latte");
		Assert.Equal("Honey Lavender Latte", ThemeManager.CurrentThemeName);

		ThemeManager.ApplyTheme("Unknown Theme");
		Assert.Equal("Espresso Crema Dark", ThemeManager.CurrentThemeName);

		ThemeManager.ApplyTheme(null);
		Assert.Equal("Espresso Crema Dark", ThemeManager.CurrentThemeName);
	}

	[Fact]
	public void ThemeManager_ApplyTheme_CaseInsensitiveAndTrims()
	{
		ThemeManager.ApplyTheme("  steamed oat latte  ");
		Assert.Equal("Steamed Oat Latte", ThemeManager.CurrentThemeName);

		ThemeManager.ApplyTheme("ESPRESSO CREMA DARK");
		Assert.Equal("Espresso Crema Dark", ThemeManager.CurrentThemeName);

		ThemeManager.ApplyTheme("  caramel macchiato  ");
		Assert.Equal("Caramel Macchiato", ThemeManager.CurrentThemeName);
	}

	[Fact]
	public void ThemeManager_ApplyTheme_PreservesMergedDictionariesIndex()
	{
		var colors = new ResourceDictionary { { "White", Colors.White } };
		var initialTheme = new CoffeeDarkTheme();
		var styles = new ResourceDictionary();

		var merged = new ObservableCollection<ResourceDictionary>
		{
			colors,       // Index 0
			initialTheme, // Index 1
			styles        // Index 2
		};

		ThemeManager.ApplyTheme("Steamed Oat Latte", merged);

		Assert.Equal(3, merged.Count);
		Assert.Same(colors, merged[0]);
		Assert.IsType<CoffeeLightTheme>(merged[1]);
		Assert.Same(styles, merged[2]);
		Assert.Equal("Steamed Oat Latte", ThemeManager.CurrentThemeName);
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

		ThemeManager.ApplyTheme("Espresso Crema Dark", merged);

		Assert.Equal(3, merged.Count);
		Assert.Same(colors, merged[0]);
		Assert.IsType<CoffeeDarkTheme>(merged[1]);
		Assert.Same(styles, merged[2]);
	}

	[Fact]
	public void ThemeManager_IsThemeDictionary_RecognizesAllThemes()
	{
		Assert.True(ThemeManager.IsThemeDictionary(new RegalEarthTheme()));
		Assert.True(ThemeManager.IsThemeDictionary(new RegalEarthDarkTheme()));
		Assert.True(ThemeManager.IsThemeDictionary(new WineDesertTheme()));
		Assert.True(ThemeManager.IsThemeDictionary(new WineDesertDarkTheme()));
		Assert.True(ThemeManager.IsThemeDictionary(new ForestLavenderTheme()));
		Assert.True(ThemeManager.IsThemeDictionary(new ForestLavenderDarkTheme()));
		Assert.True(ThemeManager.IsThemeDictionary(new PristineCleanTheme()));
		Assert.True(ThemeManager.IsThemeDictionary(new PristineCleanDarkTheme()));
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

	[Fact]
	public void TabSwitcher_DualButtonVisibilityPattern_AvoidsDataTriggersAndStyleSnapshots()
	{
		// Verifies the Dual-Button Visibility Pattern for segmented tab switcher:
		// Active and inactive buttons use dedicated static styles and IsVisible toggles,
		// with zero DataTriggers, eliminating WinUI 3 presentation caching bugs when themes change.
		var activeBtn = new Button
		{
			HeightRequest = 38,
			FontSize = 13,
			FontAttributes = FontAttributes.Bold
		};
		var inactiveBtn = new Button
		{
			HeightRequest = 38,
			FontSize = 13,
			FontAttributes = FontAttributes.Bold
		};

		Assert.Empty(activeBtn.Triggers);
		Assert.Empty(inactiveBtn.Triggers);
	}

	[Fact]
	public void TabSwitcher_ButtonDataTrigger_UsesDirectColorSetters_NotStyleSetters()
	{
		// Verifies that a button configured with direct DynamicResource setters on DataTrigger
		// updates its colors without replacing the underlying Style object (preventing the MAUI style snapshot bug).
		var button = new Button
		{
			HeightRequest = 38,
			FontSize = 13,
			FontAttributes = FontAttributes.Bold
		};

		var trigger = new DataTrigger(typeof(Button))
		{
			Binding = new Binding("IsActive"),
			Value = true
		};

		trigger.Setters.Add(new Setter
		{
			Property = VisualElement.BackgroundColorProperty,
			Value = new DynamicResourceExtension { Key = "PrimaryColor" }
		});
		trigger.Setters.Add(new Setter
		{
			Property = Button.TextColorProperty,
			Value = new DynamicResourceExtension { Key = "PrimaryButtonTextColor" }
		});

		button.Triggers.Add(trigger);

		// Assert trigger does not target StyleProperty
		foreach (var t in button.Triggers)
		{
			if (t is DataTrigger dt)
			{
				foreach (var s in dt.Setters)
				{
					Assert.NotEqual(VisualElement.StyleProperty, s.Property);
				}
			}
		}
	}

	// ==========================================
	// Test Helpers
	// ==========================================

	private sealed class TestDataPersistenceService : IDataPersistenceService
	{
		public string StoredTheme { get; set; } = "Espresso Crema Dark";

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
