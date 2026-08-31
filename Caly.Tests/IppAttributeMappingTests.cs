using Caly.Core.Services.Interfaces;
using Caly.Printing.Unix;

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

    // --- MapNumberUpSupported (number-up-supported, reported as Range[]) ---

    [Fact]
    public void MapNumberUpSupported_AttributeAbsent_KeepsConventionalSet()
    {
        // Null means the printer did not report number-up-supported: don't downgrade it to 1-up.
        Assert.Equal(new[] { 1, 2, 4 }, IppAttributeMapping.MapNumberUpSupported(null));
    }

    [Fact]
    public void MapNumberUpSupported_EmptySet_KeepsConventionalSet()
    {
        Assert.Equal(new[] { 1, 2, 4 }, IppAttributeMapping.MapNumberUpSupported([]));
    }

    [Fact]
    public void MapNumberUpSupported_SetOfIntegers_KeepsOnlyOfferedValues()
    {
        // A plain 1setOf integer arrives as one-value spans; 6/9/16 are not offered by the dialog.
        (int, int)[] reported = [(1, 1), (2, 2), (4, 4), (6, 6), (9, 9), (16, 16)];

        Assert.Equal(new[] { 1, 2, 4 }, IppAttributeMapping.MapNumberUpSupported(reported));
    }

    [Fact]
    public void MapNumberUpSupported_RangeOfInteger_ExpandsInclusively()
    {
        // rangeOfInteger 1..4 covers every value the dialog offers.
        Assert.Equal(new[] { 1, 2, 4 }, IppAttributeMapping.MapNumberUpSupported([(1, 4)]));
    }

    [Fact]
    public void MapNumberUpSupported_RangeStoppingBefore4_DropsFourUp()
    {
        Assert.Equal(new[] { 1, 2 }, IppAttributeMapping.MapNumberUpSupported([(1, 3)]));
    }

    [Fact]
    public void MapNumberUpSupported_OneUpOnlyPrinter_DropsTwoAndFourUp()
    {
        Assert.Equal(new[] { 1 }, IppAttributeMapping.MapNumberUpSupported([(1, 1)]));
    }

    [Fact]
    public void MapNumberUpSupported_SparseSet_SkipsGaps()
    {
        // Supports 1 and 4 but not 2.
        Assert.Equal(new[] { 1, 4 }, IppAttributeMapping.MapNumberUpSupported([(1, 1), (4, 4)]));
    }

    [Fact]
    public void MapNumberUpSupported_AlwaysIncludesOne()
    {
        // RFC 8011 requires 1 in number-up-supported; a printer omitting it must not leave the
        // dialog with no valid selection.
        Assert.Equal(new[] { 1, 2 }, IppAttributeMapping.MapNumberUpSupported([(2, 2)]));
    }

    [Fact]
    public void MapNumberUpSupported_FeedsMapNumberUp()
    {
        // End to end: a 1-up-only printer forces PagesPerSheet back to 1.
        var caps = AllSupported() with
        {
            SupportedNumberUp = IppAttributeMapping.MapNumberUpSupported([(1, 1)])
        };

        Assert.Equal(1, IppAttributeMapping.MapNumberUp(new PrintSettings(PagesPerSheet: 4), caps));
    }
}
