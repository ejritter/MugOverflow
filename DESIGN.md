# Design System

This document serves as the aesthetic anchor for `/maui-impeccable-xaml` craft in MugOverflow on `.NET 11`.

## Core Vibe
Warm specialty coffeehouse craftsmanship blended with crisp pharmacokinetic precision. Rich roasted espresso dark mode by default (`"Espresso Crema Dark"`), expressive 60fps vector mascot personality, tactile one-tap chat chips, and high-contrast native surfaces free of generic AI dashboard clutter.

## Color Palettes & Pure Coffeehouse Drink Themes
All colors are resolved dynamically at Index 1 of `Application.Current.Resources.MergedDictionaries` using `{DynamicResource}` bindings exclusively. Legacy theme names remain supported as aliases in `ThemeManager`.

### 5 Coffeehouse Drink Flavor Pairs (10 Palettes)
1. **Espresso & Oat Latte** (`CoffeeDarkTheme` / `CoffeeLightTheme`):
   - `"Espresso Crema Dark"` (**Default** — `#1C1510` dark roast canvas, `#D4975A` crema primary, `#E8B878` golden foam accent)
   - `"Steamed Oat Latte"` (`#FBF8F2` oat milk canvas, `#5C3818` espresso primary, `#C4823F` caramel crema accent)
2. **Caramel Mocha** (`WineDesertTheme` / `WineDesertDarkTheme`):
   - `"Caramel Macchiato"` & `"Mocha Velvet Dark"`
3. **Ceremonial Matcha** (`ForestLavenderTheme` / `ForestLavenderDarkTheme`):
   - `"Iced Matcha Latte"` & `"Dirty Matcha Dark"`
4. **Lavender Cold Brew** (`RegalEarthTheme` / `RegalEarthDarkTheme`):
   - `"Honey Lavender Latte"` & `"Midnight Cold Brew"`
5. **Mint Cortado** (`PristineCleanTheme` / `PristineCleanDarkTheme`):
   - `"Sweet Mint Cortado"` & `"Dark Mint Espresso"`

### Standard Dynamic Keys (24 Validated WCAG AA/AAA Tokens)
- **Brand & Surface**: `ThemeName`, `PageBackgroundColor`, `NavigationBarColor`, `PrimaryColor`, `SecondaryColor`, `AccentColor`, `SurfaceColor`, `SurfaceAltColor`, `BorderColor`
- **Typography & Contrast**: `PrimaryTextColor`, `SecondaryTextColor`, `TertiaryTextColor`, `PrimaryButtonTextColor`, `AccentButtonTextColor`
- **Semantic & Elevation**: `SuccessColor`, `WarningColor`, `ErrorColor`, `InfoColor`, `DecorativeColor`, `TransparentColor`, `PrimaryGradientStart`, `PrimaryGradientEnd`, `CardShadowColor`, `AppShadowColor`

## Animated Coffee Mug Mascot (`CoffeeMascotView`)
- **Vector Rendering**: Custom `IDrawable` (`CoffeeMascotDrawable`) rendered natively via `Microsoft.Maui.Graphics`.
- **Hero Espresso Stage**: Full ceramic mug & saucer with dynamic espresso fill (`ActiveCaffeineMg`), sloshing sine-wave crema foam, Bezier rising steam wisps, natural eye blinks, and 7 expressive `MascotMood` states (`Sleepy`, `Happy`, `Wired`, `Brewing`, `CelebratingYes`, `SternNo`, `SplashSkip`).
- **Mini-Avatar Mode**: High-contrast `38x38` compact mug avatar alongside Barista chat bubbles and brewing indicators.

## Typography Scale
Pure native MAUI `Label` with semantic styles:
- **Display / App Header**: `FontSize="22"`, `FontAttributes="Bold"`, `LineBreakMode="TailTruncation"`
- **Section Title / Modal Header**: `FontSize="18"`, `FontAttributes="Bold"`
- **Card Title /Mascot Mood Header**: `FontSize="17"`, `FontAttributes="Bold"`
- **Metric Highlight Number**: `FontSize="26"`, `FontAttributes="Bold"`
- **Body / Chat Message**: `FontSize="14"`, standard weight, high readability
- **Subtitle / Small Label**: `FontSize="13"`, muted text color
- **Micro / Caption**: `FontSize="11"`, metadata, roast badges & timestamps

## Structural, Motion & Responsive Rules
- **Side-by-Side Mascot & Chatbot Stage (`MainPage`)**:
  - **Wide Screens & Desktop ($\ge 900\text{dp}$)**: 2-column workspace (`320, *`), pairing Mugsy's Espresso Stage & live buzz mini-grid on the left with the back-and-forth Chatbot Stream & Tap-Chip Dock on the right.
  - **Compact Screens ($< 900\text{dp}$)**: Dynamically adapts into a 2-row (`Auto, *`) vertical flow without clipping.
  - **Slide-Over Coffeehouse Drawer**: Smooth `CubicOut` backdrop + `TranslationX` overlay replacing rigid permanent sidebars.
- **Unified Roast Kinetics Bento Stage (`MetabolismPage`)**: Single-column Coffeehouse header bar + scrollable KPI Bento stage, 24h Elimination Curve `GraphicsView`, 2x2 Alignment Matrix, and searchable/paginated Intake Ledger.
- **Pure Native Controls & 44dp Touch Targets**: Exclusively `<Border>` with `RoundRectangle` shapes, `FluentIcons.Maui` (`Symbol="Settings"` gear on theme triggers), `VisualStateManager` hover/press feedback, and `"AI engineered by EJRitter Development"` attribution.
