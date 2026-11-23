# Colour palette guidelines

Rules marked with an id (S1, T1, C1, ...) are enforced by `AuditPalette.cs`. Rules without an id are manual.

## 1. Files and workflow

| File | Role |
|---|---|
| `tools/Palette/palette.json` | The only place a theme's colours are set |
| `tools/Palette/GeneratePalette.cs` | Generates the two `ColorPaletteResources` in `Caly.Core/App.axaml` |
| `Caly.Core/CalyTheme.axaml` | Binds Caly and Tabalonia brushes, and the Fluent brushes Fluent 2 lacks, to palette keys |
| `tools/Palette/AuditPalette.cs` | Checks every rule with an id |

Caly uses Fluent 2 (`Fluent2Theme`). Fluent 2 derives its main tokens from the palette (`ColorPaletteResources.s_tokenDerivations`): text from `BaseHigh`, `BaseMediumHigh`, `BaseMedium`; control fills (buttons, drop-downs, text boxes) from `BaseLow` at the token's alpha; window and page backgrounds from `RegionColor`; the critical colour from `ErrorText`; the accent fills from `Accent` (HSL lightness −28.5/255 in Light, +70/255 in Dark). Its other tokens (flyout, subtle, strong and solid fills) are its own. Some Fluent 2 templates still read Fluent brushes from the palette (tooltips and list boxes `ChromeMediumLow`, list selection `Accent`).

- `AuditPalette.cs` holds the Fluent 2 token values and derivations Caly's colours meet. They MUST be re-read from Avalonia's `Avalonia.Themes.Fluent2` sources when Avalonia is updated.
- A Fluent 2 control key MUST be overridden in `CalyTheme.axaml` only when a check fails with Fluent 2's own value.

- MUST edit colours only in `palette.json`.
- MUST NOT edit the generated `<ColorPaletteResources>` attributes.
- MUST run before every commit that touches `palette.json`, `CalyTheme.axaml` or a colour in a view. All MUST exit 0:
  1. `dotnet run --file tools/Palette/GeneratePalette.cs`
  2. `dotnet run --file tools/Palette/GeneratePalette.cs --check`
  3. `dotnet run --file tools/Palette/AuditPalette.cs`
- MUST check the change in the running app, in Light and Dark, in these states: no document open, document open, window inactive, Settings pane open, input in error.
- MUST update this file whenever a rule changes.

## 2. Picks

Each theme MUST define all of: `accent`, `ink`, `surface`, `chrome`, `paper`, `error`.

| Pick | Palette key | Use |
|---|---|---|
| `accent` | `Accent` | Fills only. Fluent 2 derives its accent fills from it: list selection, slider, checked toggles, selected-tab pill, accent buttons |
| `ink` | `BaseHigh` | Primary text and icons |
| `surface` | `RegionColor` | Window, document view, selected tab, desk, toolbar |
| `chrome` | `ChromeMedium`, `ListLow` | Fluent brushes only; Fluent 2 controls use their own fills |
| `paper` | `AltHigh` | Add-tab hover glyph, system bar; Fluent 2 inputs use their own fill |
| `error` | `ErrorText` | Invalid-input border, error text |

- Light and Dark MUST NOT share pick values.
- `ink` MUST be the darkest of the picks in Light and the lightest in Dark.
- `paper` MUST be the opposite pole of `ink`.
- `surface` MUST be lightness-wise between `ink` and `paper`.
- `accent` MUST NOT be used as text, icon or border colour.
- A new semantic colour (success, warning, information) MUST be added as a pick in both themes, with its own checks in `AuditPalette.cs` (rule 7) before it is used.
- Hue MUST NOT carry meaning that lightness or shape does not also carry.

## 3. Ramps

