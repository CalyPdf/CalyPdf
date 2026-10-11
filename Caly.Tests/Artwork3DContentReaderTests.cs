using Caly.Pdf;
using Caly.Pdf.Artwork;
using Caly.Pdf.Models;
using Caly.Pdf.PageFactories;
using Scene3D;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Artwork;
using UglyToad.PdfPig.Tokens;

namespace Caly.Tests;

/// <summary>
/// An open document with 3D artwork on one of its pages, for tests that read the artwork's data (which
/// needs the document open, unlike what the text layer carries).
/// </summary>
internal sealed class Artwork3DTestDocument : IDisposable
{
    private readonly PdfDocument _document;

    private Artwork3DTestDocument(PdfDocument document, PdfTextLayer layer)
    {
        _document = document;
        Layer = layer;
    }

    public PdfTextLayer Layer { get; }

    public IReadOnlyList<PdfArtwork3D> Artworks => Layer.Artworks3D ?? [];

    // box_base.pdf comes from the Universal 3D Sample Software (Samples/TestScenes), Apache License 2.0.
    // Its 3D annotation covers [0 0 300 300], names one view ("DefaultView"), and holds a U3D box.
    public static string BoxPath => Path.Combine(AppContext.BaseDirectory, "Documents", "box_base.pdf");

    public static Artwork3DTestDocument OpenBox() => Open(BoxPath);

    public static Artwork3DTestDocument Open(string path, int pageNumber = 1)
    {
        PdfDocument document = PdfDocument.Open(path, new ParsingOptions { UseActualText = true });

        // TextLayerFactory reads the PPI scale back out of this fake indirect object, the way
        // PdfPigDocumentService puts it there.
        document.Advanced.ReplaceIndirectObject(CalyPdfHelper.FakePpiReference, new NumericToken(1));
        document.AddPageFactory<PageTextLayerContent, TextLayerFactory>();
        PdfTextLayer layer = PdfTextLayerHelper.GetTextLayer(document.GetPageTextLayerContent(pageNumber, CancellationToken.None), CancellationToken.None);
        return new Artwork3DTestDocument(document, layer);
    }

    public void Dispose() => _document.Dispose();
}

public class Artwork3DContentReaderTests
{
    [Fact]
    public void TheContentOfARealDocumentIsItsPayloadAndItsViews()
    {
        using Artwork3DTestDocument document = Artwork3DTestDocument.OpenBox();
        PdfArtwork3D artwork = Assert.Single(document.Artworks);

        Artwork3DContent content = Artwork3DContentReader.Read(artwork);

        Assert.Equal(Artwork3DFormat.U3D, content.Format);
        Assert.False(content.Payload.IsEmpty);
        Assert.Equal(content.Views.Count, content.ViewNames.Count);
        Assert.Equal(artwork.ViewNames, content.ViewNames);
        SceneView view = Assert.Single(content.Views);
        Assert.Equal("DefaultView", view.Name);
        Assert.Equal(0, content.DefaultViewIndex);
    }

    [Fact]
    public void EachReadGivesTheSameBytes()
    {
        using Artwork3DTestDocument document = Artwork3DTestDocument.OpenBox();
        PdfArtwork3D artwork = document.Artworks[0];

        Artwork3DContent first = Artwork3DContentReader.Read(artwork);
        Artwork3DContent second = Artwork3DContentReader.Read(artwork);

        Assert.True(first.Payload.Span.SequenceEqual(second.Payload.Span));
    }

    [Fact]
    public void EveryDocumentOfTheCorpusGivesAViewForEveryName()
    {
        IReadOnlyList<string> documents = Artwork3DCameraConventionTests.CorpusDocuments("simlab");
        if (documents.Count == 0)
        {
            Assert.Skip("Set CALY_3D_SAMPLES to the folder of sample 3D PDFs.");
        }

        int artworks = 0;
        foreach (string path in documents)
        {
            int pages;
            using (PdfDocument probe = PdfDocument.Open(path))
            {
                pages = probe.NumberOfPages;
            }

            for (int page = 1; page <= pages; page++)
            {
                using Artwork3DTestDocument document = Artwork3DTestDocument.Open(path, page);
                foreach (PdfArtwork3D artwork in document.Artworks)
                {
                    Artwork3DContent content = Artwork3DContentReader.Read(artwork);
                    artworks++;
                    Assert.False(content.Payload.IsEmpty);
                    Assert.Equal(content.Views.Count, content.ViewNames.Count);

                    // The default view is one of the listed views, or is added after them.
                    Assert.InRange(content.Views.Count, artwork.ViewNames.Count, artwork.ViewNames.Count + 1);
                    Assert.InRange(content.DefaultViewIndex, content.Views.Count == 0 ? -1 : 0, content.Views.Count - 1);
                    if (artwork.DefaultViewName is { } name && content.DefaultViewIndex >= 0)
                    {
                        Assert.Equal(name, content.ViewNames[content.DefaultViewIndex]);
                    }
                }
            }
        }

        Assert.True(artworks >= documents.Count);
    }
}
