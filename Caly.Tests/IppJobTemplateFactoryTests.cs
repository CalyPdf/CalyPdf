using Caly.Core.Services.Interfaces;
using Caly.Printing.Unix;
using SharpIpp.Protocol.Models;
using CalyPrintColorMode = Caly.Core.Services.Interfaces.PrintColorMode;
using IppPrintColorMode = SharpIpp.Protocol.Models.PrintColorMode;

namespace Caly.Tests;

/// <summary>
/// Covers the SharpIppNext-typed conversions in <see cref="IppJobTemplateFactory"/>.
/// <para>
/// SharpIppNext 4.2.4 made the implicit <c>string</c> operator on every smart enum throw
/// <see cref="ArgumentNullException"/> for a null value. Because the
/// <c>string</c> -&gt; <c>PrintColorMode?</c> conversion is not lifted (the source is a
/// reference type), a null color-mode keyword reaching the cast would throw at Create-Job
/// time. These tests pin the guard that prevents it.
/// </para>
/// </summary>
public class IppJobTemplateFactoryTests
{
    private static PrinterCapabilities AllSupported() => new(
        SupportsLandscape: true,
        IsColorDevice: true,
        SupportsMonochromeDirective: true,
        SupportedNumberUp: [1, 2, 4]);

    private static PrinterCapabilities NoMonochromeDirective() => new(
        SupportsLandscape: true,
        IsColorDevice: true,
        SupportsMonochromeDirective: false,
        SupportedNumberUp: [1, 2, 4]);

    [Fact]
    public void Build_ColorRequested_LeavesPrintColorModeUnset()
    {
        // MapColorMode returns null for Color: the printer default is inherited. The null must
        // not be handed to the implicit string -> PrintColorMode operator.
        var settings = new PrintSettings(ColorMode: CalyPrintColorMode.Color);

        var template = IppJobTemplateFactory.Build(settings, AllSupported());

        Assert.Null(template.PrintColorMode);
    }

    [Fact]
    public void Build_MonochromeOnPrinterWithoutDirective_LeavesPrintColorModeUnset()
    {
        // The other null-producing path: the app greyscales the bitmap itself instead.
        var settings = new PrintSettings(ColorMode: CalyPrintColorMode.Monochrome);

        var template = IppJobTemplateFactory.Build(settings, NoMonochromeDirective());

        Assert.Null(template.PrintColorMode);
    }

    [Fact]
    public void Build_MonochromeSupported_SetsMonochromeKeyword()
    {
        var settings = new PrintSettings(ColorMode: CalyPrintColorMode.Monochrome);

        var template = IppJobTemplateFactory.Build(settings, AllSupported());

        Assert.Equal(IppPrintColorMode.Monochrome, template.PrintColorMode);
        Assert.Equal("monochrome", ((IppPrintColorMode)template.PrintColorMode!).Value);
    }

    [Fact]
    public void Build_AutoOrientation_LeavesOrientationUnset()
    {
        var settings = new PrintSettings(Orientation: PrintOrientation.Auto);

        var template = IppJobTemplateFactory.Build(settings, AllSupported());

        Assert.Null(template.OrientationRequested);
    }

    [Theory]
    [InlineData(PrintOrientation.Portrait, Orientation.Portrait)]
    [InlineData(PrintOrientation.Landscape, Orientation.Landscape)]
    public void Build_ExplicitOrientation_MapsToIppOrientation(PrintOrientation requested, Orientation expected)
    {
        var settings = new PrintSettings(Orientation: requested);

        var template = IppJobTemplateFactory.Build(settings, AllSupported());

        Assert.Equal(expected, template.OrientationRequested);
    }

    [Fact]
    public void Build_LandscapeOnPortraitOnlyPrinter_LeavesOrientationUnset()
    {
        var caps = new PrinterCapabilities(
            SupportsLandscape: false,
            IsColorDevice: true,
            SupportsMonochromeDirective: true,
            SupportedNumberUp: [1, 2, 4]);
        var settings = new PrintSettings(Orientation: PrintOrientation.Landscape);

        var template = IppJobTemplateFactory.Build(settings, caps);

        Assert.Null(template.OrientationRequested);
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(4, 4)]
    [InlineData(6, 1)]
    public void Build_NumberUp_FallsBackToOneWhenUnsupported(int requested, int expected)
    {
        var settings = new PrintSettings(PagesPerSheet: requested);

        var template = IppJobTemplateFactory.Build(settings, AllSupported());

        Assert.Equal(expected, template.NumberUp);
    }

    [Theory]
    [InlineData(PrintFitMode.FitToPage, "fit")]
    [InlineData(PrintFitMode.ShrinkToFit, "auto-fit")]
    [InlineData(PrintFitMode.ActualSize, "none")]
    [InlineData(PrintFitMode.CustomScale, "none")]
    public void Build_FitMode_MapsToPrintScalingKeyword(PrintFitMode fit, string expected)
    {
        var settings = new PrintSettings(FitMode: fit);

        var template = IppJobTemplateFactory.Build(settings, AllSupported());

        Assert.Equal(expected, ((PrintScaling)template.PrintScaling!).Value);
    }

    [Theory]
    [InlineData(IppAttributeMapping.IppPrintScaling.Fit, "fit")]
    [InlineData(IppAttributeMapping.IppPrintScaling.None, "none")]
    [InlineData(IppAttributeMapping.IppPrintScaling.AutoFit, "auto-fit")]
    public void MapPrintScaling_ProducesRfc8011Keywords(IppAttributeMapping.IppPrintScaling scaling, string expected)
    {
        Assert.Equal(expected, IppJobTemplateFactory.MapPrintScaling(scaling).Value);
    }

    [Fact]
    public void MapPrintScaling_UnknownValue_FallsBackToFit()
    {
        Assert.Equal(PrintScaling.Fit, IppJobTemplateFactory.MapPrintScaling((IppAttributeMapping.IppPrintScaling)42));
    }
}
