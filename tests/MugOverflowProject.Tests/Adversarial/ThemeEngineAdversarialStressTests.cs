using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using MugOverflowProject.Resources.Styles.Themes;
using MugOverflowProject.ViewModels;
using Xunit;

namespace MugOverflowProject.Tests.Adversarial;

public sealed class ThemeEngineAdversarialStressTests : IDisposable
{
	public ThemeEngineAdversarialStressTests()
	{
		ThemeManager.ResetToDefault();
	}

	public void Dispose()
	{
		ThemeManager.ResetToDefault();
	}

	// =========================================================================
	// 1. WCAG 2.1 Contrast Ratio Verification (Mathematical & Empirical)
	// =========================================================================

	private static double CalculateRelativeLuminance(Color color)
	{
		static double ConvertChannel(double c) =>
			c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

		double r = ConvertChannel(color.Red);
		double g = ConvertChannel(color.Green);
		double b = ConvertChannel(color.Blue);

		return 0.2126 * r + 0.7152 * g + 0.0722 * b;
	}

	private static double CalculateContrastRatio(Color foreground, Color background)
	{
		double lum1 = CalculateRelativeLuminance(foreground);
		double lum2 = CalculateRelativeLuminance(background);

		double lighter = Math.Max(lum1, lum2);
		double darker = Math.Min(lum1, lum2);

		return (lighter + 0.05) / (darker + 0.05);
	}

	[Fact]
	public void CoffeeLight_PrimaryButton_ContrastRatio_ExceedsWcagAaa()
	{
		var theme = new CoffeeLightTheme();
		var text = (Color)theme["PrimaryButtonTextColor"];
		var bg = (Color)theme["PrimaryColor"];

		Assert.Equal(Colors.White, text);
		Assert.Equal(Color.FromArgb("#5C3818"), bg);

		double ratio = CalculateContrastRatio(text, bg);

		// WCAG AAA requires >= 7.0:1 for normal text (White on #5C3818 is ~10.39:1)
		Assert.True(ratio >= 7.0, $"Expected ratio >= 7.0:1, got {ratio:F2}:1");
		Assert.InRange(ratio, 10.0, 11.0);
	}

	[Fact]
	public void CoffeeDark_PrimaryButton_ContrastRatio_ExceedsWcagAaa()
	{
		var theme = new CoffeeDarkTheme();
		var text = (Color)theme["PrimaryButtonTextColor"];
		var bg = (Color)theme["PrimaryColor"];

		Assert.Equal(Color.FromArgb("#1C1510"), text);
		Assert.Equal(Color.FromArgb("#D4975A"), bg);

		double ratio = CalculateContrastRatio(text, bg);

		// WCAG AAA requires >= 7.0:1 for normal text (#1C1510 on #D4975A is ~7.19:1)
		Assert.True(ratio >= 7.0, $"Expected ratio >= 7.0:1, got {ratio:F2}:1");
		Assert.InRange(ratio, 7.1, 7.3);
	}

	[Fact]
	public void CoffeeLight_AccentButton_ContrastRatio_ExceedsWcagAa()
	{
		var theme = new CoffeeLightTheme();
		var text = (Color)theme["AccentButtonTextColor"];
		var bg = (Color)theme["AccentColor"];

		Assert.Equal(Color.FromArgb("#1C1510"), text);
		Assert.Equal(Color.FromArgb("#C4823F"), bg);

		double ratio = CalculateContrastRatio(text, bg);

		// WCAG AA requires >= 4.5:1 for normal text (#1C1510 on #C4823F is ~5.66:1)
		Assert.True(ratio >= 4.5, $"Expected ratio >= 4.5:1, got {ratio:F2}:1");
		Assert.InRange(ratio, 5.5, 5.8);
	}

	[Fact]
	public void CoffeeDark_AccentButton_ContrastRatio_ExceedsWcagAaa()
	{
		var theme = new CoffeeDarkTheme();
		var text = (Color)theme["AccentButtonTextColor"];
		var bg = (Color)theme["AccentColor"];

		Assert.Equal(Color.FromArgb("#1C1510"), text);
		Assert.Equal(Color.FromArgb("#E8B878"), bg);

		double ratio = CalculateContrastRatio(text, bg);

		// WCAG AAA requires >= 7.0:1 for normal text (#1C1510 on #E8B878 is ~9.95:1)
		Assert.True(ratio >= 7.0, $"Expected ratio >= 7.0:1, got {ratio:F2}:1");
		Assert.InRange(ratio, 9.8, 10.1);
	}

