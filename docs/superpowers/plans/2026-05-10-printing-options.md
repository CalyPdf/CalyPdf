# Printing Options Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the print path with orientation, page-fit, N-up, and B&W options, routed through printer capabilities first and falling back to app-side rendering when capabilities are missing.

**Architecture:** A single `PrintSettings` record carries per-job choices to `IPrintService.PrintDocumentAsync`. A new `IPrintService.GetPrinterCapabilitiesAsync` method drives dialog-side enable/disable. Pure helpers (layout math, attribute mapping, grayscale conversion) live in `Caly.Printing.Core` so they are unit-testable from `Caly.Tests`. Platform services (`Caly.Printing.Windows`, `Caly.Printing.Unix`) compose those helpers and add P/Invoke / IPP code that is verified manually.

**Tech Stack:** .NET 9 / 10, Avalonia, CommunityToolkit.Mvvm, SkiaSharp, CsWin32 (Windows), SharpIppNext (Unix), xUnit.

**Spec:** `docs/superpowers/specs/2026-05-10-printing-options-design.md`

---

## File map

| Path | Status | Responsibility |
|---|---|---|
| `Caly.Core/Services/Interfaces/IPrintService.cs` | modify | Public types (`PrintSettings`, `PrinterCapabilities`, enums); interface adds `GetPrinterCapabilitiesAsync` and the `PrintSettings` parameter |
| `Caly.Core/ViewModels/PrintDialogViewModel.cs` | modify | New observable properties; capability load on printer change; `Print` command builds `PrintSettings` |
| `Caly.Core/Views/PrintDialogWindow.axaml` | modify | New control groups; capability bindings; larger height |
| `Printing/Caly.Printing.Core/PrintServiceHelper.cs` | modify | Add `ConvertToGrayscaleInPlace` (cross-platform grayscale fallback) |
| `Printing/Caly.Printing.Core/PrintLayout.cs` | create | Pure cell-layout math (N-up cells, fit/scale rect, auto-orientation decision, 2-up rotation) |
| `Printing/Caly.Printing.Core/IppAttributeMapping.cs` | create | Pure mapping `PrintSettings + PrinterCapabilities → (orientation int?, color string?, number-up int, scaling enum)` |
| `Printing/Caly.Printing.Windows/NativeMethods.txt` | modify | Add `OpenPrinter`, `ClosePrinter`, `DocumentProperties`, `ResetDC`, `DeviceCapabilities`, `DEVMODEW` |
| `Printing/Caly.Printing.Windows/WindowsPrintService.cs` | modify | Capability query (`DeviceCapabilities`); DEVMODE building & validation; per-page `ResetDCW` for Auto; replace `DrawBitmapToHdc` with `DrawSheetToHdc`; grayscale fallback |
| `Printing/Caly.Printing.Unix/UnixPrintService.cs` | modify | Capability query via IPP `Get-Printer-Attributes`; consume `IppAttributeMapping`; per-page bitmap rotation in Auto; grayscale fallback |
| `Caly.Tests/Caly.Tests.csproj` | modify | Add project reference to `Caly.Printing.Core` |
| `Caly.Tests/PrintServiceHelperTests.cs` | create | Grayscale conversion tests |
| `Caly.Tests/PrintLayoutTests.cs` | create | Cell layout, fit, auto-orientation tests |
| `Caly.Tests/IppAttributeMappingTests.cs` | create | Mapping decision tests |
| `Caly.Tests/PrintDialogViewModelTests.cs` | create | Mutual exclusion, capability reset, settings build, custom-scale clamp |

---

## Task 1: Define types and extend `IPrintService` (no behavior change)

**Files:**
- Modify: `Caly.Core/Services/Interfaces/IPrintService.cs`
- Modify: `Printing/Caly.Printing.Windows/WindowsPrintService.cs` (add stubs to compile)
- Modify: `Printing/Caly.Printing.Unix/UnixPrintService.cs` (add stubs to compile)
- Modify: `Caly.Core/ViewModels/PrintDialogViewModel.cs` (pass default `new PrintSettings()`)

- [ ] **Step 1: Add new types and interface members**

In `Caly.Core/Services/Interfaces/IPrintService.cs`, append these types after the existing `PrinterInfo` record and modify the interface:

```csharp
public enum PrintOrientation { Auto, Portrait, Landscape }

public enum PrintFitMode { FitToPage, ActualSize, ShrinkToFit, CustomScale }

public enum PrintColorMode { Color, Monochrome }

/// <summary>
/// Per-job print settings selected in the dialog.
/// </summary>
public sealed record PrintSettings(
    PrintOrientation Orientation = PrintOrientation.Auto,
    PrintFitMode FitMode = PrintFitMode.FitToPage,
    int CustomScalePercent = 100,
    int PagesPerSheet = 1,
    PrintColorMode ColorMode = PrintColorMode.Color);

/// <summary>
/// Subset of printer capabilities relevant to the print dialog.
/// </summary>
public sealed record PrinterCapabilities(
    bool SupportsLandscape,
    bool IsColorDevice,
    bool SupportsMonochromeDirective,
    IReadOnlyList<int> SupportedNumberUp);
```

Replace the `IPrintService` interface body with:

```csharp
public interface IPrintService
{
    Task<IReadOnlyList<PrinterInfo>> GetAvailablePrintersAsync(CancellationToken token = default);

    Task<PrinterCapabilities> GetPrinterCapabilitiesAsync(
        PrinterInfo printer,
        CancellationToken token);

    Task PrintDocumentAsync(
        PrinterInfo printer,
        IPdfDocumentService documentService,
        IReadOnlyList<PrintPageInfo> pages,
        PrintSettings settings,
        IProgress<int>? progress,
        CancellationToken token);
}
```

- [ ] **Step 2: Stub the new method and parameter on Windows service**

In `Printing/Caly.Printing.Windows/WindowsPrintService.cs`, add:

```csharp
public Task<PrinterCapabilities> GetPrinterCapabilitiesAsync(
    PrinterInfo printer,
    CancellationToken token)
{
    // Stub — replaced in Task 9.
    return Task.FromResult(new PrinterCapabilities(
        SupportsLandscape: true,
        IsColorDevice: true,
        SupportsMonochromeDirective: true,
        SupportedNumberUp: [1, 2, 4]));
}
```

Update the existing `PrintDocumentAsync` signature to accept `PrintSettings settings` (positioned between `pages` and `progress`). Add the parameter to the inner `Task.Run` lambda's call to `PrintWindowsCoreAsync`. Update `PrintWindowsCoreAsync` signature too — but ignore `settings` for now (Tasks 10–13 wire it in).

- [ ] **Step 3: Stub the new method and parameter on Unix service**

Same pattern in `Printing/Caly.Printing.Unix/UnixPrintService.cs`:

```csharp
public Task<PrinterCapabilities> GetPrinterCapabilitiesAsync(
    PrinterInfo printer,
    CancellationToken token)
{
    // Stub — replaced in Task 14.
    return Task.FromResult(new PrinterCapabilities(
        SupportsLandscape: true,
        IsColorDevice: true,
        SupportsMonochromeDirective: true,
        SupportedNumberUp: [1, 2, 4]));
}
```

Add `PrintSettings settings` to `PrintDocumentAsync` and `PrintIppAsync`. Ignore the value for now.

- [ ] **Step 4: Update the ViewModel call site**

In `Caly.Core/ViewModels/PrintDialogViewModel.cs`, find the `_printService.PrintDocumentAsync(...)` call inside `Print(CancellationToken token)` and add a default `PrintSettings`:

```csharp
await _printService.PrintDocumentAsync(printer, _documentService, pages, new PrintSettings(), progress, token);
```

- [ ] **Step 5: Build to verify the type changes compile**

Run: `dotnet build -c Debug`
Expected: build succeeds, 0 warnings related to print code.

- [ ] **Step 6: Commit**

```bash
git add Caly.Core/Services/Interfaces/IPrintService.cs Caly.Core/ViewModels/PrintDialogViewModel.cs Printing/Caly.Printing.Windows/WindowsPrintService.cs Printing/Caly.Printing.Unix/UnixPrintService.cs
git commit -m "Add PrintSettings and PrinterCapabilities to IPrintService"
```

---

## Task 2: Reference `Caly.Printing.Core` from `Caly.Tests`

**Files:**
- Modify: `Caly.Tests/Caly.Tests.csproj`

- [ ] **Step 1: Add project reference**

Edit `Caly.Tests/Caly.Tests.csproj`. Inside the existing `<ItemGroup>` that contains `<ProjectReference Include="..\Caly.Core\Caly.Core.csproj" />`, append:

```xml
<ProjectReference Include="..\Printing\Caly.Printing.Core\Caly.Printing.Core.csproj" />
```

- [ ] **Step 2: Build tests to verify**

Run: `dotnet build Caly.Tests -c Debug`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add Caly.Tests/Caly.Tests.csproj
git commit -m "Reference Caly.Printing.Core from Caly.Tests"
```

---

## Task 3: TDD `ConvertToGrayscaleInPlace` in `PrintServiceHelper`

**Files:**
- Modify: `Printing/Caly.Printing.Core/PrintServiceHelper.cs`
- Create: `Caly.Tests/PrintServiceHelperTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Caly.Tests/PrintServiceHelperTests.cs`:

```csharp
using Caly.Printing.Core;
using SkiaSharp;

namespace Caly.Tests;

public class PrintServiceHelperTests
{
    [Fact]
    public void ConvertToGrayscaleInPlace_PureRed_GivesBT601Luma()
    {
        // BT.601: Y = 0.299*R + 0.587*G + 0.114*B
        // Pure red (255,0,0) → Y ≈ 76.245 → 76
        using var bitmap = new SKBitmap(1, 1, SKColorType.Bgra8888, SKAlphaType.Premul);
        bitmap.SetPixel(0, 0, new SKColor(255, 0, 0, 255));

        PrintServiceHelper.ConvertToGrayscaleInPlace(bitmap);

        var p = bitmap.GetPixel(0, 0);
        Assert.Equal(76, p.Red);
        Assert.Equal(76, p.Green);
        Assert.Equal(76, p.Blue);
        Assert.Equal(255, p.Alpha);
    }

    [Fact]
    public void ConvertToGrayscaleInPlace_PureWhite_StaysWhite()
    {
        using var bitmap = new SKBitmap(2, 2, SKColorType.Bgra8888, SKAlphaType.Premul);
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 2; x++)
                bitmap.SetPixel(x, y, SKColors.White);

        PrintServiceHelper.ConvertToGrayscaleInPlace(bitmap);

        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 2; x++)
            {
                var p = bitmap.GetPixel(x, y);
                Assert.Equal(255, p.Red);
                Assert.Equal(255, p.Green);
                Assert.Equal(255, p.Blue);
            }
    }

    [Fact]
    public void ConvertToGrayscaleInPlace_PureBlack_StaysBlack()
    {
        using var bitmap = new SKBitmap(1, 1, SKColorType.Bgra8888, SKAlphaType.Premul);
        bitmap.SetPixel(0, 0, SKColors.Black);

        PrintServiceHelper.ConvertToGrayscaleInPlace(bitmap);

        var p = bitmap.GetPixel(0, 0);
        Assert.Equal(0, p.Red);
        Assert.Equal(0, p.Green);
        Assert.Equal(0, p.Blue);
    }
}
```

- [ ] **Step 2: Run test, expect compile failure (method not defined)**

Run: `dotnet test Caly.Tests --filter "FullyQualifiedName~PrintServiceHelperTests"`
Expected: build error CS0117 — `'PrintServiceHelper' does not contain a definition for 'ConvertToGrayscaleInPlace'`.

- [ ] **Step 3: Implement `ConvertToGrayscaleInPlace`**

Append to `Printing/Caly.Printing.Core/PrintServiceHelper.cs` inside the `PrintServiceHelper` class:

```csharp
/// <summary>
/// Converts a BGRA8888 bitmap to grayscale in-place using ITU-R BT.601 luma weights
/// (R 0.299, G 0.587, B 0.114). Used as the app-side fallback when a printer cannot
/// be told to render in monochrome.
/// </summary>
public static unsafe void ConvertToGrayscaleInPlace(SKBitmap bitmap)
{
    if (bitmap.ColorType != SKColorType.Bgra8888)
    {
        throw new ArgumentException("Bitmap must be BGRA8888.", nameof(bitmap));
    }

    int w = bitmap.Width;
    int h = bitmap.Height;
    int rowBytes = bitmap.RowBytes;
    byte* basePtr = (byte*)bitmap.GetPixels();

    for (int y = 0; y < h; y++)
    {
        byte* row = basePtr + y * rowBytes;
        for (int x = 0; x < w; x++)
        {
            byte b = row[0];
            byte g = row[1];
            byte r = row[2];
            // BT.601: integer math with rounding
            int luma = (r * 299 + g * 587 + b * 114) / 1000;
            byte gray = (byte)luma;
            row[0] = gray;
            row[1] = gray;
            row[2] = gray;
            // alpha (row[3]) untouched
            row += 4;
        }
    }
}
```

- [ ] **Step 4: Run tests, expect PASS**

Run: `dotnet test Caly.Tests --filter "FullyQualifiedName~PrintServiceHelperTests"`
Expected: 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add Caly.Tests/PrintServiceHelperTests.cs Printing/Caly.Printing.Core/PrintServiceHelper.cs
git commit -m "Add ConvertToGrayscaleInPlace BT.601 luma helper"
```

