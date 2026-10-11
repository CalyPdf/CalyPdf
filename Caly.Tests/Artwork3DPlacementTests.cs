using Avalonia;
using Caly.Core.Controls;

namespace Caly.Tests;

/// <summary>
/// Where the 3D control goes over its artwork on a zoomed page, and which part of the view it shows.
/// </summary>
public class Artwork3DPlacementTests
{
    private static readonly Rect Artwork = new(40, 30, 320, 240);
    private static readonly Rect WholePage = new(0, 0, 400, 300);

    private static void AssertClose(Rect expected, Rect actual, double tolerance = 1e-9)
    {
        Assert.Equal(expected.X, actual.X, tolerance);
        Assert.Equal(expected.Y, actual.Y, tolerance);
        Assert.Equal(expected.Width, actual.Width, tolerance);
        Assert.Equal(expected.Height, actual.Height, tolerance);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 1.5)]
    [InlineData(3, 1.5)]
    [InlineData(0.5, 2)]
    public void AViewThatFitsTheBudgetIsDrawnWhole(double zoom, double scaling)
    {
        Artwork3DLayout layout = Artwork3DPlacement.Compute(Artwork, WholePage, zoom, scaling);

        Assert.False(layout.IsEmpty);
        Assert.Equal(Artwork, layout.Part);
        Assert.Equal(new Size(320 * zoom, 240 * zoom), layout.ControlSize);
        Assert.Equal(new Rect(0, 0, 1, 1), layout.VisibleRegion);
    }

    [Fact]
    public void AViewThatFitsTheBudgetKeepsItsSizeWhenOnlyACornerIsVisible()
    {
        // The control must not change size while its page scrolls: every change is a new framebuffer.
        var corner = new Rect(0, 0, 60, 45);

        Artwork3DLayout layout = Artwork3DPlacement.Compute(Artwork, corner, 2, 1.5);

        Assert.Equal(Artwork, layout.Part);
        Assert.Equal(new Size(640, 480), layout.ControlSize);
        Assert.Equal(new Rect(0, 0, 1, 1), layout.VisibleRegion);
    }

    [Fact]
    public void NothingVisibleIsEmpty()
    {
        Assert.True(Artwork3DPlacement.Compute(Artwork, null, 1, 1).IsEmpty);
        Assert.True(Artwork3DPlacement.Compute(Artwork, new Rect(0, 0, 30, 20), 1, 1).IsEmpty);
        Assert.True(Artwork3DPlacement.Compute(Artwork, new Rect(0, 0, 40, 300), 1, 1).IsEmpty);
        Assert.True(Artwork3DPlacement.Compute(new Rect(40, 30, 0, 240), WholePage, 1, 1).IsEmpty);
        Assert.True(Artwork3DPlacement.Compute(new Rect(40, 30, 320, double.NaN), WholePage, 1, 1).IsEmpty);
        Assert.Equal(default, Artwork3DPlacement.Compute(Artwork, null, 1, 1).ControlSize);
    }

    [Fact]
    public void AViewLargerThanTheBudgetIsCroppedToTheVisiblePartPlusAMargin()
    {
        // Zoom 8 at 150 %: 3840 x 2880 pixels, far more than a window. The page shows (100, 80, 100, 75).
        var visible = new Rect(100, 80, 100, 75);
        const double zoom = 8, scaling = 1.5;

        Artwork3DLayout layout = Artwork3DPlacement.Compute(Artwork, visible, zoom, scaling);

        // 64 screen units of margin are 8 page units at this zoom.
        AssertClose(new Rect(92, 72, 116, 91), layout.Part);
        Assert.Equal(116 * 8, layout.ControlSize.Width, 1e-9);
        Assert.Equal(91 * 8, layout.ControlSize.Height, 1e-9);
        AssertClose(new Rect((92 - 40) / 320.0, (72 - 30) / 240.0, 116 / 320.0, 91 / 240.0), layout.VisibleRegion);
    }

    [Fact]
    public void TheMarginStopsAtTheEdgesOfTheArtwork()
    {
        // The top-left corner of the artwork is on screen: no margin beyond it.
        var visible = new Rect(0, 0, 140, 105);

        Artwork3DLayout layout = Artwork3DPlacement.Compute(Artwork, visible, 8, 1.5);

        AssertClose(new Rect(40, 30, 108, 83), layout.Part);
        Assert.Equal(0, layout.VisibleRegion.X, 1e-12);
        Assert.Equal(0, layout.VisibleRegion.Y, 1e-12);

        // And the bottom-right corner.
        layout = Artwork3DPlacement.Compute(Artwork, new Rect(300, 220, 100, 80), 8, 1.5);
        Assert.Equal(360, layout.Part.Right, 1e-9);
        Assert.Equal(270, layout.Part.Bottom, 1e-9);
        Assert.Equal(1, layout.VisibleRegion.Right, 1e-12);
        Assert.Equal(1, layout.VisibleRegion.Bottom, 1e-12);
    }

    [Fact]
    public void CroppedEdgesLieOnDevicePixelsOfTheView()
    {
        // Awkward numbers on purpose.
        var visible = new Rect(101.37, 77.913, 93.21, 61.77);
        const double zoom = 7.3, scaling = 1.25;

        Artwork3DLayout layout = Artwork3DPlacement.Compute(Artwork, visible, zoom, scaling);

        double pixels = zoom * scaling;
        foreach (double offset in new[] { layout.Part.X - Artwork.X, layout.Part.Y - Artwork.Y, layout.Part.Width, layout.Part.Height })
        {
            double inPixels = offset * pixels;
            Assert.Equal(Math.Round(inPixels), inPixels, 1e-6);
        }

        // Snapping only ever adds: the visible part with its margin is inside.
        Rect wanted = visible.Inflate(Artwork3DPlacement.CropMargin / zoom).Intersect(Artwork);
        Assert.True(layout.Part.X <= wanted.X + 1e-9 && layout.Part.Y <= wanted.Y + 1e-9);
        Assert.True(layout.Part.Right >= wanted.Right - 1e-9 && layout.Part.Bottom >= wanted.Bottom - 1e-9);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ABadZoomOrScalingCountsAsOne(double bad)
    {
        Artwork3DLayout expected = Artwork3DPlacement.Compute(Artwork, WholePage, 1, 1);

        Assert.Equal(expected, Artwork3DPlacement.Compute(Artwork, WholePage, bad, 1));
        Assert.Equal(expected, Artwork3DPlacement.Compute(Artwork, WholePage, 1, bad));
    }

    [Fact]
    public void TheControlNeverNeedsMorePixelsThanTheBudgetOrTheVisiblePart()
    {
        double[] zooms = [0.1, 0.25, 0.5, 1, 1.5, 2, 3, 4.7, 6, 10, 32];
        double[] scalings = [1, 1.25, 1.5, 2, 3];
        Rect[] visibles = [WholePage, new Rect(0, 0, 400, 20), new Rect(150, 100, 120, 90), new Rect(350, 260, 50, 40), new Rect(39, 29, 2, 2)];

        foreach (double zoom in zooms)
        {
            foreach (double scaling in scalings)
            {
                foreach (Rect visible in visibles)
                {
                    Artwork3DLayout layout = Artwork3DPlacement.Compute(Artwork, visible, zoom, scaling);
                    if (layout.IsEmpty)
                    {
                        Assert.True(visible.Intersect(Artwork).Width <= 0 || visible.Intersect(Artwork).Height <= 0);
                        continue;
                    }

                    // Inside the artwork, and the region says exactly which part.
                    Assert.True(layout.Part.X >= Artwork.X - 1e-9 && layout.Part.Y >= Artwork.Y - 1e-9);
                    Assert.True(layout.Part.Right <= Artwork.Right + 1e-9 && layout.Part.Bottom <= Artwork.Bottom + 1e-9);
                    Assert.InRange(layout.VisibleRegion.X, 0, 1);
                    Assert.InRange(layout.VisibleRegion.Y, 0, 1);
                    Assert.InRange(layout.VisibleRegion.Right, 0, 1 + 1e-12);
                    Assert.InRange(layout.VisibleRegion.Bottom, 0, 1 + 1e-12);
                    Assert.Equal(layout.Part.Width * zoom, layout.ControlSize.Width, 1e-9);
                    Assert.Equal(layout.Part.Height * zoom, layout.ControlSize.Height, 1e-9);

                    // Never more pixels than the budget, unless the screen itself shows more.
                    double pixels = layout.ControlSize.Width * scaling * layout.ControlSize.Height * scaling;
                    double margin = 2 * Artwork3DPlacement.CropMargin + 2;
                    double onScreen = (visible.Width * zoom + margin) * scaling * ((visible.Height * zoom + margin) * scaling);
                    Assert.True(pixels <= Math.Max(Artwork3DPlacement.MaxUncroppedPixels, onScreen) + 1,
                        $"zoom {zoom}, scaling {scaling}, visible {visible}: {pixels} pixels");
                }
            }
        }
    }
}