	[Fact]
	public void RegalEarthDark_PrimaryButton_ContrastRatio_ExceedsWcagAaa()
	{
		var theme = new RegalEarthDarkTheme();
		var text = (Color)theme["PrimaryButtonTextColor"];
		var bg = (Color)theme["PrimaryColor"];

		Assert.Equal(Color.FromArgb("#140E1B"), text);
		Assert.Equal(Color.FromArgb("#B388FF"), bg);

		double ratio = CalculateContrastRatio(text, bg);

		// WCAG AAA requires >= 7.0:1 for normal text
		Assert.True(ratio >= 7.0, $"Expected ratio >= 7.0:1, got {ratio:F2}:1");
	}

	[Fact]
	public void WineDesertDark_PrimaryButton_ContrastRatio_ExceedsWcagAaa()
	{
		var theme = new WineDesertDarkTheme();
		var text = (Color)theme["PrimaryButtonTextColor"];
		var bg = (Color)theme["PrimaryColor"];

		Assert.Equal(Color.FromArgb("#1B1214"), text);
		Assert.Equal(Color.FromArgb("#EA859B"), bg);

		double ratio = CalculateContrastRatio(text, bg);

		// WCAG AAA requires >= 7.0:1 for normal text
		Assert.True(ratio >= 7.0, $"Expected ratio >= 7.0:1, got {ratio:F2}:1");
	}

	[Fact]
	public void ForestLavenderDark_PrimaryButton_ContrastRatio_ExceedsWcagAaa()
	{
		var theme = new ForestLavenderDarkTheme();
		var text = (Color)theme["PrimaryButtonTextColor"];
		var bg = (Color)theme["PrimaryColor"];

		Assert.Equal(Color.FromArgb("#111A15"), text);
		Assert.Equal(Color.FromArgb("#57B88C"), bg);

		double ratio = CalculateContrastRatio(text, bg);

		// WCAG AAA requires >= 7.0:1 for normal text
		Assert.True(ratio >= 7.0, $"Expected ratio >= 7.0:1, got {ratio:F2}:1");
	}

	[Fact]
	public void PristineCleanDark_PrimaryButton_ContrastRatio_ExceedsWcagAaa()
	{
		var theme = new PristineCleanDarkTheme();
		var text = (Color)theme["PrimaryButtonTextColor"];
		var bg = (Color)theme["PrimaryColor"];

		Assert.Equal(Color.FromArgb("#0E1816"), text);
		Assert.Equal(Color.FromArgb("#52C5A8"), bg);

		double ratio = CalculateContrastRatio(text, bg);

		// WCAG AAA requires >= 7.0:1 for normal text
		Assert.True(ratio >= 7.0, $"Expected ratio >= 7.0:1, got {ratio:F2}:1");
	}