---

## Task 4: TDD `PrintLayout.ShouldRotateForAutoOrientation`

**Files:**
- Create: `Printing/Caly.Printing.Core/PrintLayout.cs`
- Create: `Caly.Tests/PrintLayoutTests.cs`

- [ ] **Step 1: Write failing test**

Create `Caly.Tests/PrintLayoutTests.cs`:

```csharp
using Caly.Printing.Core;

namespace Caly.Tests;

public class PrintLayoutTests
{
    [Theory]
    [InlineData(595, 842, false)]   // A4 portrait — no rotation
    [InlineData(842, 595, true)]    // A4 landscape — rotate to print landscape
    [InlineData(500, 500, false)]   // square — no rotation
    public void ShouldRotateForAutoOrientation_FlipsWhenPageIsWide(
        float pdfWidth, float pdfHeight, bool expected)
    {
        Assert.Equal(expected, PrintLayout.ShouldRotateForAutoOrientation(pdfWidth, pdfHeight));
    }
}
```

- [ ] **Step 2: Run test, expect compile failure**

Run: `dotnet test Caly.Tests --filter "FullyQualifiedName~PrintLayoutTests"`
Expected: CS0103 — `'PrintLayout' could not be found`.

- [ ] **Step 3: Create `PrintLayout` with the helper**

Create `Printing/Caly.Printing.Core/PrintLayout.cs`:

```csharp
// Copyright (c) 2025 BobLd
// Licensed under the MIT License — see LICENSE in the repo root.

namespace Caly.Printing.Core;

/// <summary>
/// Pure layout math used by both Windows and Unix print services.
/// </summary>
public static class PrintLayout
{
    /// <summary>
    /// Returns true if the page should be rotated so it prints in landscape rather
    /// than portrait when <c>PrintOrientation.Auto</c> is selected. A page is "wide"
    /// if its width strictly exceeds its height.
    /// </summary>
    public static bool ShouldRotateForAutoOrientation(float pdfWidth, float pdfHeight)
    {
        return pdfWidth > pdfHeight;
    }
}
```

- [ ] **Step 4: Run, expect PASS**

Run: `dotnet test Caly.Tests --filter "FullyQualifiedName~PrintLayoutTests"`
Expected: 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add Printing/Caly.Printing.Core/PrintLayout.cs Caly.Tests/PrintLayoutTests.cs
git commit -m "Add PrintLayout.ShouldRotateForAutoOrientation"
```

---

## Task 5: TDD `PrintLayout.ComputeCells` for N-up

**Files:**
- Modify: `Printing/Caly.Printing.Core/PrintLayout.cs`
- Modify: `Caly.Tests/PrintLayoutTests.cs`

- [ ] **Step 1: Write failing tests**

Append to `Caly.Tests/PrintLayoutTests.cs`:

```csharp
public class PrintLayoutCellsTests
{
    [Fact]
    public void ComputeCells_OneUp_ReturnsFullArea()
    {
        var cells = PrintLayout.ComputeCells(printerW: 1000, printerH: 800, pagesPerSheet: 1);
        Assert.Single(cells);
        Assert.Equal(new PrintLayout.Rect(0, 0, 1000, 800), cells[0]);
    }

    [Fact]
    public void ComputeCells_TwoUp_PortraitPaper_StacksTopBottom()
    {
        // printerW=600, printerH=800 → portrait paper. Longer edge = vertical.
        // 2-up splits along the longer edge → top half + bottom half.
        var cells = PrintLayout.ComputeCells(printerW: 600, printerH: 800, pagesPerSheet: 2);
        Assert.Equal(2, cells.Count);
        Assert.Equal(new PrintLayout.Rect(0, 0, 600, 400), cells[0]);
        Assert.Equal(new PrintLayout.Rect(0, 400, 600, 400), cells[1]);
    }

    [Fact]
    public void ComputeCells_TwoUp_LandscapePaper_SplitsLeftRight()
    {
        // printerW=1000, printerH=600 → landscape paper. Longer edge = horizontal.
        var cells = PrintLayout.ComputeCells(printerW: 1000, printerH: 600, pagesPerSheet: 2);
        Assert.Equal(2, cells.Count);
        Assert.Equal(new PrintLayout.Rect(0, 0, 500, 600), cells[0]);
        Assert.Equal(new PrintLayout.Rect(500, 0, 500, 600), cells[1]);
    }

    [Fact]
    public void ComputeCells_FourUp_TwoByTwoGrid()
    {
        var cells = PrintLayout.ComputeCells(printerW: 1000, printerH: 800, pagesPerSheet: 4);
        Assert.Equal(4, cells.Count);
        // Reading order: top-left, top-right, bottom-left, bottom-right
        Assert.Equal(new PrintLayout.Rect(0, 0, 500, 400), cells[0]);
        Assert.Equal(new PrintLayout.Rect(500, 0, 500, 400), cells[1]);
        Assert.Equal(new PrintLayout.Rect(0, 400, 500, 400), cells[2]);
        Assert.Equal(new PrintLayout.Rect(500, 400, 500, 400), cells[3]);
    }

    [Fact]
    public void ComputeCells_UnsupportedNumberUp_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PrintLayout.ComputeCells(printerW: 600, printerH: 800, pagesPerSheet: 3));
    }
}
```

- [ ] **Step 2: Run, expect failure**

Run: `dotnet test Caly.Tests --filter "FullyQualifiedName~PrintLayoutCellsTests"`
Expected: CS0103 — `Rect` and `ComputeCells` not defined.

- [ ] **Step 3: Implement `Rect` and `ComputeCells`**

Append to `Printing/Caly.Printing.Core/PrintLayout.cs` (inside the `PrintLayout` class):

```csharp
/// <summary>Pixel-space rectangle. X/Y are top-left corner.</summary>
public readonly record struct Rect(int X, int Y, int Width, int Height);

/// <summary>
/// Computes the cells (regions of the printable area) for an N-up layout.
/// Reading order: left-to-right, top-to-bottom.
/// </summary>
/// <exception cref="ArgumentOutOfRangeException">
/// <paramref name="pagesPerSheet"/> is not 1, 2, or 4.
/// </exception>
public static IReadOnlyList<Rect> ComputeCells(int printerW, int printerH, int pagesPerSheet)
{
    switch (pagesPerSheet)
    {
        case 1:
            return new[] { new Rect(0, 0, printerW, printerH) };

        case 2:
            // Split along the paper's longer edge.
            if (printerH >= printerW)
            {
                int halfH = printerH / 2;
                return new[]
                {
                    new Rect(0, 0, printerW, halfH),
                    new Rect(0, halfH, printerW, printerH - halfH)
                };
            }
            else
            {
                int halfW = printerW / 2;
                return new[]
                {
                    new Rect(0, 0, halfW, printerH),
                    new Rect(halfW, 0, printerW - halfW, printerH)
                };
            }

        case 4:
        {
            int halfW = printerW / 2;
            int halfH = printerH / 2;
            return new[]
            {
                new Rect(0, 0, halfW, halfH),
                new Rect(halfW, 0, printerW - halfW, halfH),
                new Rect(0, halfH, halfW, printerH - halfH),
                new Rect(halfW, halfH, printerW - halfW, printerH - halfH)
            };
        }

        default:
            throw new ArgumentOutOfRangeException(
                nameof(pagesPerSheet),
                pagesPerSheet,
                "Only 1, 2, or 4 pages per sheet are supported.");
    }
}
```

- [ ] **Step 4: Run, expect PASS**

Run: `dotnet test Caly.Tests --filter "FullyQualifiedName~PrintLayoutCellsTests"`
Expected: 5 tests pass.

- [ ] **Step 5: Commit**

```bash
git add Printing/Caly.Printing.Core/PrintLayout.cs Caly.Tests/PrintLayoutTests.cs
git commit -m "Add PrintLayout.ComputeCells N-up helper"
```

---

## Task 6: TDD `PrintLayout.ComputeDestRect` (FitMode placement within a cell)

**Files:**
- Modify: `Printing/Caly.Printing.Core/PrintLayout.cs`
- Modify: `Caly.Tests/PrintLayoutTests.cs`

- [ ] **Step 1: Write failing tests**

Append to `Caly.Tests/PrintLayoutTests.cs`:

```csharp
public class PrintLayoutFitTests
{
    // 100×200 cell, 50×50 source bitmap (assumed already at intended physical size).
    // dpiX = dpiY = 100. 1 PDF point = 1/72 inch → 100/72 printer pixels per point.
    // We only test the rect-shape — actual scaling happens via StretchDIBits.

    [Fact]
    public void ComputeDestRect_FitToPage_PreservesAspect_FillsLongest()
    {
        // Bitmap is 50×50 (square aspect). Cell is 100×200 (tall). Fitting → 100×100 centered vertically.
        var cell = new PrintLayout.Rect(10, 20, 100, 200);
        var dest = PrintLayout.ComputeDestRect(
            cell, bitmapW: 50, bitmapH: 50,
            pdfPointsW: 50, pdfPointsH: 50,
            dpiX: 100, dpiY: 100,
            fitMode: PrintFitMode.FitToPage,
            customScalePercent: 100);

        Assert.Equal(10, dest.X);
        Assert.Equal(20 + 50, dest.Y);  // centered: (200 - 100) / 2 = 50
        Assert.Equal(100, dest.Width);
        Assert.Equal(100, dest.Height);
    }

    [Fact]
    public void ComputeDestRect_ActualSize_UsesPhysicalDimensions()
    {
        // 72 PDF points = 1 inch = 100 printer pixels at 100 dpi.
        var cell = new PrintLayout.Rect(0, 0, 1000, 1000);
        var dest = PrintLayout.ComputeDestRect(
            cell, bitmapW: 999, bitmapH: 999,  // bitmap pixel size irrelevant for ActualSize
            pdfPointsW: 72, pdfPointsH: 144,
            dpiX: 100, dpiY: 100,
            fitMode: PrintFitMode.ActualSize,
            customScalePercent: 100);

        Assert.Equal(100, dest.Width);
        Assert.Equal(200, dest.Height);
    }

    [Fact]
    public void ComputeDestRect_ShrinkToFit_OnlyShrinksWhenLarger()
    {
        // Small page (1×1 inch) on a 5×5 inch cell — stays at actual size, not scaled up.
        var cell = new PrintLayout.Rect(0, 0, 500, 500);
        var dest = PrintLayout.ComputeDestRect(
            cell, bitmapW: 100, bitmapH: 100,
            pdfPointsW: 72, pdfPointsH: 72,
            dpiX: 100, dpiY: 100,
            fitMode: PrintFitMode.ShrinkToFit,
            customScalePercent: 100);
        Assert.Equal(100, dest.Width);
        Assert.Equal(100, dest.Height);
    }

