using Caly.Pdf;
using Caly.Pdf.Models;
using Caly.Pdf.PageFactories;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Artwork;
using UglyToad.PdfPig.Tokens;

namespace Caly.Tests;

public class Artwork3DTextLayerTests
{
    // box_base.pdf comes from the Universal 3D Sample Software (Samples/TestScenes), Apache License 2.0.
    // Its 3D annotation covers [0 0 300 300] and names one view, "DefaultView".
    private static PdfTextLayer Load(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Documents", name);
        using var document = PdfDocument.Open(path, new ParsingOptions { UseActualText = true });

        // TextLayerFactory reads the PPI scale back out of this fake indirect object, the way
        // PdfPigDocumentService puts it there.
        document.Advanced.ReplaceIndirectObject(CalyPdfHelper.FakePpiReference, new NumericToken(1));
        document.AddPageFactory<PageTextLayerContent, TextLayerFactory>();

        return PdfTextLayerHelper.GetTextLayer(
            document.GetPage<PageTextLayerContent>(1, CancellationToken.None), CancellationToken.None);
    }

    [Fact]
    public void BoxBase_HasOneArtwork()
    {
        var layer = Load("box_base.pdf");

        var artwork = Assert.Single(layer.Artworks3D!);
        Assert.Equal(Artwork3DFormat.U3D, artwork.Format);
        Assert.Equal("U3D", artwork.FormatDisplayName);
        Assert.Equal(["DefaultView"], artwork.ViewNames);
        Assert.Equal("DefaultView", artwork.DefaultViewName);
        Assert.False(artwork.HasNormalAppearance); // the file's AP has an empty normal appearance
        Assert.Equal(1, artwork.PpiScale);
        Assert.Equal(300, artwork.BoundingBox.Width, 3);
        Assert.Equal(300, artwork.BoundingBox.Height, 3);
    }

    [Fact]
    public void BoxBase_ArtworkIsFoundUnderThePointer()
    {
        var layer = Load("box_base.pdf");
        var artwork = layer.Artworks3D![0];
        var centre = artwork.BoundingBox.Centroid;

        Assert.Same(artwork, layer.FindArtwork3DOver(centre.X, centre.Y));
        Assert.Null(layer.FindArtwork3DOver(artwork.BoundingBox.Right + 50, centre.Y));

        // With its place among the page's artworks, which is what says which one is active.
        Assert.Same(artwork, layer.FindArtwork3DOver(centre.X, centre.Y, out int index));
        Assert.Equal(0, index);
        Assert.Null(layer.FindArtwork3DOver(artwork.BoundingBox.Right + 50, centre.Y, out index));
        Assert.Equal(-1, index);
    }

    /// <summary>
    /// View names are shown from the UI thread on hover. They must be plain values captured while the
    /// page was processed, not something read from the document (whose scanner is not thread-safe) later.
    /// </summary>
    [Fact]
    public void ViewNamesAreValuesCapturedWithTheTextLayer()
    {
        var source = Load("box_base.pdf").Artworks3D![0];
        string[] names = ["A", "B"];

        var artwork = new PdfArtwork3D
        {
            PpiScale = 1,
            BoundingBox = source.BoundingBox,
            Artwork = source.Artwork,
            HasNormalAppearance = false,
            ViewNames = names,
            DefaultViewName = "B"
        };

        Assert.Same(names, artwork.ViewNames);
        Assert.Equal("B", artwork.DefaultViewName);
        Assert.Same(source.ViewNames, source.ViewNames);
    }

    [Fact]
    public void DocumentWithout3D_HasNoArtworkList()
    {
        var layer = Load("ICML03-081.pdf");

        Assert.Null(layer.Artworks3D);
        Assert.Null(layer.FindArtwork3DOver(100, 100));
    }

    [Fact]
    public void ArtworkSurvivesAPageWithNoTextAndNoAnnotations()
    {
        var artwork = Load("box_base.pdf").Artworks3D![0];
        var content = new PageTextLayerContent { Letters = [], Annotations = [], Artworks3D = [artwork] };

        var layer = PdfTextLayerHelper.GetTextLayer(content, CancellationToken.None);

        Assert.Same(artwork, Assert.Single(layer.Artworks3D!));
    }

    [Fact]
    public void EmptyPageStillSharesTheEmptyLayer()
    {
        var content = new PageTextLayerContent { Letters = [], Annotations = [] };

        var first = PdfTextLayerHelper.GetTextLayer(content, CancellationToken.None);
        var second = PdfTextLayerHelper.GetTextLayer(content, CancellationToken.None);

        Assert.Same(first, second);
        Assert.Null(first.Artworks3D);
    }
}
