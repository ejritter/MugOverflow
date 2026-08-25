# Design System

This document serves as the aesthetic anchor for `/maui-impeccable-xaml` commands in MugOverflow.

## Core Vibe
Refined, editorial, pharmacokinetic precision blended with conversational warmth. A clean developer-first aesthetic that feels like a premium native tool—confident, high-contrast, tactile, and strictly free of AI visual slop.

## Color Palettes & Runtime Themes
All colors are resolved dynamically through theme resource dictionaries (`RegalEarthTheme`, `WineDesertTheme`, `ForestLavenderTheme`, `PristineCleanTheme`) using `{DynamicResource}` bindings.

### Standard Dynamic Keys
- **PrimaryColor**: Main brand action and emphasis accent (`#4B1D6B` Regal, `#722F37` Wine, `#1B4332` Forest, `#004D40` Pristine).
- **SecondaryColor**: Supporting accent and badge highlight (`#C89B3C` Gold, `#C28B5B` Tan, `#7E57C2` Lavender, `#00897B` Seafoam).
- **PageBackgroundColor**: Base canvas layer (`#FBF8F2` Light / `#1C1510` Dark).
- **SurfaceColor**: Primary card container background (`#FFFFFF` Light / `#251E17` Dark).
- **SurfaceAltColor**: Secondary container and tab pill background (`#F2EBE1` Light / `#322920` Dark).
- **BorderColor**: Subtle structural division border (`#E5DCB8` Light / `#44372B` Dark).
- **PrimaryTextColor**: High-contrast text (`#1C1510` Light / `#FBF8F2` Dark).
- **SecondaryTextColor**: Muted metadata and subtext (`#7A6A58` Light / `#C4B5A5` Dark).
- **SuccessColor**: Positive / Compliance accent (`#2E7D32` Green).
- **WarningColor**: Caution / Threshold line accent (`#D97706` Amber).
- **ErrorColor**: Rebellious / Alert state accent (`#C62828` Red).
- **InfoColor**: Oracle prompt badge / neutral info (`#0288D1` Blue).

## Typography Scale
Pure native MAUI `Label` with strict semantic styles:
- **Display / App Header**: `FontSize="22"`, `FontAttributes="Bold"`, `LineBreakMode="TailTruncation"`
- **Section Title / Large Header**: `FontSize="18"`, `FontAttributes="Bold"`
- **Card Title / Metric Header**: `FontSize="14"`, `FontAttributes="Bold"`
- **Metric Highlight Number**: `FontSize="28"`, `FontAttributes="Bold"`, high visual weight
- **Body / Chat Message**: `FontSize="14"`, standard weight, high readability
- **Subtitle / Small Label**: `FontSize="13"`, muted text color
- **Micro / Caption**: `FontSize="11"`, uppercase tracking, metadata & timestamps

## Structural & Responsive Rules
- **Flat Over Nested**: Avoid deeply nested stack layouts; use single-level multi-row/multi-column `<Grid>` architectures.
- **Strict 4dp/8dp Rhythm**: Margins, paddings, and row spacings strictly use values from the standard scale: `4`, `8`, `12`, `16`, `20`, `24`, `32`.
- **Responsive Dual-Pane Architecture**:
  - **Wide Screens & Desktop ($\ge 900\text{dp}$)**: Renders side-by-side 2-column grid (`50% / 50%` or `1* / 1*`), presenting the Oracle Stream and Metabolism Dashboard simultaneously.
  - **Mobile & Compact Screens ($< 900\text{dp}$)**: Automatically collapses into the single-column tabbed switcher ("✨ Java Oracle" / "📊 Metabolism & Stats").
- **Pure Native Controls**: Exclusively `<Border>` with `RoundRectangle` corner radii (8, 12, 16, 20). Zero legacy `<Frame>` elements.
- **Minimum Touch Targets**: All interactive elements maintain a minimum target area of `44dp` (`HeightRequest="44"` or larger).
- **Tactile Micro-Interactions**:
  - Implement `VisualStateManager` on buttons and cards.
  - Apply subtle scale down (`Scale="0.98"`) on `Pressed` state.
  - Apply soft border highlight on `PointerOver` state.
  - Reject all dated bounce or elastic animations in favor of smooth, instantaneous tactile responses.
- **Zero Third-Party UI Kits**: Implement all component patterns using native .NET MAUI controls and `FluentIcons.Maui`.
