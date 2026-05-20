# Printing Options — Orientation, Fit, N-up, B&W

**Date:** 2026-05-10
**Branch:** `feature/printing-v3`
**Status:** design — pending implementation plan

## Goal

Extend the existing print path (added in `b440062`) with three user-facing options:

1. **Orientation** — Auto / Portrait / Landscape
2. **Page fit** — Fit to page / Actual size / Shrink to fit / Custom scale %
3. **Black & white** — print monochrome
4. **N-up** — 1, 2, or 4 PDF pages per printed sheet

Where the printer's own capabilities can fulfil the request, route the setting through the printer (DEVMODE on Windows, IPP Job Template attributes on Unix). Fall back to app-side rendering only when the capability is missing or refused.

## Non-goals

- Duplex / two-sided printing.
- Booklet layout, posterisation, or N-up values above 4.
- Persistence: settings are not saved between dialog openings (per user decision).
- Per-page rotation control in the dialog (`PrintPageInfo.Rotation` stays available for future use but isn't surfaced in the UI).
- Adobe-style "Choose paper source by PDF page size" or media tray selection.

## Approach

A single `PrintSettings` record carries all per-job choices. Each platform service decides per-feature whether to use the printer-native path or an app-side fallback. The dialog adapts to the selected printer's capabilities (returned by a new `GetPrinterCapabilitiesAsync` method).

### Routing summary

| Setting | Windows path | Unix path |
|---|---|---|
| Orientation Portrait/Landscape | DEVMODE `dmOrientation` | `OrientationRequested` (3 / 4) |
| Orientation Auto | per-page `ResetDCW` with new DEVMODE | per-page app-side bitmap rotation |
| FitMode Fit/Actual/Shrink | app-side (GDI doesn't expose) | `PrintScaling` Fit / None / AutoFit |
| FitMode CustomScale | app-side | app-side, send `PrintScaling.None` |
| PagesPerSheet | app-side tile (GDI doesn't expose) | `NumberUp` |
| ColorMode | DEVMODE `dmColor` if `DC_COLORDEVICE`; else app-side grayscale | `PrintColorMode` if supported; else app-side grayscale |

## Types & service surface

Added to `Caly.Core/Services/Interfaces/IPrintService.cs`:

```csharp
public enum PrintOrientation { Auto, Portrait, Landscape }
public enum PrintFitMode { FitToPage, ActualSize, ShrinkToFit, CustomScale }
public enum PrintColorMode { Color, Monochrome }

public sealed record PrintSettings(
    PrintOrientation Orientation = PrintOrientation.Auto,
    PrintFitMode FitMode = PrintFitMode.FitToPage,
    int CustomScalePercent = 100,   // 10..400, only consulted when FitMode == CustomScale
    int PagesPerSheet = 1,          // 1, 2, or 4
    PrintColorMode ColorMode = PrintColorMode.Color);

public sealed record PrinterCapabilities(
    bool SupportsLandscape,
    bool IsColorDevice,                 // printer has color hardware
    bool SupportsMonochromeDirective,   // can be told via printer to print mono
    IReadOnlyList<int> SupportedNumberUp); // filtered to subset of {1,2,4}
```

`IPrintService` gains:

```csharp
Task<PrinterCapabilities> GetPrinterCapabilitiesAsync(
    PrinterInfo printer, CancellationToken token);

Task PrintDocumentAsync(
    PrinterInfo printer,
    IPdfDocumentService documentService,
    IReadOnlyList<PrintPageInfo> pages,
    PrintSettings settings,                 // new parameter
    IProgress<int>? progress,
    CancellationToken token);
```

The existing `PrintDocumentAsync` overload without `PrintSettings` is replaced (single caller — `PrintDialogViewModel`).

## Windows implementation (`Caly.Printing.Windows`)

### Capability query

Use the `DeviceCapabilities` Win32 function (CsWin32-generated `PInvoke.DeviceCapabilitiesW`):

| Win32 capability | Result | Maps to |
|---|---|---|
| `DC_ORIENTATION` (17) | rotation in degrees (0 or 90) | `SupportsLandscape = (result == 90)` |
| `DC_COLORDEVICE` (32) | 0 / 1 | `IsColorDevice` |

`SupportsMonochromeDirective = IsColorDevice` — DEVMODE `dmColor = DMCOLOR_MONOCHROME` is honoured by every color-capable Windows driver. `SupportedNumberUp` is hard-coded to `[1, 2, 4]` because GDI does not expose N-up; we always tile app-side.

### DEVMODE construction

Helper `BuildDevModeBuffer(printerName, settings, caps) -> byte[]`:

1. `OpenPrinter` → printer handle.
2. `DocumentProperties(NULL, hPrinter, name, NULL, NULL, 0)` → required buffer size.
3. Allocate buffer, `DocumentProperties(... DM_OUT_BUFFER)` → fill with default DEVMODE.
4. Mutate fields:
   - `dmOrientation = settings.Orientation == Landscape ? DMORIENT_LANDSCAPE : DMORIENT_PORTRAIT`.
     For `Auto` we start in Portrait and flip per-page (see below).
   - `dmColor = (settings.ColorMode == Monochrome && caps.SupportsMonochromeDirective) ? DMCOLOR_MONOCHROME : DMCOLOR_COLOR`.
   - Set `dmFields` flags so the driver knows we changed those members.
5. `DocumentProperties(... DM_IN_BUFFER | DM_OUT_BUFFER)` → driver validates / merges.
6. `ClosePrinter`. The DEVMODE buffer outlives the handle.
7. Return the buffer.

`CreateDCW(NULL, name, NULL, pDevMode)` is then called with the buffer rather than `null`.

### Per-page orientation in Auto mode

Before each `StartPage`, decide orientation from the page's natural aspect (`pdfW > pdfH` → landscape). If it differs from the current DEVMODE orientation, call `ResetDCW(hdc, &dm)` to apply the updated DEVMODE before `StartPage`. This is the standard documented pattern for changing orientation between pages.

### Layout math (replaces `DrawBitmapToHdc`)

New `DrawSheetToHdc(hdc, pageBitmaps, settings, printerW, printerH, dpiX, dpiY)`:

1. **Cell layout** for N-up:
   - `1` → 1 cell at full printable area.
   - `2` → 2 cells, side-by-side along the longer paper edge.
   - `4` → 2×2 grid.
2. **2-up rotation**: when N=2, each PDF page is rotated 90° before being drawn into its cell so that two portrait PDF pages fit two-per-portrait-paper (Adobe convention). 4-up uses no rotation.
3. **Per-cell scale** (`FitMode`):
   - `FitToPage` → preserve aspect, scale to fill the cell.
   - `ActualSize` → 1 PDF pt = (1/72) × `dpiX` printer pixels (`GetDeviceCaps(LOGPIXELSX/Y)`).
   - `ShrinkToFit` → ActualSize, but if it overflows the cell, fall back to FitToPage for that cell.
   - `CustomScale` → `ActualSize × (CustomScalePercent / 100)`.
4. **Centering**: each rendered bitmap is centered in its cell.
5. `StretchDIBits` is called once per cell within a single `StartPage` / `EndPage`.

### App-side grayscale fallback

When `settings.ColorMode == Monochrome`, three sub-cases:

1. **Printer is mono-only** (`!caps.IsColorDevice` → `!caps.SupportsMonochromeDirective`). Output is B&W regardless of what we set; no app-side conversion needed.
2. **DEVMODE accepted** (color device, and after `DocumentProperties(DM_IN_BUFFER | DM_OUT_BUFFER)` the returned DEVMODE still has `dmColor == DMCOLOR_MONOCHROME`). Printer-driven mono; no app-side conversion.
3. **DEVMODE stripped** (rare; driver returned `dmColor == DMCOLOR_COLOR` despite our request). Convert the SKBitmap to grayscale before `StretchDIBits`.

The grayscale pass is one loop over BGRA8888 pixels: ITU-R BT.601 luma written back as gray BGRA. Implemented in `Caly.Printing.Core/PrintServiceHelper.cs` (shared with Unix).

### Rendering size

Pages are still rendered via `PrintServiceHelper.RenderPageToBitmapAsync` at `documentService.PpiScale`. The cell math just produces smaller `destW/destH` for `StretchDIBits`. We don't re-architect the helper.

## Unix/CUPS implementation (`Caly.Printing.Unix`)

### Capability query

Send an IPP `Get-Printer-Attributes` request via SharpIPP and read the `*-supported` attributes:

| IPP attribute | Maps to |
|---|---|
| `orientation-requested-supported` | `SupportsLandscape = contains 4` |
| `print-color-mode-supported` | `IsColorDevice = contains "color"`; `SupportsMonochromeDirective = contains "monochrome"` |
| `number-up-supported` | filter to `{1,2,4} ∩ supported` → `SupportedNumberUp` |
| `print-scaling-supported` | not surfaced; we degrade silently if a value isn't supported |

If the printer omits a `*-supported` attribute (some basic IPP printers do), assume conservative defaults: `SupportsLandscape = true`, `IsColorDevice = true`, `SupportsMonochromeDirective = true`, `SupportedNumberUp = [1, 2, 4]`. The printer will reject if it really can't, surfaced as a print error.

### Job submission — `JobTemplateAttributes` mapping

```csharp
JobTemplateAttributes = new JobTemplateAttributes
{
    OrientationRequested = MapOrientation(settings, caps), // 3, 4, or null for Auto
    PrintColorMode       = MapColorMode(settings, caps),   // "monochrome" / "color" / null
    NumberUp             = settings.PagesPerSheet,         // 1, 2, or 4
    PrintScaling         = MapFitMode(settings),           // Fit / None / AutoFit
}
```

Mapping rules:

- **Orientation** — `Portrait` → 3; `Landscape` → 4 (only if `caps.SupportsLandscape`, otherwise leave null and rotate app-side); `Auto` → leave job-level null and rotate per-page app-side (see below).
- **ColorMode** — `Monochrome` and `caps.SupportsMonochromeDirective` → `"monochrome"`. Otherwise convert bitmap to grayscale before JPEG encode and omit the directive.
- **NumberUp** — pass through if in `caps.SupportedNumberUp`. If 2 or 4 isn't in that list (rare), tile app-side and send `NumberUp = 1`. The dialog already filters values to supported, so this is a safety belt.
- **FitMode** — `FitToPage` → `Fit`; `ActualSize` → `None`; `ShrinkToFit` → `AutoFit`; `CustomScale` → `None` (bitmap is pre-scaled by the helper).

### Per-page orientation in Auto mode

`OrientationRequested` is a Job Template attribute, not a per-document override in SharpIPP's surface. Rather than chunking pages into multiple Create-Job calls, **rotate the bitmap app-side** when Auto + a page is wider than tall. Output is identical to printer-side rotation; the printer just sees an already-correctly-oriented portrait image.

### Custom-scale and grayscale rendering

`PrintServiceHelper.RenderPageToBitmapAsync` already accepts `overridePpiScale`:

- **Custom scale** — pass `printScale × (CustomScalePercent / 100)`. CUPS receives `print-scaling = none` and prints at the bitmap's native size.
- **Grayscale fallback** — new `PrintServiceHelper.ConvertToGrayscaleInPlace(SKBitmap)` invoked between render and JPEG encode when `caps.SupportsMonochromeDirective == false && settings.ColorMode == Monochrome`.

### What stays the same

`Create-Job` → loop of `Send-Document` → cleanup pattern, the `RequestingUserName` localhost trick, and JPEG transport are unchanged.

## ViewModel & dialog wiring (`Caly.Core`)

### `PrintDialogViewModel` additions

Observable properties, mirroring `PrintSettings`:

```csharp
[ObservableProperty] private bool _isOrientationAuto = true;
[ObservableProperty] private bool _isOrientationPortrait;
[ObservableProperty] private bool _isOrientationLandscape;

[ObservableProperty] private bool _isFitToPage = true;
[ObservableProperty] private bool _isActualSize;
[ObservableProperty] private bool _isShrinkToFit;
[ObservableProperty] private bool _isCustomScale;
[ObservableProperty] private int  _customScalePercent = 100;   // 10..400 clamp on Print

[ObservableProperty] private int  _pagesPerSheet = 1;          // bound to ComboBox
[ObservableProperty] private bool _isBlackAndWhite;
```

Mutual exclusion uses the `partial void OnXChanged` pattern already established for the page-range radios.

### Capability-driven UI

```csharp
[ObservableProperty] private PrinterCapabilities? _selectedPrinterCapabilities;
```

`partial void OnSelectedPrinterChanged(PrinterInfo? value)` kicks off:

```csharp
async Task LoadCapabilitiesAsync(PrinterInfo printer, CancellationToken token)
{
    SelectedPrinterCapabilities = await _printService.GetPrinterCapabilitiesAsync(printer, token);
}
```

A per-printer-change `CancellationTokenSource` ensures a quick switch doesn't race.

If a currently-selected option becomes unsupported when the printer changes (e.g. user had Landscape, switches to a portrait-only printer), the VM auto-resets to a safe default (`Auto` for orientation, `1` for N-up) and surfaces a one-line `StatusMessage` explaining the reset.

### XAML changes — `PrintDialogWindow.axaml`

The dialog grows to a taller layout matching the chosen "expanded single panel" mockup. New control groups, in order:

1. **Orientation** — three `RadioButton`s in a horizontal `StackPanel` (`Auto`, `Portrait`, `Landscape`).
2. **Fit** — four `RadioButton`s (`Fit to page`, `Actual size`, `Shrink to fit`, `Custom: [NumericUpDown]%`).
3. **Pages per sheet** — `ComboBox` with `1`, `2`, `4`.
4. **Black and white** — `CheckBox`.

Capability bindings on `IsEnabled`:

| Control | Binding |
|---|---|
| Landscape radio | `CanSelectLandscape` (= `caps == null \|\| caps.SupportsLandscape`) |
| `pagesPerSheet = 2` item | `IsNUp2Supported` |
| `pagesPerSheet = 4` item | `IsNUp4Supported` |
| Auto radio | always enabled (degrades to portrait if landscape unsupported) |
| B&W checkbox | always enabled (we have the grayscale fallback) |
| Custom-scale `NumericUpDown` | `IsCustomScale` |

`NumericUpDown` (Avalonia built-in) is preferred over a free-form TextBox so clamping happens in the control.

### `Print` command changes

```csharp
var settings = new PrintSettings(
    Orientation: IsOrientationPortrait ? Portrait : IsOrientationLandscape ? Landscape : Auto,
    FitMode:     IsActualSize ? ActualSize : IsShrinkToFit ? ShrinkToFit
                 : IsCustomScale ? CustomScale : FitToPage,
    CustomScalePercent: Math.Clamp(CustomScalePercent, 10, 400),
    PagesPerSheet: PagesPerSheet,
    ColorMode: IsBlackAndWhite ? Monochrome : Color);

await _printService.PrintDocumentAsync(printer, _documentService, pages, settings, progress, token);
```

`CanPrint` adds: if `IsCustomScale` and `CustomScalePercent` is out of `[10, 400]`, block.

## Testing

### Unit tests (Caly.Tests, xUnit)

| Test class | Coverage |
|---|---|
| `PrintSettingsTests` | Default values; record equality |
| `PrintDialogViewModelTests` | Radio mutual-exclusion (orientation, fit); `CanPrint` with each combination of state; `Custom scale` clamp boundaries (9, 10, 400, 401); reset-on-printer-change when capability removes the current selection; `PrintSettings` build from VM state |
| `WindowsLayoutTests` (Windows-only) | Pure cell-layout math: given printer pixels + N-up + FitMode + bitmap aspect, compute dest rect for each cell. Covers 1/2/4-up × Portrait/Landscape × Fit/Actual/Shrink/CustomScale |
| `UnixIppMappingTests` | `PrintSettings` → `JobTemplateAttributes` mapping (orientation 3/4/null, color-mode strings, NumberUp passthrough, PrintScaling enum); fallback paths when caps say "no" |
| `GrayscaleConversionTests` | One-pixel and 2×2 BGRA bitmap → BT.601 luma sanity check |
| `AutoOrientationTests` | Per-page decision: portrait page → portrait, wide page → landscape |
| `IppCapabilityParseTests` | Parse fake `IppAttribute` arrays into `PrinterCapabilities`; missing attributes → conservative defaults |

### What is NOT unit tested

- `WindowsPrintService` end-to-end — needs a real printer DC; manual.
- `UnixPrintService` end-to-end — needs a CUPS instance; manual.
- DEVMODE round-trip — Win32 spooler dependency; manual.

### Manual test matrix

| Scenario | Win | Linux | macOS |
|---|---|---|---|
| Portrait + Fit + 1-up + Color | ✓ | ✓ | ✓ |
| Landscape + Fit | ✓ | ✓ | ✓ |
| Auto with mixed-orientation PDF | ✓ | ✓ | ✓ |
| Actual size — A4 page on A4 paper | ✓ | ✓ | ✓ |
| Shrink to fit — Letter PDF on A4 | ✓ | ✓ | ✓ |
| Custom 50% | ✓ | ✓ | ✓ |
| Custom 200% (overflow) | ✓ | ✓ | ✓ |
| 2-up + Portrait | ✓ | ✓ | ✓ |
| 4-up + Landscape | ✓ | ✓ | ✓ |
| B&W on a color printer | ✓ | ✓ | ✓ |
| B&W on a mono-only printer | ✓ | ✓ | n/a |
| Cancel mid-print | ✓ | ✓ | ✓ |

### Build verification

Before requesting review:

- `dotnet build -c Debug` (full solution — both `net9.0` and `net10.0` TFMs)
- `dotnet build -c Release`
- `dotnet test Caly.Tests`
- `dotnet publish Caly.Desktop -r win-x64 -c AOT -f net10.0` — confirms the new code is AOT-clean (no reflection / dynamic JSON in the print path)

## Files touched

- `Caly.Core/Services/Interfaces/IPrintService.cs` — new types, `GetPrinterCapabilitiesAsync`, `PrintSettings` parameter on `PrintDocumentAsync`
- `Caly.Core/ViewModels/PrintDialogViewModel.cs` — new observable properties, capability load on printer change, `PrintSettings` build in `Print`
- `Caly.Core/Views/PrintDialogWindow.axaml` — new control groups, capability bindings, larger height
- `Printing/Caly.Printing.Core/PrintServiceHelper.cs` — `ConvertToGrayscaleInPlace`; existing `RenderPageToBitmapAsync` reused
- `Printing/Caly.Printing.Windows/WindowsPrintService.cs` — DEVMODE construction, capability query, per-page `ResetDCW` for Auto, `DrawSheetToHdc` for layout/N-up
- `Printing/Caly.Printing.Unix/UnixPrintService.cs` — `Get-Printer-Attributes` capability query, JobTemplateAttributes mapping, app-side rotate/grayscale fallbacks
- `Caly.Tests/` — new test classes listed above

## Open risks

- **DEVMODE alignment / size**: must match driver expectation exactly. Standard mitigation is the round-trip through `DocumentProperties(DM_IN_BUFFER | DM_OUT_BUFFER)`; we follow it.
- **`ResetDCW` between pages**: documented but driver-quality varies. Manual test on at least two physical drivers (Brother, HP).
- **CUPS attribute names**: SharpIPP uses strongly-typed enums (`PrintScaling`, `PrintColorMode`). If a needed attribute isn't surfaced (e.g. per-document `OrientationRequested` on Send-Document), we fall back to app-side — the design already accounts for this for Auto orientation.
- **N-up bitmap memory on Windows**: 2-up and 4-up render up to 4 page bitmaps before `StartPage`. Memory footprint scales linearly with N. Acceptable for N ≤ 4 at typical PDF sizes.
