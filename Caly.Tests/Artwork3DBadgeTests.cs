using UglyToad.PdfPig.Artwork;
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
        string text = PageInteractiveLayerControl.GetArtwork3DDescription("PRC", ["Front", "Iso View"], Artwork3DHint.FormatNotSupported);

        Assert.Equal(
            "3D model (PRC)" + Environment.NewLine +
            "Views: Front, Iso View" + Environment.NewLine +
            "PRC models are not supported yet.",
            text);
    }

    [Fact]
    public void DescriptionWithoutViews()
    {
        string text = PageInteractiveLayerControl.GetArtwork3DDescription("U3D", [], Artwork3DHint.ClickToView);

        Assert.Equal(
            "3D model (U3D)" + Environment.NewLine +
            "No authored views" + Environment.NewLine +
            "Click to view in 3D.",
            text);
    }

    [Fact]
    public void DescriptionSaysWhenTheGpuIsMissing()
    {
        string text = PageInteractiveLayerControl.GetArtwork3DDescription("U3D", ["Front"], Artwork3DHint.GpuUnavailable);

        Assert.EndsWith(Environment.NewLine + "3D needs the GPU renderer.", text);
    }

    [Fact]
    public void DescriptionOfAModelThatWasRefusedSaysWhy()
    {
        const string why = "This 3D model uses a compressed mesh format that is not supported yet.";
        string text = PageInteractiveLayerControl.GetArtwork3DDescription("PRC", ["Front"], Artwork3DHint.Refused, why);

        Assert.Equal(
            "3D model (PRC)" + Environment.NewLine +
            "Views: Front" + Environment.NewLine +
            why,
            text);
    }

    /// <summary>
    /// A page remembers which of its artworks could not be shown by their position in its text layer:
    /// the layer, and the artwork objects in it, are dropped and built again as the page comes and goes.
    /// </summary>
    [Fact]
    public void ARefusalIsFoundByTheArtworksPlaceOnThePage()
    {
        using Artwork3DTestDocument box = Artwork3DTestDocument.OpenBox();
        Caly.Pdf.Models.PdfArtwork3D artwork = box.Artworks[0];
        var refusals = new Dictionary<int, string> { [0] = "why" };

        Assert.Equal("why", PageInteractiveLayerControl.GetArtwork3DRefusal([artwork], artwork, refusals));
        Assert.Null(PageInteractiveLayerControl.GetArtwork3DRefusal([artwork], artwork, new Dictionary<int, string> { [1] = "another one's" }));
        Assert.Null(PageInteractiveLayerControl.GetArtwork3DRefusal([artwork], artwork, null));
        Assert.Null(PageInteractiveLayerControl.GetArtwork3DRefusal(null, artwork, refusals));
        Assert.Null(PageInteractiveLayerControl.GetArtwork3DRefusal([], artwork, refusals));
    }

    [Fact]
    public void TheHintFollowsTheFormatAndWhether3DWorksHere()
    {
        Caly.Core.Services.Artwork3DAvailability.MarkAvailable();
        try
        {
            Assert.Equal(Artwork3DHint.ClickToView, PageInteractiveLayerControl.GetArtwork3DHint(Artwork3DFormat.U3D));
            Assert.Equal(Artwork3DHint.ClickToView, PageInteractiveLayerControl.GetArtwork3DHint(Artwork3DFormat.Prc));
            Assert.Equal(Artwork3DHint.FormatNotSupported, PageInteractiveLayerControl.GetArtwork3DHint(Artwork3DFormat.Unknown));

            // A model that was read and refused says so, whatever else is true.
            Assert.Equal(Artwork3DHint.Refused, PageInteractiveLayerControl.GetArtwork3DHint(Artwork3DFormat.Prc, "why"));
            Assert.Equal(Artwork3DHint.FormatNotSupported, PageInteractiveLayerControl.GetArtwork3DHint(Artwork3DFormat.Unknown, "why"));

            Caly.Core.Services.Artwork3DAvailability.MarkUnavailable();
            Assert.Equal(Artwork3DHint.Refused, PageInteractiveLayerControl.GetArtwork3DHint(Artwork3DFormat.Prc, "why"));
            Assert.Equal(Artwork3DHint.GpuUnavailable, PageInteractiveLayerControl.GetArtwork3DHint(Artwork3DFormat.U3D));
            Assert.Equal(Artwork3DHint.GpuUnavailable, PageInteractiveLayerControl.GetArtwork3DHint(Artwork3DFormat.Prc));
            Assert.Equal(Artwork3DHint.FormatNotSupported, PageInteractiveLayerControl.GetArtwork3DHint(Artwork3DFormat.Unknown));
        }
        finally
        {
            Caly.Core.Services.Artwork3DAvailability.MarkAvailable();
        }
    }

    [Fact]
    public void DescriptionCapsAtTenViews()
    {
        string[] names = Enumerable.Range(1, 12).Select(i => $"V{i}").ToArray();

        string text = PageInteractiveLayerControl.GetArtwork3DDescription("PRC", names, Artwork3DHint.FormatNotSupported);

        Assert.Contains("Views: V1, V2, V3, V4, V5, V6, V7, V8, V9, V10 and 2 more", text);
        Assert.DoesNotContain("V11", text);
    }

    [Fact]
    public void DescriptionWithExactlyTenViewsHasNoRemainder()
    {
        string[] names = Enumerable.Range(1, 10).Select(i => $"V{i}").ToArray();

        string text = PageInteractiveLayerControl.GetArtwork3DDescription("PRC", names, Artwork3DHint.FormatNotSupported);

        Assert.Contains("V10" + Environment.NewLine, text);
        Assert.DoesNotContain("more", text);
    }
}