- Ramps MUST be set per theme in `palette.json`.
- S5: the Base ramp MUST be strictly ordered by contrast against `RegionColor`: `BaseHigh` > `BaseMediumHigh` > `BaseMedium` > `BaseMediumLow` > `BaseLow`.
- S4: `ChromeLow` MUST be darker than `RegionColor` in both themes.
- S3: `ChromeLow` MUST be at least 8.0 L* below `RegionColor`.
- S6: these aliases MUST hold in both themes: `ChromeAltLow` = `BaseMediumHigh`; `ChromeDisabledHigh`, `ChromeHigh`, `ListMedium` = `BaseLow`; `ChromeDisabledLow` = `BaseMediumLow`; `ListLow` = `ChromeMedium`.
- `ChromeGray` is the tab hover shade. It MUST be generated from the `tabHover` ramp (position between `ChromeLow` and `RegionColor`), MUST NOT be an alias, and MUST NOT be used for anything else.
- S12: `ChromeGray` MUST differ from `ChromeLow` and from `RegionColor` by at least 3.0 L*.
- S7: `ErrorText` MUST be set in both themes.
- `ChromeWhite` MUST be white and `ChromeBlackHigh` black; `ChromeBlackMedium`, `ChromeBlackMediumLow`, `ChromeBlackLow` MUST be black at 80%, 40%, 20%; the `Alt*` family MUST be `paper` at 100%, 80%, 60%, 40%, 20%. The generator writes these; MUST NOT be overridden.

## 4. Bindings in `CalyTheme.axaml`

