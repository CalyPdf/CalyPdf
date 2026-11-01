using Avalonia.Headless.XUnit;
using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Core.Services.Rendering;
using Caly.Core.ViewModels;
using Caly.Tests.Fakes;
using SkiaSharp;
using static Caly.Tests.Fakes.DocumentTestHarness;

namespace Caly.Tests;

/// <summary>
/// A page's tiles outlive its picture, within the tile cache's own memory budget, so scrolling back
/// to a page shows it at once while its picture is parsed again.
/// </summary>
public class PdfPageServiceTileRetentionTests
{
    private static RefreshPagesRequestMessage Showing(DocumentViewModel document, int page) => new()
    {
        Document = document,
        VisiblePages = new Range(page, page + 1),
        RealisedPages = new Range(page, page + 1),
        DisplayMode = PageDisplayMode.Continuous
    };

    private static void AddTile(PdfPageService pageService, int page)
    {
        using var surface = SKSurface.Create(new SKImageInfo(8, 8));
        pageService.TileRenderService.Cache.Add(new TileKey(page, 0, 0, 0), new TileImage(surface.Snapshot()));
    }

    /// <summary>Shows page 1, gives it a tile, then shows page 6, which evicts page 1's picture.</summary>
    private static async Task<(RenderingPdfDocumentService, PdfPageService, DocumentViewModel)> ScrolledAwayFromPage1()
    {
        var pdfService = new RenderingPdfDocumentService();
        var pageService = new PdfPageService(pdfService);
        var document = NewLoadedDocument(pdfService, pageService, pageCount: 8);

        await pageService.RefreshPages(Showing(document, 1));
        Assert.True(await WaitUntil(() => document.Pages[0].PdfPicture is not null));
        AddTile(pageService, 1);

        await pageService.RefreshPages(Showing(document, 6));
        Assert.True(await WaitUntil(() => document.Pages[5].PdfPicture is not null));

        return (pdfService, pageService, document);
    }

    [AvaloniaFact]
    public async Task EvictingAPicture_KeepsItsTiles()
    {
        var (_, pageService, _) = await ScrolledAwayFromPage1();
        await using var _ = pageService;

        Assert.True(pageService.TileRenderService.Cache.HasImages(1));
    }

    [AvaloniaFact]
    public async Task ReRenderOfTheSamePage_KeepsItsTiles()
    {
        var (pdfService, pageService, document) = await ScrolledAwayFromPage1();
        await using var _ = pageService;

        await pageService.RefreshPages(Showing(document, 1));
        Assert.True(await WaitUntil(() => document.Pages[0].PdfPicture is not null));

        Assert.Equal(2, pdfService.RenderCountFor(1));
        Assert.True(pageService.TileRenderService.Cache.HasImages(1));
    }

    /// <summary>
    /// A page can come back different, e.g. the "could not display" picture of a render that timed
    /// out followed by one that succeeds. Its old tiles would otherwise hide the new picture.
    /// </summary>
    [AvaloniaFact]
    public async Task ReRenderThatDrawsSomethingElse_DropsTheOldTiles()
    {
        var (pdfService, pageService, document) = await ScrolledAwayFromPage1();
        await using var _ = pageService;
        pdfService.DrawnSide = 50;

        await pageService.RefreshPages(Showing(document, 1));
        Assert.True(await WaitUntil(() => document.Pages[0].PdfPicture is not null));

        Assert.False(pageService.TileRenderService.Cache.HasImages(1));
    }
}