	// =========================================================================
	// 2. Adversarial Input Stress Testing (Null, Garbage, Injection, Large Inputs)
	// =========================================================================

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("\t\r\n")]
	[InlineData("NonExistentThemeName!@#$%^&*()")]
	[InlineData("Steamed Oat Latte'; DROP TABLE Themes; --")]
	[InlineData("null")]
	[InlineData("undefined")]
	[InlineData("0")]
	public void ApplyTheme_AdversarialStrings_FallbacksGracefullyToEspressoCremaDark(string? adversarialInput)
	{
		var merged = new ObservableCollection<ResourceDictionary>
		{
			new() { { "Colors", "stub" } },
			new CoffeeLightTheme(),
			new() { { "Styles", "stub" } }
		};

		var exception = Record.Exception(() => ThemeManager.ApplyTheme(adversarialInput, merged));
		Assert.Null(exception);

		Assert.Equal("Espresso Crema Dark", ThemeManager.CurrentThemeName);
		Assert.IsType<CoffeeDarkTheme>(merged[1]);
		Assert.Equal(3, merged.Count);
	}

	[Fact]
	public void ApplyTheme_ExtremelyLargeString_FallbacksGracefullyWithoutThrowing()
	{
		string massiveGarbage = new('X', 50_000);
		var merged = new ObservableCollection<ResourceDictionary>
		{
			new() { { "Colors", "stub" } },
			new CoffeeLightTheme(),
			new() { { "Styles", "stub" } }
		};

		var exception = Record.Exception(() => ThemeManager.ApplyTheme(massiveGarbage, merged));
		Assert.Null(exception);

		Assert.Equal("Espresso Crema Dark", ThemeManager.CurrentThemeName);
		Assert.IsType<CoffeeDarkTheme>(merged[1]);
	}

	[Fact]
	public void ApplyTheme_NullMergedDictionaries_HeadlessSafety_DoesNotThrow()
	{
		var exception = Record.Exception(() => ThemeManager.ApplyTheme("Steamed Oat Latte", null));
		Assert.Null(exception);
		Assert.Equal("Steamed Oat Latte", ThemeManager.CurrentThemeName);

		exception = Record.Exception(() => ThemeManager.ApplyTheme(null, null));
		Assert.Null(exception);
		Assert.Equal("Espresso Crema Dark", ThemeManager.CurrentThemeName);
	}

	// =========================================================================
	// 3. Rapid Loop Switching Stress Testing (Stability & Boundary Invariants)
	// =========================================================================

	[Fact]
	public void ApplyTheme_RapidSequentialLoop_1000Iterations_MaintainsExactDictionaryIndex()
	{
		var colors = new ResourceDictionary { { "ColorsStub", true } };
		var styles = new ResourceDictionary { { "StylesStub", true } };
		var initialTheme = new CoffeeDarkTheme();

		var merged = new ObservableCollection<ResourceDictionary>
		{
			colors,       // Index 0
			initialTheme, // Index 1
			styles        // Index 2
		};

		string[] allThemes =
		[
			"Espresso Crema Dark",
			"Steamed Oat Latte",
			"Caramel Macchiato",
			"Mocha Velvet Dark",
			"Iced Matcha Latte",
			"Dirty Matcha Dark",
			"Honey Lavender Latte",
			"Midnight Cold Brew",
			"Sweet Mint Cortado",
			"Dark Mint Espresso"
		];

		for (int i = 0; i < 1_000; i++)
		{
			string targetTheme = allThemes[i % allThemes.Length];
			ThemeManager.ApplyTheme(targetTheme, merged);

			Assert.Equal(3, merged.Count);
			Assert.Same(colors, merged[0]);
			Assert.True(ThemeManager.IsThemeDictionary(merged[1]));
			Assert.Same(styles, merged[2]);
			Assert.Equal(targetTheme, ThemeManager.CurrentThemeName);
		}

		Assert.Equal(3, merged.Count);
		Assert.Same(colors, merged[0]);
		Assert.Same(styles, merged[2]);
	}

	[Fact]
	public async Task ApplyTheme_ConcurrentExecution_StressTest()
	{
		// Test concurrent invocations of ApplyTheme to check static state stability under concurrent callers
		string[] allThemes =
		[
			"Espresso Crema Dark",
			"Steamed Oat Latte",
			"Caramel Macchiato",
			"Mocha Velvet Dark",
			"Iced Matcha Latte",
			"Dirty Matcha Dark",
			"Honey Lavender Latte",
			"Midnight Cold Brew",
			"Sweet Mint Cortado",
			"Dark Mint Espresso"
		];

		var tasks = Enumerable.Range(0, 100).Select(i => Task.Run(() =>
		{
			string theme = allThemes[i % allThemes.Length];
			ThemeManager.ApplyTheme(theme, null);
		})).ToArray();

		await Task.WhenAll(tasks);

		// Must end in a valid theme name
		Assert.Contains(ThemeManager.CurrentThemeName, allThemes);
	}

	// =========================================================================
	// 4. Varying Initial MergedDictionaries Topology & Ordering
	// =========================================================================

	[Fact]
	public void ApplyTheme_EmptyDictionariesList_ClampsToIndex0_AndReplacesCorrectly()
	{
		var merged = new ObservableCollection<ResourceDictionary>();

		// Initial application into empty list
		ThemeManager.ApplyTheme("Steamed Oat Latte", merged);

		Assert.Single(merged);
		Assert.IsType<CoffeeLightTheme>(merged[0]);
		Assert.Equal("Steamed Oat Latte", ThemeManager.CurrentThemeName);

		// Subsequent application replaces existing theme at index 0
		ThemeManager.ApplyTheme("Espresso Crema Dark", merged);

		Assert.Single(merged);
		Assert.IsType<CoffeeDarkTheme>(merged[0]);
		Assert.Equal("Espresso Crema Dark", ThemeManager.CurrentThemeName);
	}

	[Fact]
	public void ApplyTheme_SingleItemDictionariesList_InsertsAtIndex1()
	{
		var colors = new ResourceDictionary { { "Colors", true } };
		var merged = new ObservableCollection<ResourceDictionary> { colors };

		// Target index clamped to 1
		ThemeManager.ApplyTheme("Steamed Oat Latte", merged);

		Assert.Equal(2, merged.Count);
		Assert.Same(colors, merged[0]);
		Assert.IsType<CoffeeLightTheme>(merged[1]);

		// Second call replaces index 1
		ThemeManager.ApplyTheme("Espresso Crema Dark", merged);

		Assert.Equal(2, merged.Count);
		Assert.Same(colors, merged[0]);
		Assert.IsType<CoffeeDarkTheme>(merged[1]);
	}

	[Fact]
	public void ApplyTheme_CorruptedListWithDuplicateThemes_PurgesExtraneousThemes()
	{
		var colors = new ResourceDictionary { { "Colors", true } };
		var theme1 = new RegalEarthTheme();
		var styles = new ResourceDictionary { { "Styles", true } };
		var theme2 = new WineDesertTheme();
		var theme3 = new CoffeeLightTheme();

		var merged = new ObservableCollection<ResourceDictionary>
		{
			colors, // Index 0
			theme1, // Index 1
			styles, // Index 2
			theme2, // Index 3 (extraneous)
			theme3  // Index 4 (extraneous)
		};

		ThemeManager.ApplyTheme("Espresso Crema Dark", merged);

		// The engine replaces at index 1 and purges all extraneous themes with index > existingIndex
		Assert.Equal(3, merged.Count);
		Assert.Same(colors, merged[0]);
		Assert.IsType<CoffeeDarkTheme>(merged[1]);
		Assert.Same(styles, merged[2]);
		Assert.DoesNotContain(theme1, merged);
		Assert.DoesNotContain(theme2, merged);
		Assert.DoesNotContain(theme3, merged);
	}

	[Fact]
	public void ApplyTheme_ThemeInitiallyAtIndex0_PreservesIndex0Position()
	{
		var initialTheme = new RegalEarthTheme();
		var colors = new ResourceDictionary { { "Colors", true } };
		var styles = new ResourceDictionary { { "Styles", true } };

		var merged = new ObservableCollection<ResourceDictionary>
		{
			initialTheme, // Index 0
			colors,       // Index 1
			styles        // Index 2
		};

		ThemeManager.ApplyTheme("Steamed Oat Latte", merged);

		Assert.Equal(3, merged.Count);
		Assert.IsType<CoffeeLightTheme>(merged[0]);
		Assert.Same(colors, merged[1]);
		Assert.Same(styles, merged[2]);
	}

	[Fact]
	public void ApplyTheme_FiveNonThemeDictionaries_InsertsAtIndex1AndPreservesPosition()
	{
		var d0 = new ResourceDictionary { { "D0", true } };
		var d1 = new ResourceDictionary { { "D1", true } };
		var d2 = new ResourceDictionary { { "D2", true } };
		var d3 = new ResourceDictionary { { "D3", true } };
		var d4 = new ResourceDictionary { { "D4", true } };

		var merged = new ObservableCollection<ResourceDictionary> { d0, d1, d2, d3, d4 };

		// First call: inserts at clamped index 1
		ThemeManager.ApplyTheme("Espresso Crema Dark", merged);

		Assert.Equal(6, merged.Count);
		Assert.Same(d0, merged[0]);
		Assert.IsType<CoffeeDarkTheme>(merged[1]);
		Assert.Same(d1, merged[2]);

		// Second call: finds theme at index 1 and swaps in-place
		ThemeManager.ApplyTheme("Steamed Oat Latte", merged);

		Assert.Equal(6, merged.Count);
		Assert.Same(d0, merged[0]);
		Assert.IsType<CoffeeLightTheme>(merged[1]);
		Assert.Same(d1, merged[2]);
	}

	[Fact]
	public void ApplyTheme_ThemeAtEndOfList_PreservesEndPosition()
	{
		var colors = new ResourceDictionary { { "Colors", true } };
		var styles = new ResourceDictionary { { "Styles", true } };
		var initialTheme = new WineDesertTheme();

		var merged = new ObservableCollection<ResourceDictionary>
		{
			colors,       // Index 0
			styles,       // Index 1
			initialTheme  // Index 2
		};

		ThemeManager.ApplyTheme("Espresso Crema Dark", merged);

		Assert.Equal(3, merged.Count);
		Assert.Same(colors, merged[0]);
		Assert.Same(styles, merged[1]);
		Assert.IsType<CoffeeDarkTheme>(merged[2]);
	}

	[Fact]
	public void ApplyTheme_RapidAlternatingSwitching_10000Iterations_ZeroDrift()
	{
		var colors = new ResourceDictionary { { "Colors", true } };
		var styles = new ResourceDictionary { { "Styles", true } };
		var initialTheme = new RegalEarthTheme();

		var merged = new ObservableCollection<ResourceDictionary>
		{
			colors,
			initialTheme,
			styles
		};

		for (int i = 0; i < 10_000; i++)
		{
			string target = (i % 2 == 0) ? "Steamed Oat Latte" : "Espresso Crema Dark";
			ThemeManager.ApplyTheme(target, merged);
		}

		Assert.Equal(3, merged.Count);
		Assert.Same(colors, merged[0]);
		Assert.IsType<CoffeeDarkTheme>(merged[1]);
		Assert.Same(styles, merged[2]);
		Assert.Equal("Espresso Crema Dark", ThemeManager.CurrentThemeName);
	}

	[Fact]
	public void ApplyTheme_NonListICollection_HandlesRemovalAndAddition()
	{
		// Tests the fallback branch where dictionaries is ICollection<ResourceDictionary> but not IList
		var colors = new ResourceDictionary { { "Colors", true } };
		var theme1 = new RegalEarthTheme();
		var styles = new ResourceDictionary { { "Styles", true } };

		var collection = new CustomDictionaryCollection
		{
			colors,
			theme1,
			styles
		};

		ThemeManager.ApplyTheme("Steamed Oat Latte", collection);

		Assert.Equal(3, collection.Count);
		Assert.Contains(colors, collection);
		Assert.Contains(styles, collection);
		Assert.DoesNotContain(theme1, collection);
		Assert.Contains(collection, d => d is CoffeeLightTheme);
	}

	// =========================================================================
	// 5. Static Theme Pollution & Isolation
	// =========================================================================

	[Fact]
	public void StaticPollution_DemonstratesWhyResetToDefaultIsMandatory()
	{
		// Set theme to a non-default coffeehouse theme ("Honey Lavender Latte")
		ThemeManager.ApplyTheme("Honey Lavender Latte");
		Assert.Equal("Honey Lavender Latte", ThemeManager.CurrentThemeName);

		// If MainViewModel is initialized while theme is Honey Lavender Latte, it captures Honey Lavender Latte
		var vmPolluted = new MainViewModel(
			new FakeCoffeeDictionaryService(),
			new FakeOracleEngineService(),
			new FakeCaffeineMetabolismService(),
			new FakeDataPersistenceService(),
			new FakeAppPopupService(),
			new Fixtures.FakeSqliteDatabaseService(),
			new Fixtures.FakeStorageSyncService());

		Assert.Equal("Honey Lavender Latte", vmPolluted.CurrentThemeName);

		// Now reset to default ("Espresso Crema Dark")
		ThemeManager.ResetToDefault();
		Assert.Equal("Espresso Crema Dark", ThemeManager.CurrentThemeName);

		// Now a new MainViewModel correctly starts with Espresso Crema Dark
		var vmClean = new MainViewModel(
			new FakeCoffeeDictionaryService(),
			new FakeOracleEngineService(),
			new FakeCaffeineMetabolismService(),
			new FakeDataPersistenceService(),
			new FakeAppPopupService(),
			new Fixtures.FakeSqliteDatabaseService(),
			new Fixtures.FakeStorageSyncService());

		Assert.Equal("Espresso Crema Dark", vmClean.CurrentThemeName);
	}

	private sealed class CustomDictionaryCollection : ICollection<ResourceDictionary>
	{
		private readonly HashSet<ResourceDictionary> _set = [];

		public int Count => _set.Count;
		public bool IsReadOnly => false;

		public void Add(ResourceDictionary item) => _set.Add(item);
		public void Clear() => _set.Clear();
		public bool Contains(ResourceDictionary item) => _set.Contains(item);
		public void CopyTo(ResourceDictionary[] array, int arrayIndex) => _set.CopyTo(array, arrayIndex);
		public bool Remove(ResourceDictionary item) => _set.Remove(item);
		public IEnumerator<ResourceDictionary> GetEnumerator() => _set.GetEnumerator();
		IEnumerator IEnumerable.GetEnumerator() => _set.GetEnumerator();
	}
}
