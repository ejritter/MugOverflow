# Product Context

This document grounds the AI and the design system in the core goals of the product.

**Brand / Product Name**: MugOverflow - Java Oracle & Caffeine Tracker  
**Surface Type**: Product (Interactive Decision Oracle & Pharmacokinetic Metabolism Dashboard)  
**Target Audience**: Developers, software engineers, night-shift coders, and caffeine enthusiasts seeking unbiased coffee intake counsel, pharmacokinetic half-life clearance modeling, and safe sleep window protection.

## Core Workflows
1. **Consult the Pure Stochastic Java Oracle**: Ask whether to drink coffee; receive an impartial 50/50 coin-flip decision drawn from 100 crafted responses (50 nuanced YES / 50 strategic NO) and record compliance (Drank vs Skipped).
2. **Session Lifecycle & Daily Auto-Archival**: Each new day starts with a clean, focused Oracle chat stream, while all previous questions, drinks, and verdicts are preserved indefinitely in the Metabolism & Intake History ledger.
3. **Track Pharmacokinetic Caffeine Elimination**: Monitor active caffeine in the body using the clinical standard 5-hour exponential half-life model ($N(t) = \sum N_i \cdot (0.5)^{(t - t_i)/5}$), total daily consumption, and countdowns to the $<20\text{mg}$ safe sleep threshold and $<1\text{mg}$ full clearance.
4. **24-Hour Elimination Curve Visualization**: Render interactive decay curves with active point indicators and safe sleep threshold lines on a native MAUI `GraphicsView`.
5. **Coffee Reference & Intake Log Management**: Access 10 USDA/Mayo Clinic reference coffees, log intake with exact mg caffeine, edit or delete past logs, and review the 2x2 compliance matrix (Yes•Drank, No•Skipped, Yes•Skipped, No•Drank).
6. **Responsive Dual-Pane Experience**: Automatically present a simultaneous side-by-side view (Oracle Chat on the left, Metabolism Dashboard on the right) on wide screens and desktop viewports ($\ge 900\text{dp}$), collapsing smoothly into a seamless top-tab switcher on mobile.
7. **Instant Runtime Theming**: Switch between Regal Earth, Wine Desert, Forest Lavender, and Pristine Clean themes without restarting the app.

## Non-Goals
- **No Web Views, JavaScript, or Tailwind**: Pure native MAUI XAML components exclusively.
- **No Mandatory Cloud Account or Tracking**: 100% offline-first local data persistence with zero external telemetry.
- **No Invasive Biometric Overrides**: Adheres strictly to standard clinical baseline kinetics (5.0-hour half-life) without confusing multi-slider friction.
- **No Cluttered E-commerce Slop**: Laser-focused on coffee decision-making and pharmacokinetic tracking.

## Competitors & Anti-References
- **Generic Habit Trackers**: Flat checklists that treat a 215mg Nitro Cold Brew the same as a 63mg Single Espresso. MugOverflow incorporates precise dosage and elimination curves.
- **Overly Complex Medical Tools**: Cluttered data-entry forms with high cognitive load. MugOverflow keeps the primary workflow conversational, fast, and visually rewarding.