    [Fact]
    public void ComputeDestRect_ShrinkToFit_FitsWhenLarger()
    {
        // Big page (10×10 inch) on a 5×5 inch cell — fits to 5×5 inch (500×500 px).
        var cell = new PrintLayout.Rect(0, 0, 500, 500);
        var dest = PrintLayout.ComputeDestRect(
            cell, bitmapW: 1000, bitmapH: 1000,
            pdfPointsW: 720, pdfPointsH: 720,
            dpiX: 100, dpiY: 100,
            fitMode: PrintFitMode.ShrinkToFit,
            customScalePercent: 100);
        Assert.Equal(500, dest.Width);
        Assert.Equal(500, dest.Height);
    }

    [Fact]
    public void ComputeDestRect_CustomScale50_HalfActual()
    {
        // 1 inch source, 50% scale, 100 dpi → 50 pixels.
        var cell = new PrintLayout.Rect(0, 0, 1000, 1000);
        var dest = PrintLayout.ComputeDestRect(
            cell, bitmapW: 100, bitmapH: 100,
            pdfPointsW: 72, pdfPointsH: 72,
            dpiX: 100, dpiY: 100,
            fitMode: PrintFitMode.CustomScale,
            customScalePercent: 50);
        Assert.Equal(50, dest.Width);
        Assert.Equal(50, dest.Height);
    }
}
```

- [ ] **Step 2: Run, expect failure**

Run: `dotnet test Caly.Tests --filter "FullyQualifiedName~PrintLayoutFitTests"`
Expected: CS0103 / CS0117 — `ComputeDestRect` not defined; possibly `PrintFitMode` not in scope.

- [ ] **Step 3: Implement `ComputeDestRect`**

In `Printing/Caly.Printing.Core/PrintLayout.cs`, add `using Caly.Core.Services.Interfaces;` at the top, then append the method to the class:

```csharp
/// <summary>
/// Returns the printer-pixel rectangle to draw a single PDF page into, centered in
/// <paramref name="cell"/>, sized according to <paramref name="fitMode"/>.
/// </summary>
public static Rect ComputeDestRect(
    Rect cell,
    int bitmapW,
    int bitmapH,
    float pdfPointsW,
    float pdfPointsH,
    int dpiX,
    int dpiY,
    PrintFitMode fitMode,
    int customScalePercent)
{
    int targetW;
    int targetH;

    switch (fitMode)
    {
        case PrintFitMode.FitToPage:
            (targetW, targetH) = FitPreservingAspect(cell.Width, cell.Height, bitmapW, bitmapH);
            break;

        case PrintFitMode.ActualSize:
            targetW = (int)(pdfPointsW * dpiX / 72f);
            targetH = (int)(pdfPointsH * dpiY / 72f);
            break;

        case PrintFitMode.ShrinkToFit:
        {
            int actualW = (int)(pdfPointsW * dpiX / 72f);
            int actualH = (int)(pdfPointsH * dpiY / 72f);
            if (actualW <= cell.Width && actualH <= cell.Height)
            {
                targetW = actualW;
                targetH = actualH;
            }
            else
            {
                (targetW, targetH) = FitPreservingAspect(cell.Width, cell.Height, actualW, actualH);
            }
            break;
        }

        case PrintFitMode.CustomScale:
            int scale = Math.Clamp(customScalePercent, 10, 400);
            targetW = (int)(pdfPointsW * dpiX / 72f * scale / 100f);
            targetH = (int)(pdfPointsH * dpiY / 72f * scale / 100f);
            break;

        default:
            throw new ArgumentOutOfRangeException(nameof(fitMode), fitMode, null);
    }

    int x = cell.X + (cell.Width - targetW) / 2;
    int y = cell.Y + (cell.Height - targetH) / 2;
    return new Rect(x, y, targetW, targetH);
}

private static (int W, int H) FitPreservingAspect(int boxW, int boxH, int srcW, int srcH)
{
    if (srcW <= 0 || srcH <= 0)
    {
        return (boxW, boxH);
    }
    float boxAspect = (float)boxW / boxH;
    float srcAspect = (float)srcW / srcH;
    if (srcAspect >= boxAspect)
    {
        return (boxW, (int)(boxW / srcAspect));
    }
    return ((int)(boxH * srcAspect), boxH);
}
```

- [ ] **Step 4: Run, expect PASS**

Run: `dotnet test Caly.Tests --filter "FullyQualifiedName~PrintLayoutFitTests"`
Expected: 5 tests pass.

- [ ] **Step 5: Commit**

```bash
git add Printing/Caly.Printing.Core/PrintLayout.cs Caly.Tests/PrintLayoutTests.cs
git commit -m "Add PrintLayout.ComputeDestRect for fit mode placement"
```

---

## Task 7: TDD `IppAttributeMapping`

**Files:**
- Create: `Printing/Caly.Printing.Core/IppAttributeMapping.cs`
- Create: `Caly.Tests/IppAttributeMappingTests.cs`

- [ ] **Step 1: Write failing tests**

Create `Caly.Tests/IppAttributeMappingTests.cs`:

```csharp
using Caly.Core.Services.Interfaces;
using Caly.Printing.Core;

namespace Caly.Tests;

public class IppAttributeMappingTests
{
    private static PrinterCapabilities AllSupported() => new(
        SupportsLandscape: true,
        IsColorDevice: true,
        SupportsMonochromeDirective: true,
        SupportedNumberUp: [1, 2, 4]);

    [Fact]
    public void MapOrientation_Portrait_Returns3()
    {
        var settings = new PrintSettings(Orientation: PrintOrientation.Portrait);
        Assert.Equal(3, IppAttributeMapping.MapOrientation(settings, AllSupported()));
    }

    [Fact]
    public void MapOrientation_Landscape_WhenSupported_Returns4()
    {
        var settings = new PrintSettings(Orientation: PrintOrientation.Landscape);
        Assert.Equal(4, IppAttributeMapping.MapOrientation(settings, AllSupported()));
    }

    [Fact]
    public void MapOrientation_Landscape_WhenNotSupported_ReturnsNull()
    {
        var settings = new PrintSettings(Orientation: PrintOrientation.Landscape);
        var caps = AllSupported() with { SupportsLandscape = false };
        Assert.Null(IppAttributeMapping.MapOrientation(settings, caps));
    }

    [Fact]
    public void MapOrientation_Auto_ReturnsNull()
    {
        var settings = new PrintSettings(Orientation: PrintOrientation.Auto);
        Assert.Null(IppAttributeMapping.MapOrientation(settings, AllSupported()));
    }

    [Fact]
    public void MapColorMode_Mono_WhenSupported_ReturnsMonochrome()
    {
        var settings = new PrintSettings(ColorMode: PrintColorMode.Monochrome);
        Assert.Equal("monochrome", IppAttributeMapping.MapColorMode(settings, AllSupported()));
    }

    [Fact]
    public void MapColorMode_Mono_WhenNotSupported_ReturnsNull()
    {
        var settings = new PrintSettings(ColorMode: PrintColorMode.Monochrome);
        var caps = AllSupported() with { SupportsMonochromeDirective = false };
        Assert.Null(IppAttributeMapping.MapColorMode(settings, caps));
    }

    [Fact]
    public void MapColorMode_Color_ReturnsNull()
    {
        var settings = new PrintSettings(ColorMode: PrintColorMode.Color);
        Assert.Null(IppAttributeMapping.MapColorMode(settings, AllSupported()));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(4, 4)]
    public void MapNumberUp_Supported_PassesThrough(int requested, int expected)
    {
        var settings = new PrintSettings(PagesPerSheet: requested);
        Assert.Equal(expected, IppAttributeMapping.MapNumberUp(settings, AllSupported()));
    }

    [Fact]
    public void MapNumberUp_NotSupported_FallsBackToOne()
    {
        var settings = new PrintSettings(PagesPerSheet: 2);
        var caps = AllSupported() with { SupportedNumberUp = new[] { 1 } };
        Assert.Equal(1, IppAttributeMapping.MapNumberUp(settings, caps));
    }

    [Theory]
    [InlineData(PrintFitMode.FitToPage, IppAttributeMapping.IppPrintScaling.Fit)]
    [InlineData(PrintFitMode.ActualSize, IppAttributeMapping.IppPrintScaling.None)]
    [InlineData(PrintFitMode.ShrinkToFit, IppAttributeMapping.IppPrintScaling.AutoFit)]
    [InlineData(PrintFitMode.CustomScale, IppAttributeMapping.IppPrintScaling.None)]
    public void MapFitMode_ReturnsExpectedScaling(PrintFitMode fit, IppAttributeMapping.IppPrintScaling expected)
    {
        var settings = new PrintSettings(FitMode: fit);
        Assert.Equal(expected, IppAttributeMapping.MapFitMode(settings));
    }
}
```

- [ ] **Step 2: Run, expect failure**

Run: `dotnet test Caly.Tests --filter "FullyQualifiedName~IppAttributeMappingTests"`
Expected: CS0103 — `IppAttributeMapping` not defined.

- [ ] **Step 3: Implement `IppAttributeMapping`**

Create `Printing/Caly.Printing.Core/IppAttributeMapping.cs`:

```csharp
// Copyright (c) 2025 BobLd
// Licensed under the MIT License — see LICENSE in the repo root.

using Caly.Core.Services.Interfaces;

namespace Caly.Printing.Core;

/// <summary>
/// Pure mapping from <see cref="PrintSettings"/> + <see cref="PrinterCapabilities"/>
/// to primitive IPP attribute values. Kept platform-agnostic and SharpIPP-agnostic
/// so that the Unix service can adapt the primitives to its concrete enum types
/// while the mapping logic stays unit-testable.
/// </summary>
public static class IppAttributeMapping
{
    /// <summary>
    /// Mirrors RFC 8011 print-scaling values, expressed as an enum so callers do not
    /// hand-write strings.
    /// </summary>
    public enum IppPrintScaling { None, Fit, AutoFit }

    /// <summary>
    /// Returns the IPP <c>orientation-requested</c> integer (3=portrait, 4=landscape)
    /// or <c>null</c> when the printer should be left to default
    /// (Auto — printer-side default; Landscape on a non-supporting printer).
    /// </summary>
    public static int? MapOrientation(PrintSettings settings, PrinterCapabilities caps)
    {
        return settings.Orientation switch
        {
            PrintOrientation.Portrait => 3,
            PrintOrientation.Landscape => caps.SupportsLandscape ? 4 : null,
            PrintOrientation.Auto => null,
            _ => null,
        };
    }

    /// <summary>
    /// Returns the IPP <c>print-color-mode</c> string ("monochrome" / "color") or
    /// <c>null</c> when no directive should be sent (printer can't honor mono → use
    /// app-side grayscale; or user wants color and we'd rather inherit printer default).
    /// </summary>
    public static string? MapColorMode(PrintSettings settings, PrinterCapabilities caps)
    {
        if (settings.ColorMode == PrintColorMode.Monochrome)
        {
            return caps.SupportsMonochromeDirective ? "monochrome" : null;
        }
        return null;
    }

    /// <summary>
    /// Returns the IPP <c>number-up</c> value, falling back to 1 if the printer does
    /// not support the requested value.
    /// </summary>
    public static int MapNumberUp(PrintSettings settings, PrinterCapabilities caps)
    {
        return caps.SupportedNumberUp.Contains(settings.PagesPerSheet)
            ? settings.PagesPerSheet
            : 1;
    }

    /// <summary>
    /// Returns the IPP <c>print-scaling</c> value to send for a given fit mode.
    /// CustomScale is rendered app-side, so we send <c>None</c>.
    /// </summary>
    public static IppPrintScaling MapFitMode(PrintSettings settings)
    {
        return settings.FitMode switch
        {
            PrintFitMode.FitToPage    => IppPrintScaling.Fit,
            PrintFitMode.ActualSize   => IppPrintScaling.None,
            PrintFitMode.ShrinkToFit  => IppPrintScaling.AutoFit,
            PrintFitMode.CustomScale  => IppPrintScaling.None,
            _ => IppPrintScaling.Fit,
        };
    }

