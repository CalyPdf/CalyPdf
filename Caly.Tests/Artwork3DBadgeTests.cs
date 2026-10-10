using Avalonia;
using Caly.Core.Controls;
using UglyToad.PdfPig.Core;

namespace Caly.Tests;

public class Artwork3DBadgeTests
{
    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    [InlineData(0.5)]
    [InlineData(6.0)]
    public void BadgeHasConstantOnScreenSize(double zoom)
    {
        var box = new PdfRectangle(100, 200, 400, 500);

        Rect badge = PageInteractiveLayerControl.GetArtwork3DBadgeRect(box, zoom);

        Assert.Equal(PageInteractiveLayerControl.Artwork3DBadgeWidth, badge.Width * zoom, 6);
        Assert.Equal(PageInteractiveLayerControl.Artwork3DBadgeHeight, badge.Height * zoom, 6);

        // inset from the left and top edges of the artwork (y grows downwards in the layer)
        Assert.Equal(PageInteractiveLayerControl.Artwork3DBadgeMargin, (badge.X - 100) * zoom, 6);
        Assert.Equal(PageInteractiveLayerControl.Artwork3DBadgeMargin, (badge.Y - 200) * zoom, 6);
    }

    [Fact]
    public void BadgeSitsAtTheTopLeftWhateverTheCornerOrder()
    {
        // the same rectangle given with its corners the other way round
        var flipped = new PdfRectangle(new PdfPoint(400, 500), new PdfPoint(100, 200));

        Rect badge = PageInteractiveLayerControl.GetArtwork3DBadgeRect(flipped, 1);

        Assert.Equal(100 + PageInteractiveLayerControl.Artwork3DBadgeMargin, badge.X, 6);
        Assert.Equal(200 + PageInteractiveLayerControl.Artwork3DBadgeMargin, badge.Y, 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void BadgeTreatsAnUnusableZoomAsOne(double zoom)
    {
        Rect badge = PageInteractiveLayerControl.GetArtwork3DBadgeRect(new PdfRectangle(0, 0, 300, 300), zoom);

        Assert.Equal(PageInteractiveLayerControl.Artwork3DBadgeWidth, badge.Width, 6);
        Assert.Equal(PageInteractiveLayerControl.Artwork3DBadgeHeight, badge.Height, 6);
    }

    [Fact]
    public void DescriptionListsFormatAndViews()
    {
        string text = PageInteractiveLayerControl.GetArtwork3DDescription("PRC", ["Front", "Iso View"]);

        Assert.Equal(
            "3D model (PRC)" + Environment.NewLine +
            "Views: Front, Iso View" + Environment.NewLine +
            "Interactive 3D viewing is not available yet.",
            text);
    }

    [Fact]
    public void DescriptionWithoutViews()
    {
        string text = PageInteractiveLayerControl.GetArtwork3DDescription("U3D", []);

        Assert.Equal(
            "3D model (U3D)" + Environment.NewLine +
            "No authored views" + Environment.NewLine +
            "Interactive 3D viewing is not available yet.",
            text);
    }

    [Fact]
    public void DescriptionCapsAtTenViews()
    {
        string[] names = Enumerable.Range(1, 12).Select(i => $"V{i}").ToArray();

        string text = PageInteractiveLayerControl.GetArtwork3DDescription("PRC", names);

        Assert.Contains("Views: V1, V2, V3, V4, V5, V6, V7, V8, V9, V10 and 2 more", text);
        Assert.DoesNotContain("V11", text);
    }

    [Fact]
    public void DescriptionWithExactlyTenViewsHasNoRemainder()
    {
        string[] names = Enumerable.Range(1, 10).Select(i => $"V{i}").ToArray();

        string text = PageInteractiveLayerControl.GetArtwork3DDescription("PRC", names);

        Assert.Contains("V10" + Environment.NewLine, text);
        Assert.DoesNotContain("more", text);
    }
}
