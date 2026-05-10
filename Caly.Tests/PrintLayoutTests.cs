using Avalonia;
using Caly.Core.Services.Interfaces;
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

public class PrintLayoutCellsTests
{
    [Fact]
    public void ComputeCells_OneUp_ReturnsFullArea()
    {
        var cells = PrintLayout.ComputeCells(printerW: 1000, printerH: 800, pagesPerSheet: 1);
        Assert.Single(cells);
        Assert.Equal(new PixelRect(0, 0, 1000, 800), cells[0]);
    }

    [Fact]
    public void ComputeCells_TwoUp_PortraitPaper_StacksTopBottom()
    {
        // printerW=600, printerH=800 → portrait paper. Longer edge = vertical.
        // 2-up splits along the longer edge → top half + bottom half.
        var cells = PrintLayout.ComputeCells(printerW: 600, printerH: 800, pagesPerSheet: 2);
        Assert.Equal(2, cells.Count);
        Assert.Equal(new PixelRect(0, 0, 600, 400), cells[0]);
        Assert.Equal(new PixelRect(0, 400, 600, 400), cells[1]);
    }

    [Fact]
    public void ComputeCells_TwoUp_LandscapePaper_SplitsLeftRight()
    {
        // printerW=1000, printerH=600 → landscape paper. Longer edge = horizontal.
        var cells = PrintLayout.ComputeCells(printerW: 1000, printerH: 600, pagesPerSheet: 2);
        Assert.Equal(2, cells.Count);
        Assert.Equal(new PixelRect(0, 0, 500, 600), cells[0]);
        Assert.Equal(new PixelRect(500, 0, 500, 600), cells[1]);
    }

    [Fact]
    public void ComputeCells_FourUp_TwoByTwoGrid()
    {
        var cells = PrintLayout.ComputeCells(printerW: 1000, printerH: 800, pagesPerSheet: 4);
        Assert.Equal(4, cells.Count);
        // Reading order: top-left, top-right, bottom-left, bottom-right
        Assert.Equal(new PixelRect(0, 0, 500, 400), cells[0]);
        Assert.Equal(new PixelRect(500, 0, 500, 400), cells[1]);
        Assert.Equal(new PixelRect(0, 400, 500, 400), cells[2]);
        Assert.Equal(new PixelRect(500, 400, 500, 400), cells[3]);
    }

    [Fact]
    public void ComputeCells_UnsupportedNumberUp_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PrintLayout.ComputeCells(printerW: 600, printerH: 800, pagesPerSheet: 3));
    }
}

public class PrintLayoutFitTests
{
    // 100×200 cell, 50×50 source bitmap (assumed already at intended physical size).
    // dpiX = dpiY = 100. 1 PDF point = 1/72 inch → 100/72 printer pixels per point.
    // We only test the rect-shape — actual scaling happens via StretchDIBits.

    [Fact]
    public void ComputeDestRect_FitToPage_PreservesAspect_FillsLongest()
    {
        // Bitmap is 50×50 (square aspect). Cell is 100×200 (tall). Fitting → 100×100 centered vertically.
        var cell = new PixelRect(10, 20, 100, 200);
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
        var cell = new PixelRect(0, 0, 1000, 1000);
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
        var cell = new PixelRect(0, 0, 500, 500);
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
        var cell = new PixelRect(0, 0, 500, 500);
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
        var cell = new PixelRect(0, 0, 1000, 1000);
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