- S1: MUST NOT contain per-theme dictionaries.
- MUST bind with `Color="{DynamicResource System<Key>Color}"`. MUST NOT contain hex or named colours.
- S2: `SelectedTabItemBackgroundBrush`, `CalyDeskBrush` and the window background (Fluent 2's `SolidBackgroundFillColorBase`, derived from `RegionColor`) MUST all be `RegionColor`.
- Role bindings:

| Role | Palette key |
|---|---|
| Tab strip, unselected tab (active and inactive window), navigation pane | `ChromeLow` |
| Selected tab, desk, window, panels (`SystemRegionBrush`) | `RegionColor` |
| Hovered unselected tab (active and inactive window) (S12) | `ChromeGray` |
| Tab separator | `BaseLow` |
| Tab title: unselected (active and inactive window) | `BaseMedium` |
| Tab title: hover, selected hover | `BaseMediumHigh` |
| Tab title: selected | Fluent 2 `TextFillColorPrimary` (`BaseHigh`, derived; not bound) |
| Borders and separators (`SystemControlForegroundBaseMediumLowBrush`) | `BaseMediumLow` |
| Scroll button: glyph | `BaseMediumHigh` |
| Scroll button: hover glyph | `BaseHigh` |
| Scroll button: inactive-window glyph | `BaseMediumLow` |
| Scroll button: disabled glyph | `BaseLow` |
| Scroll button: hover fill | `BaseLow` |
| Scroll button: pressed fill | `RegionColor` |
| Add-tab button: hover glyph | `AltHigh` |
| Add-tab button: hover fill | `BaseLow` |
| Add-tab button: pressed fill | `BaseMedium` |
| Add-tab button: inactive-window glyph (rest and hover) | `BaseMediumHigh` |
| Close button: glyph | `BaseHigh` |
| Close button: hover glyph, inactive-window glyph | `BaseMediumLow` |
| Close button: inactive-window hover glyph | `BaseMediumHigh` |
| Close button: pressed circle | `ChromeMediumLow` |
| 3D toolbar fill (`Artwork3DView.axaml`) | `RegionColor` |
| Slider empty track: rest, hover, pressed (the thumb and filled track are Fluent 2's accent fill) | `BaseLow` |

- `DocumentsTabsControl.axaml` MUST NOT set `Background` on the `TabsControl`.
- The `TabsControl` content row MUST be painted `RegionColor` by the style in `DocumentsTabsControl.axaml`.
- The splash screen MUST be declared after `DocumentsTabsControl` in `MainView.axaml`.
- New brushes MUST be added to the role table above.

## 5. Views and code

- S13: the invalid page-number state MUST also be shown by a 2px border and italic text (`DocumentTabView.axaml`).
- An error or invalid state MUST NOT be shown by colour alone. It MUST add a non-colour cue: border thickness, text style, icon or text.
- S9: MUST NOT use literal colours (hex or named) in `.axaml`. Exception: `PageItem.axaml` (white page).
- S8: MUST NOT use the `Opacity` attribute in `.axaml`. Use a palette brush.
- S10: MUST assign brush resources (`…Brush`) to `Foreground`, `Background`, `Fill`, `Stroke`, `BorderBrush`. MUST NOT assign `…Color` resources.
- S11: MUST NOT use `SystemControlForegroundBaseMediumLowBrush` or `SystemControlForegroundBaseLowBrush` as a `Foreground`.
- Text colours allowed: `BaseHigh`, `BaseMediumHigh`, `BaseMedium`; Fluent 2's `TextOnAccentFillColorPrimary` on its accent fill; `ErrorText` on `RegionColor` only.
- Theme-dependent drawing in code MUST read brushes from resources. MUST NOT use colour literals.
- Colour literals in code are allowed only for drawing on the white PDF page (`SelectionColor`, `SearchColor`) and MUST pass C3.
- Debug-only colours MUST be inside `#if DEBUG`.

## 6. Contrast (WCAG 2.1)

Ratios are measured in both themes.

"Flyout fill" is Fluent 2's `AcrylicInAppFillColorDefault` (flyouts, menus, the expanded scrollbar track). "Accent fill" is Fluent 2's `AccentFillColorDefault`, derived from `Accent` (§1).

| Id | Foreground on background | Minimum |
|---|---|---|
| T1 | `BaseHigh` on `RegionColor`, `ChromeLow`, `ChromeMediumLow` (tooltips), the flyout fill | 4.5 |
| T2 | `BaseMediumHigh` on `RegionColor`, `ChromeLow`, the flyout fill | 4.5 |
| T3 | `BaseMedium` on `RegionColor`, `ChromeLow`, the flyout fill | 4.5 |
| T4 | Fluent 2 `TextOnAccentFillColorPrimary` on the accent fill (accent buttons) | 4.5 |
| T5 | `BaseHigh` on Fluent 2's button fill (`ControlFillColorDefault`: `BaseLow` at 70% in Light, 6% in Dark, over `RegionColor`) | 4.5 |
| T6 | `BaseHigh` on Fluent 2's hover fill (`SubtleFillColorSecondary` over `RegionColor` and over the flyout fill) | 4.5 |
| T7 | `ErrorText` on `RegionColor` | 4.5 |
| G1 | `BaseMediumLow` on `RegionColor`, `ChromeLow` | 3.0 |
| G2 | `BaseMedium` on `RegionColor` (control borders) | 3.0 |
| G3 | `ErrorText` on `RegionColor` (error border) | 3.0 |
| G6 | Tab titles: selected, selected hover, unselected, inactive, hover, inactive hover | 4.5 |
| G6 | Close, scroll and add-tab glyphs, every state except disabled, on their background | 3.0 |
| G7 | Slider (Fluent 2): accent dot on its disc in every state; filled track (accent fill) on `RegionColor` and the flyout fill; empty track 1.5; filled vs empty track 8.0 L* | 3.0 |
| G8 | Selected-tab pill (accent fill) on `ChromeLow`, `RegionColor` | 3.0 |
| D1 | Tab separator on the strip | 1.5 |
| D3 | Disabled scroll glyph on the strip | 1.5 |
| D4 | Scrollbar thumb (Fluent 2 `ControlStrongFillColorDefault`, every state) on `RegionColor` and on its expanded track (flyout fill) | 1.5 |

- Every foreground, in every state (rest, hover, pressed, inactive window, disabled), MUST reach at least 1.5:1 against its background.
- Body text MUST use T1-T3 colours. Text MUST NOT be faded with opacity.
- Focus indicators MUST remain visible in both themes.

## 7. Colour blindness

Simulations: protanopia, deuteranopia, tritanopia (Machado et al. 2009, severity 1.0). ΔE is CIEDE2000.

| Id | Check | Minimum |
|---|---|---|
| C1 | `ErrorText` vs `BaseMedium` (error border vs normal control border), each simulation | ΔE 15 |
| C2 | `ErrorText` on `RegionColor`, each simulation | 3.0:1 |
| C3 | Selection overlay vs search overlay on white, each simulation | ΔE 15 |
| C4 | `BaseHigh`, `BaseMediumHigh`, `BaseMedium` on `RegionColor`, each simulation | 4.5:1 |
| C5 | `Accent` vs `RegionColor`, each simulation | ΔE 10 |

- MUST NOT distinguish two states by hue alone, in particular red against green or blue against yellow.
- States MUST be distinguished by at least one of: 3.0:1 contrast, an L* step of 8.0 or more, or a non-colour cue (shape, thickness, icon, text).
- A new semantic colour MUST have C1-C3 style checks against every other semantic colour and against `RegionColor`.

## 8. Open items

None.

An item added here MUST have an id (O1, O2, ...) and MUST NOT be widened.