    /// <summary>
    /// Returns true when the bitmap should be converted to grayscale before transport
    /// because the printer cannot be told to print in monochrome.
    /// </summary>
    public static bool NeedsAppSideGrayscale(PrintSettings settings, PrinterCapabilities caps)
    {
        return settings.ColorMode == PrintColorMode.Monochrome
               && !caps.SupportsMonochromeDirective;
    }
}
```

- [ ] **Step 4: Run, expect PASS**

Run: `dotnet test Caly.Tests --filter "FullyQualifiedName~IppAttributeMappingTests"`
Expected: 12 tests pass.

- [ ] **Step 5: Commit**

```bash
git add Printing/Caly.Printing.Core/IppAttributeMapping.cs Caly.Tests/IppAttributeMappingTests.cs
git commit -m "Add IppAttributeMapping for PrintSettings -> IPP primitives"
```

---

## Task 8: Add Win32 P/Invokes to `NativeMethods.txt`

**Files:**
- Modify: `Printing/Caly.Printing.Windows/NativeMethods.txt`

- [ ] **Step 1: Append entries**

Edit `Printing/Caly.Printing.Windows/NativeMethods.txt`. After the existing line `StretchDIBits`, add:

```
OpenPrinter
ClosePrinter
DocumentProperties
ResetDC
DeviceCapabilities
DEVMODEW
```

- [ ] **Step 2: Build to verify CsWin32 generates the symbols**

Run: `dotnet build Printing/Caly.Printing.Windows -c Debug`
Expected: build succeeds. CsWin32 generates `PInvoke.OpenPrinter`, `PInvoke.ClosePrinter`, `PInvoke.DocumentPropertiesW`, `PInvoke.ResetDCW`, `PInvoke.DeviceCapabilitiesW`, and the `DEVMODEW` struct.

If `DocumentProperties` is ambiguous (CsWin32 may report A/W variants), specify explicitly: replace `DocumentProperties` with `DocumentPropertiesW` and `ResetDC` with `ResetDCW`, `DeviceCapabilities` with `DeviceCapabilitiesW`.

- [ ] **Step 3: Commit**

```bash
git add Printing/Caly.Printing.Windows/NativeMethods.txt
git commit -m "Add OpenPrinter/DocumentProperties/ResetDC P/Invokes"
```

---

## Task 9: Implement `WindowsPrintService.GetPrinterCapabilitiesAsync`

**Files:**
- Modify: `Printing/Caly.Printing.Windows/WindowsPrintService.cs`

This task is manual-tested. There is no unit test (Win32 spooler dependency).

- [ ] **Step 1: Add capability constants and helper**

Inside `WindowsPrintService` (Windows-only class), add:

```csharp
// DeviceCapabilities indices (winddi.h)
private const uint DC_ORIENTATION = 17;
private const uint DC_COLORDEVICE = 32;

private static unsafe int QueryDeviceCap(string printerName, uint index)
{
    fixed (char* p = printerName)
    {
        return PInvoke.DeviceCapabilitiesW(
            new Windows.Win32.Foundation.PCWSTR(p),
            default,                  // pszPort: NULL → use the printer's default port
            (Windows.Win32.Graphics.Gdi.PRINTER_DEVICE_CAPABILITIES)index,
            default,                  // pszOutput: NULL → returns the value, not a list
            null);                    // pDevMode: NULL → use defaults
    }
}
```

> Note: the exact CsWin32 type name for the third parameter may be different in your CsWin32 version. If the build complains, check the generated signature for `DeviceCapabilitiesW` and adjust the cast — the underlying value (the Windows define `DC_*`) is what matters.

- [ ] **Step 2: Replace the stub `GetPrinterCapabilitiesAsync`**

```csharp
public Task<PrinterCapabilities> GetPrinterCapabilitiesAsync(
    PrinterInfo printer, CancellationToken token)
{
    return Task.Run(() =>
    {
        Debug.ThrowOnUiThread();
        token.ThrowIfCancellationRequested();

        bool supportsLandscape = false;
        bool isColorDevice = false;

        try
        {
            // DC_ORIENTATION returns 90 if landscape is supported, 0 otherwise.
            int orient = QueryDeviceCap(printer.Name, DC_ORIENTATION);
            supportsLandscape = (orient == 90);

            // DC_COLORDEVICE returns 1 for color, 0 for monochrome-only.
            int color = QueryDeviceCap(printer.Name, DC_COLORDEVICE);
            isColorDevice = (color == 1);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"WindowsPrintService: DeviceCapabilities failed for '{printer.Name}': {ex.Message}");
        }

        return new PrinterCapabilities(
            SupportsLandscape: supportsLandscape,
            IsColorDevice: isColorDevice,
            SupportsMonochromeDirective: isColorDevice, // every color-capable Windows driver honors DMCOLOR_MONOCHROME
            SupportedNumberUp: [1, 2, 4]);              // GDI does not expose N-up; we tile app-side
    }, token);
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build -c Debug`
Expected: success.

- [ ] **Step 4: Manual smoke test**

In the IDE / from `dotnet run --project Caly.Desktop`, open the print dialog. Confirm no exception when a printer is selected. (Capability isn't yet wired into the UI, so this just tests the call path.)

- [ ] **Step 5: Commit**

```bash
git add Printing/Caly.Printing.Windows/WindowsPrintService.cs
git commit -m "Query printer capabilities via DeviceCapabilities on Windows"
```

---

## Task 10: Build and use DEVMODE on Windows

**Files:**
- Modify: `Printing/Caly.Printing.Windows/WindowsPrintService.cs`

- [ ] **Step 1: Add DEVMODE constants and helper**

Inside `WindowsPrintService`:

```csharp
// DEVMODE flags (wingdi.h)
private const uint DM_ORIENTATION = 0x00000001;
private const uint DM_COLOR = 0x00000800;

// dmOrientation values
private const short DMORIENT_PORTRAIT = 1;
private const short DMORIENT_LANDSCAPE = 2;

// dmColor values
private const short DMCOLOR_MONOCHROME = 1;
private const short DMCOLOR_COLOR = 2;

// DocumentProperties fMode flags (winspool.h)
private const uint DM_OUT_BUFFER = 2;
private const uint DM_IN_BUFFER  = 8;

