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
}
