# Theme palette analysis

Analysis of the two `ColorPaletteResources` in `Caly.Core/App.axaml` (`Light` and `Dark`), aimed at
reducing them to a handful of colours that define the app identity. Written 2026-09-20.

Status: done. The palette was analysed, its four inconsistencies were standardised (see
[Standardisation](#standardisation-applied)), and the two `ColorPaletteResources` lines in
`App.axaml` are now **generated from 5 picks per theme** by `tools/Palette/GeneratePalette.cs`.
`CalyDeskBrush` and the `Tab*` brushes in `CalyTheme.axaml` reference that palette with identical
bindings in both themes, so `palette.json` is the only place a theme is set (see
[Caly brushes derived from the palette](#caly-brushes-derived-from-the-palette)).
Colour rules and constraints are in [GUIDELINES.md](GUIDELINES.md); `AuditPalette.cs` checks them.

## Using the generator

```
dotnet run --file tools/Palette/GeneratePalette.cs           rewrite App.axaml if it is out of date
dotnet run --file tools/Palette/GeneratePalette.cs --check   exit 1 if App.axaml is out of date, write nothing
```

Edit `tools/Palette/palette.json`, run the command, and rebuild. Do not edit the generated attributes
in `App.axaml` by hand: a comment above them says so, and `--check` fails if they drift from the
picks. When it rewrites the file it prints every attribute that changed (`Name old -> new`), and it
only touches the two `<ColorPaletteResources>` elements.

`palette.json` has, per theme (`Light`, `Dark`):

- **`picks`**: `accent`, `ink`, `surface`, `chrome`, `paper`, and an optional `error`. Colours are
  `#rrggbb`.
- **`ramps`**: seven numbers per theme (see [Blend ratios](#blend-ratios)).

The file allows `//` comments, which carry the same explanation of each pick.

## Summary

Each theme used to set 27 colours by hand (54 in total). Every `Base*` and `Chrome*` value sits on
the straight line between `RegionColor` and `BaseHigh`, with the same ratio in all three colour
channels, so these are computed shades, not independent choices.

Five picks per theme plus six ratios generate all of them: **Accent, Ink, Surface, Chrome, Paper**,
and optionally **Error**. Mathematically 4 are sufficient (Accent, Ink, Surface, Paper). Chrome is a
convenience for controlling the frame tint directly, and Error is optional: without it Fluent's stock
`ErrorText` applies. Both themes set it explicitly, to Fluent's stock values, so the error colour is
visible and editable in `palette.json`.

Light is purple-tinted and Dark is neutral grey, so the picks cannot be shared between themes: 5
colours per theme, 10 in total, instead of 54 values.

## Grouping

| Pick | Sets | Derived from it |
|---|---|---|
| **Accent** | `Accent` | Avalonia already derives `AccentDark1-3` and `AccentLight1-3` itself (`SystemAccentColors.CalculateAccentShades`) |
| **Ink** (text and icons) | `BaseHigh` | `BaseMediumHigh`, `BaseMedium`, `BaseMediumLow`, `BaseLow` are Surface-to-Ink blends. Aliases: `ChromeAltLow` = `BaseMediumHigh`; `ChromeDisabledLow` = `BaseMediumLow`; `ChromeDisabledHigh`, `ChromeHigh` and `ListMedium` = `BaseLow` |
| **Surface** (window background, document view, selected tab, desk behind the pages) | `RegionColor` | The anchor for every blend. `ChromeLow` and `ChromeMediumLow` are also Surface-to-Ink blends; `ChromeLow` is the frame shade (tab strip, unselected tabs, navigation pane) |
| **Chrome** (control and list-hover fills) | `ChromeMedium` | `ListLow` = `ChromeMedium` |
| **Paper** (input fields) | `AltHigh` | `AltMediumHigh`, `AltMedium`, `AltMediumLow`, `AltLow` are Paper at 80, 60, 40 and 20% alpha |
| **Error** (optional) | `ErrorText` | Omitted from the output unless picked. Drives the invalid-input border (`TextBox:error`, e.g. the page number box), the wrong-password message and the print status. Set to `#c50500` (Light) and `#fff000` (Dark), Fluent's stock values |

Fixed, no pick needed: `ChromeWhite` (white), `ChromeBlackHigh` (black), and `ChromeBlackMedium`,
`ChromeBlackMediumLow`, `ChromeBlackLow` (black at 80, 40 and 20% alpha). Same values in both
themes.

Count of the 27 values per theme: `Base` 5, `Chrome` 13, `Alt` 5, `List` 2, `Region` 1, `Accent` 1.

## Values in App.axaml

These are the generated values.

| Key | Light | Dark |
|---|---|---|
| `Accent` | `#636b8f` | `#565c77` |
| `AltHigh` | `#ffffff` | `#000000` |
| `AltMediumHigh` | `#ccffffff` | `#cc000000` |
| `AltMedium` | `#99ffffff` | `#99000000` |
| `AltMediumLow` | `#66ffffff` | `#66000000` |
| `AltLow` | `#33ffffff` | `#33000000` |
| `BaseHigh` | `#120821` | `#ffffff` |
| `BaseMediumHigh` | `#3a3247` | `#c6c6c6` |
| `BaseMedium` | `#4a4256` | `#b3b3b3` |
| `BaseMediumLow` | `#696274` | `#8c8c8c` |
| `BaseLow` | `#95909d` | `#666666` |
| `ChromeAltLow` | `#3a3247` | `#c6c6c6` |
| `ChromeBlackHigh` | `#000000` | `#000000` |
| `ChromeBlackMedium` | `#cc000000` | `#cc000000` |
| `ChromeBlackMediumLow` | `#66000000` | `#66000000` |
| `ChromeBlackLow` | `#33000000` | `#33000000` |
| `ChromeDisabledHigh` | `#95909d` | `#666666` |
| `ChromeDisabledLow` | `#696274` | `#8c8c8c` |
| `ChromeGray` | `#e0dee5` | `#2a2a2a` |
| `ChromeHigh` | `#95909d` | `#666666` |
| `ChromeLow` | `#d4d1d9` | `#212121` |
| `ChromeMedium` | `#dbd7e1` | `#6a6a6a` |
| `ChromeMediumLow` | `#e8e5ec` | `#585858` |
| `ChromeWhite` | `#ffffff` | `#ffffff` |
| `ListLow` | `#dbd7e1` | `#6a6a6a` |
| `ListMedium` | `#95909d` | `#666666` |
| `RegionColor` | `#eceaf0` | `#333333` |
| `ErrorText` | `#c50500` | `#fff000` |

## Blend ratios

`t` is the fraction of the way from Surface to Ink (0 = Surface, 1 = Ink, negative = the other side
of Surface). These are the `ramps` in `palette.json`, tuned so the generated values land as close as
possible to the original hand-tuned palette.

| Ramp | Light `t` | Dark `t` |
|---|---|---|
| `baseMediumHigh` | 0.815 | 0.72 |
| `baseMedium` | 0.745 | 0.625 |
| `baseMediumLow` | 0.6 | 0.435 |
| `baseLow` | 0.4 | 0.25 |
| `chromeLow` | 0.11 | -0.09 |
| `chromeMediumLow` | 0.02 | 0.18 |
| `tabHover` | 0.5 | 0.5 |

`tabHover` is measured differently: it is where the hovered-tab shade (`ChromeGray`) sits between
`ChromeLow` (0) and `RegionColor` (1). At 0.5 it is `#e0dee5` in Light and `#2a2a2a` in Dark.

The Base ramp is strictly ordered in both themes (`High` > `MediumHigh` > `Medium` > `MediumLow` >
`Low`). Ratios differ between themes, so they stay as per-theme constants rather than one shared
ramp.

## Round-trip accuracy

Compared with the hand-tuned palette that existed just before the generator was introduced (after
standardisation):

- **Dark:** identical, every value.
- **Light:** identical except four ink shades and the aliases that follow them. The Light values
  were hand-tuned, and their purple tint drifts slightly from a pure linear mix, so no single ratio
  reproduces all three channels.

| Light value (and aliases) | Hand-tuned | Generated | Max channel change |
|---|---|---|---|
| `BaseMediumHigh` (`ChromeAltLow`) | `#3a3049` | `#3a3247` | 2 |
| `BaseMedium` | `#4a4059` | `#4a4256` | 3 |
| `BaseMediumLow` (`ChromeDisabledLow`) | `#5d5470` | `#60596c` | 5 |
| `BaseLow` (`ChromeDisabledHigh`, `ChromeHigh`, `ListMedium`) | `#9990a6` | `#9994a1` | 5 |

The generated Light ink shades are slightly less purple than before. That is invisible in practice
but not bit-identical. If a specific Light shade matters, adjust the `ink` pick or its ramp.
Since then Light `baseMediumLow` and `baseLow` were retuned for contrast (see
[Accessibility review](#accessibility-review)), so the last two rows now generate `#696274` and
`#95909d`.

## Caly brushes derived from the palette

`Caly.Core/CalyTheme.axaml` defines brushes that are not Fluent palette keys: `CalyDeskBrush` (the
surface the pages sit on) and the keys that override Tabalonia's own colours (`Tab*`, plus the
scroll, add-tab and close-tab button keys, see [Tab buttons](#tab-buttons)). The generator
does not write them. Each is bound to a palette colour with
`Color="{DynamicResource System…Color}"`, and **the bindings are identical in Light and Dark**: the
file has no per-theme dictionaries, so nothing in XAML sets a theme. A theme is set only by its
picks and ramps in `palette.json`. Dark is the reference; Light was tuned to match it.

| Brush | Bound to | Role |
|---|---|---|
| `CalyDeskBrush` | `RegionColor` | the desk behind the pages, same as the view |
| `TabControlWindowActiveBackgroundBrush` | `ChromeLow` | tab strip (see below) |
| `TabItemBackgroundBrush` | `ChromeLow` | unselected tab |
| `SelectedTabItemBackgroundBrush` | `RegionColor` | selected tab, same as the document view so it flows into it |
| `TabItemHeaderBackgroundUnselectedPointerOver` | `ChromeGray` | hovered unselected tab, and the Fluent `TabItem` in the navigation pane (they share this key) |
| `TabControlWindowInactiveBackgroundBrush` | `ChromeLow` | tab strip while the window is inactive (Tabalonia does not use this key; bound for consistency) |
| `TabItemBackgroundBrushWindowInactive` | `ChromeLow` | unselected tab while the window is inactive |
| `TabItemHeaderBackgroundUnselectedPointerOverWindowInactive` | `ChromeGray` | hovered tab while the window is inactive |
| `TabItemRightSeparatorBackgroundBrush` | `BaseLow` | separator between tabs |

`ChromeLow` is the "frame" shade, a step darker than the surface. Fluent's `SplitView` also paints
its pane with it, so the navigation pane and the tab strip always match. Tune it with the
`chromeLow` ramp: it sets how much the selected tab (which is the surface) stands out against the
strip. Both themes are set to a step of about 8.5 perceived lightness (L\*), which is what Tabalonia's
original Dark strip had.

**How the strip is painted.** The strip is the tab control's own background, so
`DocumentsTabsControl.axaml` must not set a `Background` on the `TabsControl`. It used to set
`Transparent`, which overrode `TabControlWindowActiveBackgroundBrush`: the strip showed the window
colour, identical to the selected tab, and `chromeLow` only changed the unselected tabs. The tab
control's background also fills its content row, so a style in `DocumentsTabsControl.axaml` paints
that row (`PART_SelectedContentHost`) with `RegionColor`. That keeps the document area, and an
empty window, at the surface colour. The splash screen in `MainView.axaml` is declared after the
tab control so it draws on top of it: declared before it, the tab control's background hid the
splash text when no document was open.

**Hover.** A hovered unselected tab uses `ChromeGray`, the shade halfway between the strip and the
selected tab, so it differs from both. Fluent's `TabItem` in the navigation pane reads the same
hover key, so both kinds of tab hover alike.

**Invalid page number.** `DocumentTabView.axaml` adds a 2px border and italic text to the
page-number box in its error state, so the state is not shown by colour alone.

### Tab buttons

Tabalonia's scroll, add-tab and close-tab button colours are bound the same way (they are defined in
`external/Tabalonia/Tabalonia/Themes/Custom/Brushes.axaml`, with a different grey per theme). The
bindings keep each key's role: a mid-tone fill on hover with a contrasting glyph, a stronger step
when pressed, and dimmer glyphs when the window is inactive or the button is disabled. "Was" is
Tabalonia's Dark value, the reference. Dark values are shown as they are today.

| Button | State | Bound to | Dark was | Dark now |
|---|---|---|---|---|
| Scroll | glyph | `BaseMediumHigh` | `#cccccc` | `#c6c6c6` |
| Scroll | glyph, hover | `BaseHigh` | `#ffffff` | `#ffffff` |
| Scroll | glyph, window inactive | `BaseMediumLow` | `#666666` | `#8c8c8c` |
| Scroll | glyph, disabled | `BaseLow` | `#444444` | `#666666` |
| Scroll | fill, hover | `BaseLow` | `#464546` | `#666666` |
| Scroll | fill, pressed | `RegionColor` | `#333333` | `#333333` |
| Add tab | glyph, hover | `AltHigh` | `#151719` | `#000000` |
| Add tab | fill, hover | `BaseLow` | `#464546` | `#666666` |
| Add tab | fill, pressed | `BaseMedium` | `#333333` | `#b3b3b3` |
| Add tab | glyph, window inactive (rest and hover) | `BaseMediumHigh` | `#bfbfbf` / `#c9c9c9` | `#c6c6c6` |
| Close | glyph | `BaseHigh` | `#ffffff` | `#ffffff` |
| Close | glyph, hover | `BaseMediumLow` | `#1a1a1a` | `#8c8c8c` |
| Close | glyph, window inactive | `BaseMediumLow` | `#666666` | `#8c8c8c` |
| Close | glyph, window inactive, hover | `BaseMediumHigh` | `#b8b8b8` | `#c6c6c6` |
| Close | circle, pressed | `ChromeMediumLow` | none defined | `#585858` |

The add-tab hover glyph is Paper (`AltHigh`) on a `BaseLow` fill in both themes, as in Tabalonia:
dark on grey in Dark, white on grey in Light. The pressed fill is `BaseMedium` so that glyph stays
readable in both themes. The close button's hover glyph was nearly black in Dark
(`#1a1a1a` on a `#212121` tab), which made it disappear; it is now a dimmer ink shade.

`AddItemCommandButtonPointerOverBrush` is defined by Tabalonia for Light only and nothing uses it,
so it is not bound.

### What consumes the changed values

Searched across Avalonia's Fluent theme sources, `Caly.Core`, Tabalonia, TreeDataGrid and
AvaloniaProgressRing:

- `ChromeBlack*`, `ChromeHigh`: no consumer anywhere outside their own definitions.
  Changing them has no visible effect.
- `Alt*`: `AltHigh` drives the window transparency fallback and system bar colour (unchanged, still
  opaque). `AltMedium` drives the inner focus ring (`AdornerLayer`) and, with `AltMediumHigh`, the
  managed file chooser's hover and selection backgrounds (only shown when there is no native
  dialog). `AltMediumLow` drives `GridSplitter`'s background and `AltMediumHigh` the `Expander`
  header, and Caly uses neither control.
- `ChromeDisabledLow`: the disabled `TextBox` foreground and the scrollbar panning thumb.
- `BaseMedium` and `BaseMediumLow` have many consumers (secondary text, hint text, icons).

### Visible changes to expect

1. **Light:** anything using `BaseMedium` is now darker, and anything using `BaseMediumLow` is
   lighter. This includes the "Applies straight away." hint in `SettingsAppearanceView.axaml`,
   which is now lighter.
2. **Dark:** disabled `TextBox` text and the scrollbar panning thumb go from `#b3b3b3` to `#8c8c8c`.
3. **Both:** the inner focus ring is now 60% translucent instead of opaque.
4. **Light:** the four ink shades above are up to 5/255 less purple than before.
5. **Light:** the desk behind the pages is now `#eceaf0`, the same as the view, instead of `#d8d4de`.
6. **Light:** `chromeLow` went from 0.02 to 0.11 so the tab strip stays clearly darker than the
   selected tab (`#d4d1d9` against `#eceaf0`). The navigation pane uses the same shade, so it is
   now darker than before (`#e8e5ec`). In Dark the same ramp went from -0.03 to -0.09, so the strip
   is `#212121` instead of `#2d2d2d` and stands out from the selected tab (`#333333`); the
   navigation pane is `#212121` too.
7. **Both:** hovered tabs use the new `ChromeGray` shade (Light `#e0dee5`, Dark `#2a2a2a`), midway
   between the strip and the selected tab. They no longer match the selected tab.
8. **Both:** the strip behind and to the right of the tabs is now painted with the frame shade. It
    was transparent (the window colour), so the last tab had no contrast against it. The
    unselected tabs of an inactive window use the frame shade too, instead of the selected tab's
    colour.
9. **Dark:** hovered tabs while the window is inactive are `#2a2a2a` instead of Tabalonia's
   `#363636`.
10. **Dark:** the tab buttons move a little (see [Tab buttons](#tab-buttons)). The largest changes:
   the add-tab pressed fill (`#333333` to `#b3b3b3`), the scroll and add-tab hover fill
   (`#464546` to `#666666`), the disabled scroll glyph (`#444444` to `#585858`) and the close
   button's hover glyph (`#1a1a1a` to `#8c8c8c`).
11. **Light:** the tab buttons now use tinted shades instead of Tabalonia's neutral greys.
12. **Both:** the disabled scroll glyph is `BaseLow` (Dark `#666666`, Light `#95909d`), and the
    inactive-window scroll and close glyphs are `BaseMediumLow` (Dark `#8c8c8c`, Light `#696274`).
13. **Light:** `baseLow` 0.38 to 0.4 and `baseMediumLow` 0.64 to 0.6, so `BaseLow` is `#95909d` and
    `BaseMediumLow` `#696274`. Their aliases follow.
14. **Both:** the splash screen, the `n / m` page counter and the corrupted-document icon use
    `BaseMedium` instead of `Opacity`, so they are more legible. The Settings hint lines use
    `BaseMedium` instead of `BaseMediumLow`. The document loading ring uses `BaseMedium` instead of
    the accent colour.
15. **Both:** selected drop-down items use `ChromeWhite` text (Light was ink on the accent fill).
16. **Both:** the invalid page-number box has a 2px border and italic text as well as the error
    colour.
17. **Both:** the navigation pane's tab hover is now `ChromeGray` (it was `RegionColor` through the
    shared key).

## Accessibility review

Rules: [GUIDELINES.md](GUIDELINES.md). Checks: `dotnet run --file tools/Palette/AuditPalette.cs`
(146 checks, all pass).

Fixed:

| Finding | Fix |
|---|---|
| Light selected drop-down item text 3.7:1 on the accent fill | Text bound to `ChromeWhite` (5.3:1) |
| Settings hint text (`BaseMediumLow`) 3.8:1 in Dark on `RegionColor` | `BaseMedium` |
| Splash, page counter and corrupted-document icon faded with `Opacity` (about 3:1) | `BaseMedium` brush |
| Document loading ring in the accent colour: 1.9:1 in Dark | `BaseMedium` brush |
| Inactive-window close and scroll glyphs 2.0:1 to 2.8:1 | `BaseMediumLow` (3.0:1 or more) |
| Disabled scroll glyph 1.2:1 in Light | `BaseLow` (2.1:1) |
| Pressed fill with ink glyph 2.9:1 in Light; add-tab hover glyph 2.96:1 in Light | `baseMediumLow` and `baseLow` ramps retuned |
| Three `…Color` resources assigned to brush properties | `…Brush` resources |

Colour blindness (protanopia, deuteranopia, tritanopia), lowest margins: error border against the
normal border ΔE 17.7 (Dark, tritanopia); accent against `RegionColor` ΔE 16.7 (Dark,
tritanopia); selection against search overlay ΔE 33.1 (protanopia). Neutral surfaces, text and
tabs differ by lightness only and are unaffected.

Checked in the running app: the empty-window splash, the page counter and Settings hint in both
themes, and the resolved drop-down, glyph and disabled colours in Light. Not checked in the running
app: the print dialog error text and the document loading ring (both changed to brush resources).

The four open items from the first review were then addressed: the non-colour cue on the invalid
page number, the distinct hover shade and the shared hover key. The selected tab differs from the
strip by an 8.5 L* step in both themes (rule S3) with no extra indicator. GUIDELINES.md section 8
is now empty.

## Verification

- `Caly.Desktop` builds with 0 errors.
- The generator reproduces Dark exactly, and is idempotent: a second run reports
  "App.axaml is up to date".
- `--check` exits 0 when in sync and 1 when a palette attribute has been edited by hand.
- The app was launched in Dark and Light with two PDFs open. The tab strip, toolbar, navigation
  pane and thumbnail render correctly in both.
- Through the Avalonia DevTools MCP, with the single set of bindings:
  - Dark: selected tab `#333333`, unselected tab `#212121`, desk `#333333`, window `#333333`.
  - Switching to Light at runtime through the Settings combo box (the real `App.ApplyTheme` path)
    updated everything: window, selected tab and desk `#eceaf0`, tab strip and unselected tab
    `#d4d1d9`, separator `#9994a1`. So the flat dictionary follows a live theme change.
  - Strip fix, checked in both themes with the last tab selected: the tab control's `Background`
    resolves to `TabControlWindowActiveBackgroundBrush` (`#212121` Dark, `#d4d1d9` Light) and the
    screenshots show the strip right of the `+` button clearly darker than the selected tab. The
    toolbar and document area keep the surface colour.
  - Empty window (no document): the splash text is visible in Dark and Light, on the surface
    colour, with the strip and `+` button at the top. Before the fix the strip colour covered the
    whole area and hid the splash.
  - Inactive window (Notepad brought to the front): the unselected tab uses
    `TabItemBackgroundBrushWindowInactive` and resolves to the frame shade in both themes, the
    selected tab stays `#333333` / `#eceaf0`.
  - Selected-tab contrast against the strip is about 8.5 L\* in Dark and 8.7 in Light (Tabalonia's
    original Dark `#202020` strip is 9.0). Before the `chromeLow` change it was 2.8 in Dark and 6.3
    in Light.
  - With eight documents open so the tabs overflow, the tab buttons resolved to the palette colours
    in Dark (scroll glyph `#c6c6c6`, disabled scroll glyph `#585858`, close glyph white). After the
    live switch to Light, all 16 button keys resolved to the values in [Tab buttons](#tab-buttons)
    and the buttons on screen followed (scroll glyph `#3a3247`, close glyph `#120821`).
  - Attaching needed `.WithDeveloperTools()` uncommented in `Caly.Desktop/Program.cs` (it is
    commented out in the `#if DEBUG` block); it was reverted after.
  - Hover shade and invalid page number: in Dark a forced `:pointerover` on the unselected tab
    resolved to `#2a2a2a`, and the page-number box in error showed italic text and a 2px yellow
    border. In Light after the live switch: hover `#e0dee5`, italic text and a 2px red border. A
    forced hover on a navigation-pane tab resolved to `#e0dee5` through the shared key.
  - Not exercised: pressed and inactive-window states of the buttons, which need real pointer input
    or an unfocused window.
- Not visually checked: the settings hint text, disabled text boxes, the focus ring and the managed
  file chooser.

## Open decisions

- **Dark tint.** Whether Dark should also carry the purple tint of Light, or stay neutral grey. It
  is now a one-line change per pick in `palette.json`.
- **Guarding drift.** `--check` could run in CI or as a test so nobody edits the generated lines by
  hand. Not wired up.