/// <summary>
/// Opens the printer, gets a default DEVMODE, mutates orientation and color according
/// to <paramref name="settings"/> and <paramref name="caps"/>, and round-trips it through
/// DocumentProperties so the driver merges defaults. Returns the buffer (caller frees nothing —
/// it is a managed byte[]).
/// </summary>
private static unsafe byte[]? BuildDevModeBuffer(string printerName, PrintSettings settings, PrinterCapabilities caps)
{
    Windows.Win32.Foundation.HANDLE hPrinter;
    if (!PInvoke.OpenPrinter(printerName, out hPrinter, null))
    {
        return null;
    }

    try
    {
        // Step 1: ask for required DEVMODE size.
        int needed;
        fixed (char* pname = printerName)
        {
            needed = PInvoke.DocumentPropertiesW(
                default,
                hPrinter,
                new Windows.Win32.Foundation.PCWSTR(pname),
                null, null, 0);
        }
        if (needed <= 0)
        {
            return null;
        }

        var buffer = new byte[needed];

        // Step 2: fill with default DEVMODE.
        fixed (byte* p = buffer)
        fixed (char* pname = printerName)
        {
            int r = PInvoke.DocumentPropertiesW(
                default,
                hPrinter,
                new Windows.Win32.Foundation.PCWSTR(pname),
                (Windows.Win32.Graphics.Gdi.DEVMODEW*)p,
                null,
                DM_OUT_BUFFER);
            if (r < 0)
            {
                return null;
            }
        }

        // Step 3: mutate orientation + color.
        fixed (byte* p = buffer)
        {
            var dm = (Windows.Win32.Graphics.Gdi.DEVMODEW*)p;

            short orient = settings.Orientation == PrintOrientation.Landscape
                ? DMORIENT_LANDSCAPE
                : DMORIENT_PORTRAIT;
            dm->Anonymous1.Anonymous1.dmOrientation = orient;
            dm->dmFields |= DM_ORIENTATION;

            if (settings.ColorMode == PrintColorMode.Monochrome && caps.SupportsMonochromeDirective)
            {
                dm->dmColor = DMCOLOR_MONOCHROME;
                dm->dmFields |= DM_COLOR;
            }
        }

        // Step 4: validate / merge driver defaults.
        fixed (byte* p = buffer)
        fixed (char* pname = printerName)
        {
            int r = PInvoke.DocumentPropertiesW(
                default,
                hPrinter,
                new Windows.Win32.Foundation.PCWSTR(pname),
                (Windows.Win32.Graphics.Gdi.DEVMODEW*)p,
                (Windows.Win32.Graphics.Gdi.DEVMODEW*)p,
                DM_IN_BUFFER | DM_OUT_BUFFER);
            if (r < 0)
            {
                return null;
            }
        }

        return buffer;
    }
    finally
    {
        PInvoke.ClosePrinter(hPrinter);
    }
}
```

> If CsWin32's generated `DEVMODEW` field names differ (e.g. union flattening), adjust the field accesses; on .NET 9 / 10 with CsWin32 0.3.x, `dmOrientation` typically lives in `Anonymous1.Anonymous1`. The build error will tell you the right path.

- [ ] **Step 2: Use the DEVMODE in `PrintWindowsCoreAsync`**

Replace the existing `PInvoke.CreateDCW(null, printer.Name, null, null);` line with a DEVMODE-aware version. Around the existing call, restructure:

```csharp
private static async Task PrintWindowsCoreAsync(
    PrinterInfo printer,
    IPdfDocumentService documentService,
    IReadOnlyList<PrintPageInfo> pages,
    PrintSettings settings,
    IProgress<int>? progress,
    CancellationToken token)
{
    Debug.ThrowOnUiThread();

    var caps = await new WindowsPrintService().GetPrinterCapabilitiesAsync(printer, token).ConfigureAwait(false);
    var devMode = BuildDevModeBuffer(printer.Name, settings, caps);

    HDC hdc;
    unsafe
    {
        if (devMode is not null)
        {
            fixed (byte* pdm = devMode)
            {
                hdc = PInvoke.CreateDCW(null, printer.Name, null,
                    (Windows.Win32.Graphics.Gdi.DEVMODEW*)pdm);
            }
        }
        else
        {
            hdc = PInvoke.CreateDCW(null, printer.Name, null, null);
        }
    }

    if (hdc.IsNull)
    {
        throw new InvalidOperationException(
            $"Cannot create DC for printer '{printer.Name}' (Win32 error {Marshal.GetLastWin32Error()}).");
    }

    // ... rest of existing method body, unchanged for now ...
}
```

> The capability query uses a fresh `WindowsPrintService` instance to avoid threading the existing instance method into a `static`. Refactor later if it becomes a hot path; capability queries take milliseconds.

- [ ] **Step 3: Build, smoke test**

Run: `dotnet build -c Debug`
Then run the app and print a single-page PDF in **Portrait** (default) — output should look identical to before.

- [ ] **Step 4: Manual: print with `Landscape`**

Temporarily hard-code `new PrintSettings(Orientation: PrintOrientation.Landscape)` at the call site (revert before commit) and verify the printer rotates the page. Revert.

- [ ] **Step 5: Commit**

```bash
git add Printing/Caly.Printing.Windows/WindowsPrintService.cs
git commit -m "Build DEVMODE for orientation and color on Windows"
```

---

## Task 11: Wire per-page orientation in Auto mode (Windows)

**Files:**
- Modify: `Printing/Caly.Printing.Windows/WindowsPrintService.cs`

- [ ] **Step 1: Pass settings + caps + DEVMODE buffer through the per-page loop**

Refactor `PrintWindowsCoreAsync` so the page loop has access to `settings`, `caps`, the `devMode` buffer, and the page's PDF dimensions. The PDF dimensions come from `documentService.GetPageSizeAsync` — but `RenderPageToBitmapAsync` already calls that internally. Add a parallel `GetPageSizeAsync` call before `RenderPageToBitmapAsync` to know the orientation.

- [ ] **Step 2: Add orientation-flip logic before each `StartPage`**

Replace the body of the `foreach (var pageInfo in pages)` loop's pre-`StartPage` section with:

```csharp
foreach (var pageInfo in pages)
{
    token.ThrowIfCancellationRequested();

    var pageSize = await documentService.GetPageSizeAsync(pageInfo.PageNumber, token).ConfigureAwait(false);

    // Auto orientation: flip DEVMODE if this page is wider than tall.
    if (settings.Orientation == PrintOrientation.Auto && devMode is not null && pageSize is not null)
    {
        bool wantLandscape = PrintLayout.ShouldRotateForAutoOrientation(
            (float)pageSize.Value.Width, (float)pageSize.Value.Height);
        SetDevModeOrientation(devMode, wantLandscape ? DMORIENT_LANDSCAPE : DMORIENT_PORTRAIT);

        unsafe
        {
            fixed (byte* pdm = devMode)
            {
                _ = PInvoke.ResetDCW(hdc, (Windows.Win32.Graphics.Gdi.DEVMODEW*)pdm);
            }
        }

        printerW = PInvoke.GetDeviceCaps(hdc, GET_DEVICE_CAPS_INDEX.HORZRES);
        printerH = PInvoke.GetDeviceCaps(hdc, GET_DEVICE_CAPS_INDEX.VERTRES);
    }

    using var bitmap = await PrintServiceHelper.RenderPageToBitmapAsync(documentService, pageInfo, token)
        .ConfigureAwait(false);

    token.ThrowIfCancellationRequested();

    if (PInvoke.StartPage(hdc) <= 0)
    {
        throw new InvalidOperationException($"StartPage failed (Win32 error {Marshal.GetLastWin32Error()}).");
    }

    try
    {
        if (bitmap is not null)
        {
            DrawBitmapToHdc(hdc, bitmap, printerW, printerH);  // unchanged for now; replaced in Task 13
        }
    }
    finally
    {
        _ = PInvoke.EndPage(hdc);
    }

    progress?.Report(++pagesProcessed);
}
```

Add the helper:

```csharp
private static unsafe void SetDevModeOrientation(byte[] buffer, short orientation)
{
    fixed (byte* p = buffer)
    {
        var dm = (Windows.Win32.Graphics.Gdi.DEVMODEW*)p;
        dm->Anonymous1.Anonymous1.dmOrientation = orientation;
        dm->dmFields |= DM_ORIENTATION;
    }
}
```

- [ ] **Step 3: Build, manual test**

Build, then print a 2-page PDF where page 1 is portrait and page 2 is landscape (e.g., a presentation PDF). Confirm both pages print in their correct orientation when `PrintOrientation.Auto` is used. Hard-code the setting temporarily at the call site for testing.

- [ ] **Step 4: Commit**

```bash
git add Printing/Caly.Printing.Windows/WindowsPrintService.cs
git commit -m "Apply per-page orientation in Auto mode via ResetDC on Windows"
```

---

## Task 12: Wire grayscale fallback (Windows)

**Files:**
- Modify: `Printing/Caly.Printing.Windows/WindowsPrintService.cs`

- [ ] **Step 1: Detect whether DEVMODE preserved DM_COLOR**

After the DEVMODE round-trip in `BuildDevModeBuffer`, the caller needs to know whether the driver kept the `DM_COLOR` change. Refactor `BuildDevModeBuffer` to return both buffer and that flag:

```csharp
private static unsafe (byte[]? Buffer, bool ColorDirectiveAccepted) BuildDevModeBuffer(
    string printerName, PrintSettings settings, PrinterCapabilities caps)
{
    // ... existing body up through Step 4 of the round-trip ...

    bool colorOk;
    fixed (byte* p = buffer)
    {
        var dm = (Windows.Win32.Graphics.Gdi.DEVMODEW*)p;
        colorOk = (dm->dmFields & DM_COLOR) != 0
                  && (settings.ColorMode != PrintColorMode.Monochrome || dm->dmColor == DMCOLOR_MONOCHROME);
    }

    return (buffer, colorOk);
}
```

Update the call site in `PrintWindowsCoreAsync` to receive both values.

- [ ] **Step 2: Convert bitmap when fallback is needed**

Inside the page loop, just before `DrawBitmapToHdc`:

```csharp
if (settings.ColorMode == PrintColorMode.Monochrome && !colorDirectiveAccepted && bitmap is not null)
{
    PrintServiceHelper.ConvertToGrayscaleInPlace(bitmap);
}
```

- [ ] **Step 3: Build, manual test**

Build. Manual test: print a color PDF page with the temporarily-hard-coded `ColorMode = Monochrome`. On a color-capable printer, the printer should produce grayscale output (DEVMODE path). Force the fallback by temporarily overriding `colorDirectiveAccepted = false` to verify the conversion path also produces grayscale output. Revert overrides.

- [ ] **Step 4: Commit**

```bash
git add Printing/Caly.Printing.Windows/WindowsPrintService.cs
git commit -m "Add app-side grayscale fallback for monochrome on Windows"
```

---

## Task 13: Replace `DrawBitmapToHdc` with `DrawSheetToHdc` (Windows N-up + Fit modes)

**Files:**
- Modify: `Printing/Caly.Printing.Windows/WindowsPrintService.cs`

- [ ] **Step 1: Add the new method**

Append to `WindowsPrintService`:

```csharp
/// <summary>
/// Draws zero-or-more page bitmaps onto the printer DC using the cell layout from
/// <see cref="PrintLayout.ComputeCells"/>. Each cell is sized according to <paramref name="settings"/>'s
/// fit mode. Used inside a single StartPage / EndPage block.
/// </summary>
private static unsafe void DrawSheetToHdc(
    HDC hdc,
    IReadOnlyList<(SKBitmap Bitmap, float PdfPointsW, float PdfPointsH)> sourcePages,
    PrintSettings settings,
    int printerW,
    int printerH,
    int dpiX,
    int dpiY)
{
    var cells = PrintLayout.ComputeCells(printerW, printerH, settings.PagesPerSheet);

    for (int i = 0; i < sourcePages.Count && i < cells.Count; i++)
    {
        var (bitmap, ppW, ppH) = sourcePages[i];
        if (bitmap is null) continue;

        // 2-up rotation: rotate each page 90° to fit two portrait PDFs on portrait paper.
        // Implemented by swapping bitmap aspect dimensions in the dest math; the actual
        // pixel rotation happens in StretchDIBits via swapped src dimensions and a 90°
        // setup. For simplicity in v1 we keep StretchDIBits as-is and rely on cell
        // splitting along the longer paper edge to produce a sensible result without
        // explicit per-bitmap rotation. (Future improvement: pre-rotate the bitmap.)

        int effW = bitmap.Width;
        int effH = bitmap.Height;
        float effPpW = ppW;
        float effPpH = ppH;

        var dest = PrintLayout.ComputeDestRect(
            cells[i], effW, effH, effPpW, effPpH, dpiX, dpiY,
            settings.FitMode, settings.CustomScalePercent);

        BITMAPINFO bmi = default;
        bmi.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
        bmi.bmiHeader.biWidth = bitmap.Width;
        bmi.bmiHeader.biHeight = -bitmap.Height;
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = 0;

        _ = PInvoke.StretchDIBits(hdc,
            dest.X, dest.Y, dest.Width, dest.Height,
            0, 0, bitmap.Width, bitmap.Height,
            (void*)bitmap.GetPixels(),
            &bmi,
            DIB_USAGE.DIB_RGB_COLORS,
            ROP_CODE.SRCCOPY);
    }
}
```

> The 2-up rotation note in the spec is acknowledged here as a deliberate v1 simplification. The cell layout itself splits along the paper's longer edge, which gives correct fit on landscape paper for 2-up; on portrait paper, 2-up stacks pages top/bottom rather than rotating. This is documented as a known cosmetic difference; the spec's "Adobe-style rotation" can be added in a follow-up. The unit tests in Task 5 already lock in the cell-splitting behavior.

- [ ] **Step 2: Restructure the page loop to gather N pages per sheet**

Replace the existing page-loop section so it groups the input `pages` into chunks of `settings.PagesPerSheet`, renders each, then issues one `StartPage / EndPage` per chunk:

```csharp
int dpiX = PInvoke.GetDeviceCaps(hdc, GET_DEVICE_CAPS_INDEX.LOGPIXELSX);
int dpiY = PInvoke.GetDeviceCaps(hdc, GET_DEVICE_CAPS_INDEX.LOGPIXELSY);

int idx = 0;
while (idx < pages.Count)
{
    token.ThrowIfCancellationRequested();

    int chunkSize = Math.Min(settings.PagesPerSheet, pages.Count - idx);
    var renderedChunk = new List<(SKBitmap Bitmap, float PdfPointsW, float PdfPointsH)>(chunkSize);

    for (int j = 0; j < chunkSize; j++)
    {
        var pi = pages[idx + j];
        var size = await documentService.GetPageSizeAsync(pi.PageNumber, token).ConfigureAwait(false);
        var bm = await PrintServiceHelper.RenderPageToBitmapAsync(documentService, pi, token).ConfigureAwait(false);
        if (bm is not null && size is not null)
        {
            if (settings.ColorMode == PrintColorMode.Monochrome && !colorDirectiveAccepted)
            {
                PrintServiceHelper.ConvertToGrayscaleInPlace(bm);
            }
            renderedChunk.Add((bm, (float)size.Value.Width, (float)size.Value.Height));
        }
    }

    // Auto-orientation: flip DEVMODE based on the FIRST page in the chunk.
    if (settings.Orientation == PrintOrientation.Auto && devMode is not null && renderedChunk.Count > 0)
    {
        bool wantLandscape = PrintLayout.ShouldRotateForAutoOrientation(
            renderedChunk[0].PdfPointsW, renderedChunk[0].PdfPointsH);
        SetDevModeOrientation(devMode, wantLandscape ? DMORIENT_LANDSCAPE : DMORIENT_PORTRAIT);

        unsafe
        {
            fixed (byte* pdm = devMode)
            {
                _ = PInvoke.ResetDCW(hdc, (Windows.Win32.Graphics.Gdi.DEVMODEW*)pdm);
            }
        }

        printerW = PInvoke.GetDeviceCaps(hdc, GET_DEVICE_CAPS_INDEX.HORZRES);
        printerH = PInvoke.GetDeviceCaps(hdc, GET_DEVICE_CAPS_INDEX.VERTRES);
    }

    if (PInvoke.StartPage(hdc) <= 0)
    {
        foreach (var (bm, _, _) in renderedChunk) bm.Dispose();
        throw new InvalidOperationException($"StartPage failed (Win32 error {Marshal.GetLastWin32Error()}).");
    }

    try
    {
        DrawSheetToHdc(hdc, renderedChunk, settings, printerW, printerH, dpiX, dpiY);
    }
    finally
    {
        _ = PInvoke.EndPage(hdc);
        foreach (var (bm, _, _) in renderedChunk) bm.Dispose();
    }

    idx += chunkSize;
    progress?.Report(idx);
}
```

Remove the now-unused `DrawBitmapToHdc` method.

- [ ] **Step 3: Build, manual smoke**

Run: `dotnet build -c Debug` and print a 4-page PDF with `PagesPerSheet=2` (hard-coded in VM call site). Verify two PDF pages appear per printed sheet.

Repeat with `PagesPerSheet=4`, `FitMode=ActualSize`, `CustomScale=50`. Revert hard-coded settings.

- [ ] **Step 4: Commit**

```bash
git add Printing/Caly.Printing.Windows/WindowsPrintService.cs
git commit -m "Replace DrawBitmapToHdc with N-up + fit-aware DrawSheetToHdc"
```

---

## Task 14: Implement Unix `GetPrinterCapabilitiesAsync`

**Files:**
- Modify: `Printing/Caly.Printing.Unix/UnixPrintService.cs`

This task is manual-tested (CUPS dependency).

- [ ] **Step 1: Replace the stub**

In `UnixPrintService`, replace the stub `GetPrinterCapabilitiesAsync` with:

```csharp
public async Task<PrinterCapabilities> GetPrinterCapabilitiesAsync(
    PrinterInfo printer, CancellationToken token)
{
    Debug.ThrowOnUiThread();

    if (printer.IppUri is null)
    {
        return ConservativeDefaults();
    }

    try
    {
        var request = new GetPrinterAttributesRequest
        {
            OperationAttributes = new GetPrinterAttributesOperationAttributes
            {
                PrinterUri = printer.IppUri,
                RequestingUserName = Environment.UserName
            }
        };

        var response = await _ippClient.GetPrinterAttributesAsync(request, token).ConfigureAwait(false);
        var attrs = response.PrinterAttributes;
        if (attrs is null)
        {
            return ConservativeDefaults();
        }

        // SharpIPP exposes typed properties on PrinterAttributes; the names below
        // match SharpIppNext 3.x. Adjust if the version differs.
        bool supportsLandscape = attrs.OrientationRequestedSupported is { Length: > 0 } orient
            && Array.Exists(orient, o => (int)o == 4);

        var modes = attrs.PrintColorModeSupported;
        bool isColor = modes is { Length: > 0 }
            && Array.Exists(modes, m => string.Equals(m, "color", StringComparison.OrdinalIgnoreCase));
        bool monoDirective = modes is { Length: > 0 }
            && Array.Exists(modes, m => string.Equals(m, "monochrome", StringComparison.OrdinalIgnoreCase));

        if (modes is null || modes.Length == 0)
        {
            // Attribute missing → assume color (most printers are) and monochrome supported.
            isColor = true;
            monoDirective = true;
        }

        IReadOnlyList<int> nUp = attrs.NumberUpSupported is { Length: > 0 } supported
            ? supported.Where(n => n is 1 or 2 or 4).ToArray()
            : new[] { 1, 2, 4 };
        if (nUp.Count == 0) nUp = new[] { 1 };

        return new PrinterCapabilities(
            SupportsLandscape: supportsLandscape,
            IsColorDevice: isColor,
            SupportsMonochromeDirective: monoDirective,
            SupportedNumberUp: nUp);
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"UnixPrintService: Get-Printer-Attributes failed: {ex.Message}");
        return ConservativeDefaults();
    }
}

