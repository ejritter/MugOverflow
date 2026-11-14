# Product Context

This document grounds the AI and the design system in the core goals of the product.

**Brand / Product Name**: MugOverflow - Specialty Barista Chatbot & Caffeine Tracker  
**Platform**: Native `.NET 11` MAUI (`net11.0-windows10.0.19041.0`, `net11.0-android`, `net11.0-ios`, `net11.0-maccatalyst`)  
**Surface Type**: Product (Interactive Coffeehouse Barista Chatbot, Animated Coffee Mug Mascot & Pharmacokinetic Roast Ledger)  
**Target Audience**: Developers, software engineers, night-shift coders, and specialty coffee enthusiasts seeking unbiased coffee intake counsel, warm coffeehouse character, pharmacokinetic half-life clearance modeling, and safe sleep window protection.

## Core Workflows
1. **Tap-Chip Consultation with Mugsy the Barista**: Pick any drink from the House Coffee & Caffeine Menu, tap a one-touch coffeehouse prompt chip (zero keyboard typing required), watch Mugsy pull a fresh shot at 60fps, receive an impartial 50/50 Barista verdict drawn from 100 crafted responses, and reply via tactile quick-reply chat bubbles (`Brewed & drank` vs `Skipped this round`).
2. **Playful Animated Coffee Mug Mascot (`CoffeeMascotView`)**: Native `Microsoft.Maui.Graphics` vector mascot with sloshing espresso fill driven by active bloodstream caffeine (mg), rising Bezier steam wisps, eye blinks, micro-jitter when over-caffeinated, and celebratory/protective reaction bursts—rendered both on the Hero Espresso Stage and as compact mini-avatars beside Barista chat bubbles.
3. **Session Lifecycle, Slide-Over Drawer & Daily Auto-Archival**: Browse or switch past brew sessions in the slide-over Coffeehouse Drawer while keeping all logged drinks and verdicts preserved indefinitely in local SQLite storage (with optional cloud sync).
4. **Track Pharmacokinetic Caffeine Elimination**: Monitor active caffeine in the body using the clinical standard 5-hour exponential half-life model ($N(t) = \sum N_i \cdot (0.5)^{(t - t_i)/5}$), total daily consumption, and countdowns to the $<20\text{mg}$ safe sleep threshold and $<1\text{mg}$ full clearance.
5. **24-Hour Elimination Curve & Intake Ledger**: Render interactive 24-hour decay curves with safe sleep threshold lines on a native MAUI `GraphicsView`, alongside a searchable, filterable, paginated Intake Ledger and 2x2 Barista Alignment Matrix.
6. **Pure Coffeehouse Drink Runtime Theming**: Defaults to `"Espresso Crema Dark"` on launch and switches instantaneously across 5 specialty coffeehouse drink pairs (10 WCAG AA/AAA light & dark palettes) without restarting the app:
   - **Espresso & Oat Latte**: `"Espresso Crema Dark"` (Default) & `"Steamed Oat Latte"`
   - **Caramel Mocha**: `"Caramel Macchiato"` & `"Mocha Velvet Dark"`
   - **Ceremonial Matcha**: `"Iced Matcha Latte"` & `"Dirty Matcha Dark"`
   - **Lavender Cold Brew**: `"Honey Lavender Latte"` & `"Midnight Cold Brew"`
   - **Mint Cortado**: `"Sweet Mint Cortado"` & `"Dark Mint Espresso"`

## Non-Goals
- **No Web Views, JavaScript, or Tailwind**: Pure native `.NET 11` MAUI XAML and `Microsoft.Maui.Graphics` vector components exclusively.
- **No Mandatory Cloud Account or External Telemetry**: 100% offline-first local SQLite & preference persistence with optional user-initiated cloud sync.
- **No Keyboard Form Friction in Chat**: Tap-chip prompt dock and one-tap conversational reply pills keep every Barista interaction effortless.
- **No Invasive Biometric Overrides**: Adheres strictly to clinical baseline kinetics (5.0-hour half-life) without multi-slider clutter.
