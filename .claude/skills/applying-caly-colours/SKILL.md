---
name: applying-caly-colours
description: Use when adding or restyling any Caly UI — a new view, control, toolbar, flyout, popup or overlay; setting Background, Foreground, BorderBrush, Fill or Stroke; using a Fluent control Caly has not used before (Slider, ProgressBar, ToggleSwitch, ...); editing palette.json, App.axaml palettes or CalyTheme.axaml; or when something "doesn't match the app", is "barely visible", "too dark/light" or low contrast in Light or Dark.
---

# Applying Caly's colours

## Overview

Caly's colours are a designed system with written rules and an automated audit. UI built without reading them comes out looking foreign or unreadable, and the audit cannot catch a brush it is not told about.

**Authoritative:** `tools/Palette/GUIDELINES.md`. Read it before choosing any colour. Background: `tools/Palette/README.md`, `docs/ui-review/2026-09-19-ui-ux-review.md`.

## Before choosing a colour

1. Find the role in GUIDELINES §2 and §4 (role table) and use that palette key. Surfaces, panels and toolbars are `RegionColor` (`SystemRegionBrush`). `AltHigh` is the input-field colour (pure black in Dark), never a surface.
2. Use the colour's brush resource (`SystemRegionBrush`, `SystemControl…Brush`) with `DynamicResource`. No hex, no named colours, no `Opacity` (S8, S9, S10).
3. Measure contrast against the background the element actually sits on. Flyouts and popups are Fluent's `ChromeMediumLow`, not `RegionColor`.

## Fluent controls new to Caly

Fluent paints parts of many controls in `Accent` (slider thumb and fill, toggle "on", progress fill), and Caly's Dark accent is close to 1:1 on `ChromeMediumLow`. For each control type Caly has not used before:

1. Read its resource keys in the Fluent theme (`C:\Users\Bob\source\repos\Avalonia\src\Avalonia.Themes.Fluent\Controls\<Control>.xaml` and `Accents\FluentControlResources.xaml`).
2. Override what fails in `Caly.Core/CalyTheme.axaml`, for the whole app, in the existing form: `<SolidColorBrush x:Key="…" Color="{DynamicResource System<Key>Color}" />`. Sliders are the worked example.
3. Keep only layout choices of one panel local (spacing, sizes).
4. Do not force a control's height: Fluent templates have fixed rows (the slider needs 50 px), and a smaller height clips the template.

## Before calling it done

All of these, every time:

- Add the new brushes to GUIDELINES §4 role table, and a contrast row to §6.
- Add a check to `tools/Palette/AuditPalette.cs` naming the **bound brush keys** (it resolves them through `CalyTheme.axaml`) on every background they appear on.
- `dotnet run --file tools/Palette/GeneratePalette.cs --check` and `dotnet run --file tools/Palette/AuditPalette.cs` both exit 0; `-- --all` prints the ratios to report.
- Look at it running, in **Light and Dark**. The theme is `"Theme": "Light"` in `%LOCALAPPDATA%\Caly\caly_settings` (absent means Dark). Back that file up first and restore it after. For 3D views, follow `verifying-3d-changes` for launching Caly.

## Common mistakes

| Mistake | Result |
|---|---|
| Panel or toolbar on `AltHigh` | Black box in Dark, white in Light, unlike every other surface |
| Fluent default kept on a new control | Accent parts vanish on flyouts in Dark |
| Contrast checked on `RegionColor` only | Passes the audit, unreadable in a flyout |
| Override only inside one view | The next view repeats the bug |
| Audit check on palette names, not brush keys | Passes even after someone rebinds the brush |
| Fixed `Height` on a templated control | Thumb or glyph cut in half |