private static PrinterCapabilities ConservativeDefaults() => new(
    SupportsLandscape: true,
    IsColorDevice: true,
    SupportsMonochromeDirective: true,
    SupportedNumberUp: [1, 2, 4]);
```

> **Note for the implementer:** SharpIppNext 3.x property names might differ slightly. If `attrs.OrientationRequestedSupported`, `attrs.PrintColorModeSupported`, or `attrs.NumberUpSupported` aren't present under those names, look for the closest matches in IntelliSense (`OrientationRequestedSupported`, `OrientationsRequestedSupported`, etc.) and adjust. The semantics — "is value 4 in the list?", "is 'monochrome' in the list?" — stay the same.

- [ ] **Step 2: Build, smoke test**

Build. On Linux/macOS: open the print dialog with a real CUPS printer selected. No exception should be thrown. Capability values aren't yet visible in the UI; this validates the call path.

- [ ] **Step 3: Commit**

```bash
git add Printing/Caly.Printing.Unix/UnixPrintService.cs
git commit -m "Query printer capabilities via Get-Printer-Attributes on Unix"
```

---

## Task 15: Wire `JobTemplateAttributes` from settings on Unix

**Files:**
- Modify: `Printing/Caly.Printing.Unix/UnixPrintService.cs`

- [ ] **Step 1: Pass settings + caps through `PrintIppAsync`**

Update `PrintIppAsync` signature to accept `PrintSettings settings`. Capability is queried at the start of the method:

```csharp
private async Task PrintIppAsync(
    PrinterInfo printer,
    IPdfDocumentService documentService,
    IReadOnlyList<PrintPageInfo> pages,
    PrintSettings settings,
    IProgress<int>? progress,
    CancellationToken token)
{
    var caps = await GetPrinterCapabilitiesAsync(printer, token).ConfigureAwait(false);

    // ... existing scale and userName setup ...

    var ippScaling = IppAttributeMapping.MapFitMode(settings) switch
    {
        IppAttributeMapping.IppPrintScaling.Fit     => PrintScaling.Fit,
        IppAttributeMapping.IppPrintScaling.None    => PrintScaling.None,
        IppAttributeMapping.IppPrintScaling.AutoFit => PrintScaling.AutoFit,
        _ => PrintScaling.Fit,
    };

    var jobTemplate = new JobTemplateAttributes
    {
        OrientationRequested = MapToSharpIppOrientation(IppAttributeMapping.MapOrientation(settings, caps)),
        PrintColorMode = MapToSharpIppColorMode(IppAttributeMapping.MapColorMode(settings, caps)),
        NumberUp = IppAttributeMapping.MapNumberUp(settings, caps),
        PrintScaling = ippScaling,
    };

    var createRequest = new CreateJobRequest
    {
        OperationAttributes = new CreateJobOperationAttributes
        {
            PrinterUri = printer.IppUri,
            RequestingUserName = userName,
            JobName = docName
        },
        JobTemplateAttributes = jobTemplate
    };

    // ... rest of the existing CreateJob → loop → cleanup body ...
}
```

Add the small adapter helpers at the bottom of the class:

```csharp
private static OrientationRequested? MapToSharpIppOrientation(int? value) => value switch
{
    3 => OrientationRequested.Portrait,
    4 => OrientationRequested.Landscape,
    _ => null,
};

private static PrintColorMode? MapToSharpIppColorMode(string? value) => value switch
{
    "monochrome" => PrintColorMode.Monochrome,
    "color"      => PrintColorMode.Color,
    _ => null,
};
```

> If SharpIppNext's enum values are named differently (e.g. `OrientationRequested.PortraitOrientation`), use the names in IntelliSense and adjust. The mapping must be 3↔portrait and 4↔landscape per RFC 8011.

- [ ] **Step 2: Update the public `PrintDocumentAsync` to forward `settings`**

```csharp
public Task PrintDocumentAsync(
    PrinterInfo printer,
    IPdfDocumentService documentService,
    IReadOnlyList<PrintPageInfo> pages,
    PrintSettings settings,
    IProgress<int>? progress,
    CancellationToken token)
{
    Debug.ThrowOnUiThread();
    return PrintIppAsync(printer, documentService, pages, settings, progress, token);
}
```

- [ ] **Step 3: Build, manual test**

Run: `dotnet build -c Debug`
Linux test: hard-code `new PrintSettings(Orientation: PrintOrientation.Landscape, PagesPerSheet: 2)` and print a 4-page PDF. CUPS should produce 2 sheets, landscape, with 2 pages each.

- [ ] **Step 4: Commit**

```bash
git add Printing/Caly.Printing.Unix/UnixPrintService.cs
git commit -m "Apply PrintSettings via CUPS JobTemplateAttributes"
```

---

## Task 16: Unix per-page Auto rotation, custom-scale render, grayscale fallback

**Files:**
- Modify: `Printing/Caly.Printing.Unix/UnixPrintService.cs`

- [ ] **Step 1: Compute the print scale per page**

In `PrintIppAsync`, replace the constant `printScale = 200f / 72f` line with a per-call decision:

```csharp
// Default 200 DPI render; CustomScale renders at the requested scale so CUPS doesn't
// re-scale the bitmap.
const float baseDpi = 200f;
float baseScale = baseDpi / 72f;
float printScale = settings.FitMode == PrintFitMode.CustomScale
    ? baseScale * Math.Clamp(settings.CustomScalePercent, 10, 400) / 100f
    : baseScale;
```

- [ ] **Step 2: Auto rotation + grayscale per page**

Inside the existing `for (int i = 0; i < pages.Count; i++)` loop, after `RenderPageToBitmapAsync`, before `EncodeJpeg`:

```csharp
if (bitmap is null) continue;

// Auto-orientation: rotate the bitmap if the page is wider than tall.
if (settings.Orientation == PrintOrientation.Auto)
{
    var size = await documentService.GetPageSizeAsync(pages[i].PageNumber, token).ConfigureAwait(false);
    if (size is not null && PrintLayout.ShouldRotateForAutoOrientation((float)size.Value.Width, (float)size.Value.Height))
    {
        // Replace bitmap with a 90°-rotated copy.
        var rotated = RotateBitmap90Cw(bitmap);
        bitmap.Dispose();
        bitmap = rotated;
    }
}

// Grayscale fallback when caps say no.
if (IppAttributeMapping.NeedsAppSideGrayscale(settings, caps))
{
    PrintServiceHelper.ConvertToGrayscaleInPlace(bitmap);
}
```

Add the rotation helper:

```csharp
private static SKBitmap RotateBitmap90Cw(SKBitmap source)
{
    var rotated = new SKBitmap(source.Height, source.Width, source.ColorType, source.AlphaType);
    using var canvas = new SKCanvas(rotated);
    canvas.Translate(rotated.Width, 0);
    canvas.RotateDegrees(90);
    canvas.DrawBitmap(source, 0, 0);
    canvas.Flush();
    return rotated;
}
```

- [ ] **Step 3: Build, manual test**

Build. Linux test: print a multi-page PDF with `Orientation: Auto`, where some pages are landscape — verify each page prints in its correct orientation. Then test `ColorMode: Monochrome` against a printer whose `print-color-mode-supported` does NOT include `monochrome` (rare; can be simulated by hard-coding `caps.SupportsMonochromeDirective = false` temporarily). Verify the output is grayscale.

- [ ] **Step 4: Commit**

```bash
git add Printing/Caly.Printing.Unix/UnixPrintService.cs
git commit -m "Auto-orientation, custom scale, and grayscale fallback on Unix"
```

---

## Task 17: TDD `PrintDialogViewModel` orientation properties

**Files:**
- Modify: `Caly.Core/ViewModels/PrintDialogViewModel.cs`
- Create: `Caly.Tests/PrintDialogViewModelTests.cs`

- [ ] **Step 1: Write failing test**

Create `Caly.Tests/PrintDialogViewModelTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Caly.Core.Services.Interfaces;
using Caly.Core.ViewModels;

namespace Caly.Tests;

public sealed class FakePrintService : IPrintService
{
    public IReadOnlyList<PrinterInfo> Printers { get; set; } = Array.Empty<PrinterInfo>();
    public PrinterCapabilities Capabilities { get; set; } = new(true, true, true, new[] { 1, 2, 4 });

    public Task<IReadOnlyList<PrinterInfo>> GetAvailablePrintersAsync(CancellationToken token = default)
        => Task.FromResult(Printers);

    public Task<PrinterCapabilities> GetPrinterCapabilitiesAsync(PrinterInfo printer, CancellationToken token)
        => Task.FromResult(Capabilities);

    public Task PrintDocumentAsync(
        PrinterInfo printer,
        IPdfDocumentService documentService,
        IReadOnlyList<PrintPageInfo> pages,
        PrintSettings settings,
        IProgress<int>? progress,
        CancellationToken token)
        => Task.CompletedTask;
}

public sealed class FakePdfDocumentService : IPdfDocumentService
{
    public int NumberOfPages => 5;
    public string? FileName => "test.pdf";
    public double PpiScale => 1.0;

    // Stub the rest of IPdfDocumentService — return null / Task.FromResult(null) for the methods
    // touched by the print path, throw NotImplementedException for the rest.
    // The implementer should fill these out based on the live IPdfDocumentService surface.
}

public class PrintDialogViewModelOrientationTests
{
    private static PrintDialogViewModel NewVm()
        => new(new FakePrintService(), new FakePdfDocumentService(), currentPageNumber: 1);

    [Fact]
    public void Orientation_DefaultIsAuto()
    {
        var vm = NewVm();
        Assert.True(vm.IsOrientationAuto);
        Assert.False(vm.IsOrientationPortrait);
        Assert.False(vm.IsOrientationLandscape);
    }

    [Fact]
    public void Orientation_SelectingPortrait_ClearsOthers()
    {
        var vm = NewVm();
        vm.IsOrientationPortrait = true;
        Assert.False(vm.IsOrientationAuto);
        Assert.True(vm.IsOrientationPortrait);
        Assert.False(vm.IsOrientationLandscape);
    }

    [Fact]
    public void Orientation_SelectingLandscape_ClearsOthers()
    {
        var vm = NewVm();
        vm.IsOrientationLandscape = true;
        Assert.False(vm.IsOrientationAuto);
        Assert.False(vm.IsOrientationPortrait);
        Assert.True(vm.IsOrientationLandscape);
    }
}
```

- [ ] **Step 2: Stub `FakePdfDocumentService`**

Open `Caly.Core/Services/Interfaces/IPdfDocumentService.cs` (use Glob if path differs) and list every member. The VM unit tests only exercise the constructor, which reads `NumberOfPages` and `FileName`. Implement those two properties with the values shown in the example fake (5 and "test.pdf"). For every other interface member — every property and method — add an implementation that throws `NotImplementedException()`. The compiler will list them all if you build the test project once with the partial fake; iterate until it builds. Throwing rather than returning defaults ensures any future test that accidentally exercises a real document operation fails loudly rather than silently passing.

- [ ] **Step 3: Run, expect compile failure**

Run: `dotnet test Caly.Tests --filter "FullyQualifiedName~PrintDialogViewModelOrientationTests"`
Expected: CS0117 — `IsOrientationAuto` / `IsOrientationPortrait` / `IsOrientationLandscape` not on VM.

- [ ] **Step 4: Add orientation properties to `PrintDialogViewModel`**

Append in the `[ObservableProperty]` block (right after `_isCustomRangeSelected`):

```csharp
// --- Orientation (mutually exclusive radios) ---

[ObservableProperty] private bool _isOrientationAuto = true;
[ObservableProperty] private bool _isOrientationPortrait;
[ObservableProperty] private bool _isOrientationLandscape;

partial void OnIsOrientationAutoChanged(bool value)
{
    if (!value) return;
    IsOrientationPortrait = false;
    IsOrientationLandscape = false;
}

partial void OnIsOrientationPortraitChanged(bool value)
{
    if (!value) return;
    IsOrientationAuto = false;
    IsOrientationLandscape = false;
}

partial void OnIsOrientationLandscapeChanged(bool value)
{
    if (!value) return;
    IsOrientationAuto = false;
    IsOrientationPortrait = false;
}
```

- [ ] **Step 5: Run, expect PASS**

Run: `dotnet test Caly.Tests --filter "FullyQualifiedName~PrintDialogViewModelOrientationTests"`
Expected: 3 tests pass.

- [ ] **Step 6: Commit**

```bash
git add Caly.Tests/PrintDialogViewModelTests.cs Caly.Core/ViewModels/PrintDialogViewModel.cs
git commit -m "Add orientation properties to PrintDialogViewModel"
```

---

## Task 18: TDD fit-mode properties (incl. custom scale)

**Files:**
- Modify: `Caly.Core/ViewModels/PrintDialogViewModel.cs`
- Modify: `Caly.Tests/PrintDialogViewModelTests.cs`

- [ ] **Step 1: Write failing tests**

Append to `Caly.Tests/PrintDialogViewModelTests.cs`:

```csharp
public class PrintDialogViewModelFitTests
{
    private static PrintDialogViewModel NewVm()
        => new(new FakePrintService(), new FakePdfDocumentService(), currentPageNumber: 1);

    [Fact]
    public void Fit_DefaultIsFitToPage()
    {
        var vm = NewVm();
        Assert.True(vm.IsFitToPage);
    }

    [Fact]
    public void Fit_SelectingActualSize_ClearsOthers()
    {
        var vm = NewVm();
        vm.IsActualSize = true;
        Assert.False(vm.IsFitToPage);
        Assert.False(vm.IsShrinkToFit);
        Assert.False(vm.IsCustomScale);
    }

    [Fact]
    public void Fit_SelectingCustomScale_ClearsOthers()
    {
        var vm = NewVm();
        vm.IsCustomScale = true;
        Assert.True(vm.IsCustomScale);
        Assert.False(vm.IsFitToPage);
    }

    [Fact]
    public void CustomScalePercent_DefaultIs100()
    {
        var vm = NewVm();
        Assert.Equal(100, vm.CustomScalePercent);
    }
}
```

- [ ] **Step 2: Run, expect failure**

Run: `dotnet test Caly.Tests --filter "FullyQualifiedName~PrintDialogViewModelFitTests"`
Expected: missing properties.

- [ ] **Step 3: Add fit-mode properties**

Append to `PrintDialogViewModel`:

```csharp
// --- Fit (mutually exclusive radios) ---

[ObservableProperty] private bool _isFitToPage = true;
[ObservableProperty] private bool _isActualSize;
[ObservableProperty] private bool _isShrinkToFit;
[ObservableProperty] private bool _isCustomScale;
[ObservableProperty] private int _customScalePercent = 100;

partial void OnIsFitToPageChanged(bool value)
{
    if (!value) return;
    IsActualSize = false;
    IsShrinkToFit = false;
    IsCustomScale = false;
}

partial void OnIsActualSizeChanged(bool value)
{
    if (!value) return;
    IsFitToPage = false;
    IsShrinkToFit = false;
    IsCustomScale = false;
}

partial void OnIsShrinkToFitChanged(bool value)
{
    if (!value) return;
    IsFitToPage = false;
    IsActualSize = false;
    IsCustomScale = false;
}

partial void OnIsCustomScaleChanged(bool value)
{
    if (!value) return;
    IsFitToPage = false;
    IsActualSize = false;
    IsShrinkToFit = false;
}
```

- [ ] **Step 4: Run, expect PASS**

Run: `dotnet test Caly.Tests --filter "FullyQualifiedName~PrintDialogViewModelFitTests"`
Expected: 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add Caly.Core/ViewModels/PrintDialogViewModel.cs Caly.Tests/PrintDialogViewModelTests.cs
git commit -m "Add fit-mode properties to PrintDialogViewModel"
```

---

## Task 19: TDD pages-per-sheet and B&W properties

**Files:**
- Modify: `Caly.Core/ViewModels/PrintDialogViewModel.cs`
- Modify: `Caly.Tests/PrintDialogViewModelTests.cs`

- [ ] **Step 1: Write failing tests**

Append to `Caly.Tests/PrintDialogViewModelTests.cs`:

```csharp
public class PrintDialogViewModelOtherPropsTests
{
    private static PrintDialogViewModel NewVm()
        => new(new FakePrintService(), new FakePdfDocumentService(), currentPageNumber: 1);

    [Fact]
    public void PagesPerSheet_DefaultIs1()
    {
        Assert.Equal(1, NewVm().PagesPerSheet);
    }

    [Fact]
    public void IsBlackAndWhite_DefaultIsFalse()
    {
        Assert.False(NewVm().IsBlackAndWhite);
    }
}
```

- [ ] **Step 2: Run, expect failure**

Run: `dotnet test Caly.Tests --filter "FullyQualifiedName~PrintDialogViewModelOtherPropsTests"`
Expected: missing properties.

- [ ] **Step 3: Add the properties**

Append to `PrintDialogViewModel`:

```csharp
[ObservableProperty] private int _pagesPerSheet = 1;
[ObservableProperty] private bool _isBlackAndWhite;
```

- [ ] **Step 4: Run, expect PASS**

Run: `dotnet test Caly.Tests --filter "FullyQualifiedName~PrintDialogViewModelOtherPropsTests"`
Expected: 2 tests pass.

- [ ] **Step 5: Commit**

```bash
git add Caly.Core/ViewModels/PrintDialogViewModel.cs Caly.Tests/PrintDialogViewModelTests.cs
git commit -m "Add pages-per-sheet and B&W properties to PrintDialogViewModel"
```

---

## Task 20: TDD capability load and incompatibility reset

**Files:**
- Modify: `Caly.Core/ViewModels/PrintDialogViewModel.cs`
- Modify: `Caly.Tests/PrintDialogViewModelTests.cs`

- [ ] **Step 1: Write failing tests**

Append to `Caly.Tests/PrintDialogViewModelTests.cs`:

```csharp
public class PrintDialogViewModelCapabilitiesTests
{
    [Fact]
    public async Task SelectingPrinter_LoadsCapabilities()
    {
        var svc = new FakePrintService();
        var caps = new PrinterCapabilities(true, true, true, new[] { 1, 2, 4 });
        svc.Capabilities = caps;
        var vm = new PrintDialogViewModel(svc, new FakePdfDocumentService(), 1);

        vm.SelectedPrinter = new PrinterInfo("p1", null);
        await Task.Delay(50);  // capability load is fire-and-forget

        Assert.Equal(caps, vm.SelectedPrinterCapabilities);
    }

    [Fact]
    public async Task SwitchingToPortraitOnlyPrinter_ResetsLandscape()
    {
        var svc = new FakePrintService();
        var vm = new PrintDialogViewModel(svc, new FakePdfDocumentService(), 1);

        // Start with all-supported.
        svc.Capabilities = new PrinterCapabilities(true, true, true, new[] { 1, 2, 4 });
        vm.SelectedPrinter = new PrinterInfo("color", null);
        await Task.Delay(50);
        vm.IsOrientationLandscape = true;

        // Switch to a printer that does not support landscape.
        svc.Capabilities = new PrinterCapabilities(false, true, true, new[] { 1, 2, 4 });
        vm.SelectedPrinter = new PrinterInfo("portrait-only", null);
        await Task.Delay(50);

        Assert.True(vm.IsOrientationAuto);
        Assert.False(vm.IsOrientationLandscape);
    }
}
```

- [ ] **Step 2: Run, expect failure**

Expected: `SelectedPrinterCapabilities` not present.

- [ ] **Step 3: Add capability tracking**

Append to `PrintDialogViewModel`:

```csharp
[ObservableProperty] private PrinterCapabilities? _selectedPrinterCapabilities;

private CancellationTokenSource? _capabilitiesCts;

partial void OnSelectedPrinterChanged(PrinterInfo? value)
{
    _capabilitiesCts?.Cancel();
    _capabilitiesCts = new CancellationTokenSource();
    if (value is null)
    {
        SelectedPrinterCapabilities = null;
        return;
    }
    _ = LoadCapabilitiesAsync(value, _capabilitiesCts.Token);
}

private async Task LoadCapabilitiesAsync(PrinterInfo printer, CancellationToken token)
{
    try
    {
        var caps = await _printService.GetPrinterCapabilitiesAsync(printer, token).ConfigureAwait(true);
        if (token.IsCancellationRequested) return;
        SelectedPrinterCapabilities = caps;
        ResetIncompatibleSelections(caps);
    }
    catch (OperationCanceledException) { }
    catch (Exception ex)
    {
        StatusMessage = $"Could not load printer capabilities: {ex.Message}";
    }
}

private void ResetIncompatibleSelections(PrinterCapabilities caps)
{
    if (IsOrientationLandscape && !caps.SupportsLandscape)
    {
        IsOrientationAuto = true;
        StatusMessage = "Landscape not supported by this printer; using Auto.";
    }
    if (!caps.SupportedNumberUp.Contains(PagesPerSheet))
    {
        PagesPerSheet = 1;
        StatusMessage = "Selected pages-per-sheet not supported by this printer; using 1.";
    }
}
```

- [ ] **Step 4: Run, expect PASS**

Run: `dotnet test Caly.Tests --filter "FullyQualifiedName~PrintDialogViewModelCapabilitiesTests"`
Expected: 2 tests pass.

- [ ] **Step 5: Commit**

```bash
git add Caly.Core/ViewModels/PrintDialogViewModel.cs Caly.Tests/PrintDialogViewModelTests.cs
git commit -m "Load printer capabilities and reset incompatible selections"
```

---

## Task 21: TDD `Print` command builds `PrintSettings`

**Files:**
- Modify: `Caly.Core/ViewModels/PrintDialogViewModel.cs`
- Modify: `Caly.Tests/PrintDialogViewModelTests.cs`

- [ ] **Step 1: Add a recording fake to capture the settings argument**

Update `FakePrintService` to capture the last `PrintSettings` passed:

```csharp
public PrintSettings? LastSettings { get; private set; }

public Task PrintDocumentAsync(
    PrinterInfo printer,
    IPdfDocumentService documentService,
    IReadOnlyList<PrintPageInfo> pages,
    PrintSettings settings,
    IProgress<int>? progress,
    CancellationToken token)
{
    LastSettings = settings;
    return Task.CompletedTask;
}
```

- [ ] **Step 2: Write failing test**

Append to `Caly.Tests/PrintDialogViewModelTests.cs`:

```csharp
public class PrintDialogViewModelPrintCommandTests
{
    [Fact]
    public async Task PrintCommand_BuildsSettingsFromVmState()
    {
        var svc = new FakePrintService();
        var vm = new PrintDialogViewModel(svc, new FakePdfDocumentService(), 1);
        vm.SelectedPrinter = new PrinterInfo("p1", null);
        await Task.Delay(50);

        vm.IsOrientationLandscape = true;
        vm.IsCustomScale = true;
        vm.CustomScalePercent = 75;
        vm.PagesPerSheet = 2;
        vm.IsBlackAndWhite = true;

        await vm.PrintCommand.ExecuteAsync(null);

        Assert.NotNull(svc.LastSettings);
        Assert.Equal(PrintOrientation.Landscape, svc.LastSettings!.Orientation);
        Assert.Equal(PrintFitMode.CustomScale, svc.LastSettings.FitMode);
        Assert.Equal(75, svc.LastSettings.CustomScalePercent);
        Assert.Equal(2, svc.LastSettings.PagesPerSheet);
        Assert.Equal(PrintColorMode.Monochrome, svc.LastSettings.ColorMode);
    }

    [Fact]
    public async Task PrintCommand_CustomScaleClampedTo10To400()
    {
        var svc = new FakePrintService();
        var vm = new PrintDialogViewModel(svc, new FakePdfDocumentService(), 1);
        vm.SelectedPrinter = new PrinterInfo("p1", null);
        await Task.Delay(50);

        vm.IsCustomScale = true;
        vm.CustomScalePercent = 500;  // out of range

        await vm.PrintCommand.ExecuteAsync(null);

        Assert.Equal(400, svc.LastSettings!.CustomScalePercent);
    }
}
```

- [ ] **Step 3: Run, expect failure**

Expected: settings field on the command does not exist (we're still passing `new PrintSettings()`).

- [ ] **Step 4: Build settings inside `Print`**

In `PrintDialogViewModel.cs`, replace the `new PrintSettings()` line in the `Print` command body with:

```csharp
var settings = BuildPrintSettings();
await _printService.PrintDocumentAsync(printer, _documentService, pages, settings, progress, token);
```

Add the helper:

```csharp
private PrintSettings BuildPrintSettings()
{
    var orientation = IsOrientationPortrait
        ? PrintOrientation.Portrait
        : IsOrientationLandscape ? PrintOrientation.Landscape
        : PrintOrientation.Auto;

    var fit = IsActualSize ? PrintFitMode.ActualSize
        : IsShrinkToFit ? PrintFitMode.ShrinkToFit
        : IsCustomScale ? PrintFitMode.CustomScale
        : PrintFitMode.FitToPage;

    return new PrintSettings(
        Orientation: orientation,
        FitMode: fit,
        CustomScalePercent: Math.Clamp(CustomScalePercent, 10, 400),
        PagesPerSheet: PagesPerSheet,
        ColorMode: IsBlackAndWhite ? PrintColorMode.Monochrome : PrintColorMode.Color);
}
```

- [ ] **Step 5: Run, expect PASS**

Run: `dotnet test Caly.Tests --filter "FullyQualifiedName~PrintDialogViewModelPrintCommandTests"`
Expected: 2 tests pass.

- [ ] **Step 6: Commit**

```bash
git add Caly.Core/ViewModels/PrintDialogViewModel.cs Caly.Tests/PrintDialogViewModelTests.cs
git commit -m "Build PrintSettings from PrintDialogViewModel state"
```

---

## Task 22: Update `PrintDialogWindow.axaml` with new controls and bindings

**Files:**
- Modify: `Caly.Core/Views/PrintDialogWindow.axaml`

This task is manual-tested.

- [ ] **Step 1: Resize the window and add the four new groups**

Edit `Caly.Core/Views/PrintDialogWindow.axaml`. Change the `Window` opening tag's height bounds:

```xml
Width="380" MinHeight="540" MaxHeight="640"
```

Insert the new groups inside the `DockPanel`, after the existing `<!-- Page range -->` `StackPanel` and before the `<!-- Status / progress while printing -->` `ProgressBar`:

```xml
<!-- Orientation -->
<TextBlock DockPanel.Dock="Top" Text="Orientation" FontWeight="SemiBold" Margin="0,8,0,4"/>
<StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="0,0,0,10">
    <RadioButton GroupName="Orientation"
                 Content="Auto"
                 IsChecked="{Binding IsOrientationAuto}"
                 IsEnabled="{Binding !IsPrinting}"
                 Margin="0,0,12,0"/>
    <RadioButton GroupName="Orientation"
                 Content="Portrait"
                 IsChecked="{Binding IsOrientationPortrait}"
                 IsEnabled="{Binding !IsPrinting}"
                 Margin="0,0,12,0"/>
    <RadioButton GroupName="Orientation"
                 Content="Landscape"
                 IsChecked="{Binding IsOrientationLandscape}"
                 IsEnabled="{Binding CanSelectLandscape}"/>
</StackPanel>

<!-- Fit -->
<TextBlock DockPanel.Dock="Top" Text="Fit" FontWeight="SemiBold" Margin="0,8,0,4"/>
<StackPanel DockPanel.Dock="Top" Margin="0,0,0,10">
    <RadioButton GroupName="Fit" Content="Fit to page"
                 IsChecked="{Binding IsFitToPage}" IsEnabled="{Binding !IsPrinting}"/>
    <RadioButton GroupName="Fit" Content="Actual size"
                 IsChecked="{Binding IsActualSize}" IsEnabled="{Binding !IsPrinting}"/>
    <RadioButton GroupName="Fit" Content="Shrink to fit"
                 IsChecked="{Binding IsShrinkToFit}" IsEnabled="{Binding !IsPrinting}"/>
    <StackPanel Orientation="Horizontal">
        <RadioButton GroupName="Fit" Content="Custom scale:"
                     IsChecked="{Binding IsCustomScale}" IsEnabled="{Binding !IsPrinting}"/>
        <NumericUpDown Width="80" Margin="6,0,0,0"
                       Minimum="10" Maximum="400" Increment="5"
                       Value="{Binding CustomScalePercent}"
                       IsEnabled="{Binding IsCustomScale}"/>
        <TextBlock Text="%" VerticalAlignment="Center" Margin="2,0,0,0"/>
    </StackPanel>
</StackPanel>

<!-- Pages per sheet -->
<TextBlock DockPanel.Dock="Top" Text="Pages per sheet" FontWeight="SemiBold" Margin="0,8,0,4"/>
<ComboBox DockPanel.Dock="Top" Width="80" HorizontalAlignment="Left"
          Margin="0,0,0,10"
          SelectedValue="{Binding PagesPerSheet}"
          SelectedValueBinding="{Binding}"
          IsEnabled="{Binding !IsPrinting}">
    <x:Int32>1</x:Int32>
    <x:Int32>2</x:Int32>
    <x:Int32>4</x:Int32>
</ComboBox>

<!-- Black and white -->
<CheckBox DockPanel.Dock="Top" Content="Print in black and white"
          IsChecked="{Binding IsBlackAndWhite}"
          IsEnabled="{Binding !IsPrinting}"
          Margin="0,0,0,10"/>
```

- [ ] **Step 2: Add `CanSelectLandscape` computed property to the VM**

In `PrintDialogViewModel.cs`, change the existing `_selectedPrinterCapabilities` declaration (added in Task 20) to notify dependents, and add the computed property:

```csharp
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(CanSelectLandscape))]
private PrinterCapabilities? _selectedPrinterCapabilities;

public bool CanSelectLandscape =>
    SelectedPrinterCapabilities is null || SelectedPrinterCapabilities.SupportsLandscape;
```

This matches the codebase's existing pattern (e.g. `_currentPageNumber` already uses `[NotifyPropertyChangedFor(nameof(CurrentPageLabel))]`).

- [ ] **Step 3: Build and run the app**

Run: `dotnet build -c Debug` then `dotnet run --project Caly.Desktop`
Open a PDF, hit Print. Verify the dialog now shows all four new groups, sized correctly. Custom-scale `NumericUpDown` enables only when its radio is selected.

- [ ] **Step 4: Manual smoke — full happy path**

Print a multi-page PDF with each new option:

| Combo | Expected |
|---|---|
| Auto / Fit to page / 1-up / Color | works as before |
| Landscape / Actual size / 1-up / Color | each page printed at native size, landscape paper |
| Auto / Custom 50% / 2-up / Color | half-size pages, two per sheet |
| Portrait / Fit / 4-up / B&W | 4 pages per sheet, grayscale output |

- [ ] **Step 5: Commit**

```bash
git add Caly.Core/Views/PrintDialogWindow.axaml Caly.Core/ViewModels/PrintDialogViewModel.cs
git commit -m "Add orientation/fit/N-up/B&W controls to print dialog"
```

---

## Task 23: Final integration and AOT verification

**Files:**
- (no edits — verification only)

- [ ] **Step 1: Full test suite**

Run: `dotnet test Caly.Tests`
Expected: all tests pass.

- [ ] **Step 2: Full Debug build**

Run: `dotnet build -c Debug`
Expected: success on all TFMs (`net9.0` and `net10.0`).

- [ ] **Step 3: Full Release build**

Run: `dotnet build -c Release`
Expected: success.

- [ ] **Step 4: AOT publish (Windows)**

Run: `dotnet publish Caly.Desktop -r win-x64 -c AOT -f net10.0`
Expected: succeeds with no AOT trim/IL warnings related to print code (CommunityToolkit source-generated properties + simple records are AOT-clean).

- [ ] **Step 5: Manual test matrix from spec**

Walk through the manual-test table in the spec on Windows + at least one Unix host. Mark the table in the spec or the PR description with which scenarios were verified.

- [ ] **Step 6: Final commit (only if any docs were updated)**

If you marked verification status in the spec, commit it:

```bash
git add docs/superpowers/specs/2026-05-10-printing-options-design.md
git commit -m "Record manual test verification results"
```

If nothing changed, no commit is needed.

---

## Risks / call-outs for the implementer

- **CsWin32 generated names** — the exact `DEVMODEW` field path (e.g. `Anonymous1.Anonymous1.dmOrientation`) is version-dependent. If the build error is "no member dmOrientation", inspect the generated source under `obj/Debug/net10.0/generated/Microsoft.Windows.CsWin32/` and adjust.
- **SharpIppNext property names** — `OrientationRequestedSupported` etc. on `response.PrinterAttributes` may be named slightly differently. Adjust to whatever IntelliSense surfaces; the semantic check (value 4 → landscape, "monochrome" string → mono) is unchanged.
- **2-up rotation** — Task 13 ships a deliberate v1 simplification (cells split along longer paper edge, no per-bitmap rotation). On portrait paper, 2-up will show pages stacked top/bottom rather than rotated side-by-side. This matches the spec's documented "open risk" and can be improved in a follow-up by pre-rotating each cell's source bitmap 90° when paper-cell-aspect mismatches the page aspect.
- **`FakePdfDocumentService`** — when stubbing it in Task 17, prefer throwing `NotImplementedException` for any methods the print VM does not call, rather than returning silent defaults; that surfaces accidental dependencies during refactors.
- **Test isolation** — Tests using `await Task.Delay(50)` for the fire-and-forget capability load are timing-dependent. If they prove flaky, refactor `LoadCapabilitiesAsync` to expose a `TaskCompletionSource` for tests to await directly.
