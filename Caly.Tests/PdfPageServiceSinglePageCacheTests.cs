using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Core.ViewModels;
using Caly.Tests.Fakes;
using static Caly.Tests.Fakes.DocumentTestHarness;

namespace Caly.Tests;

public class PdfPageServiceSinglePageCacheTests
{
    private static RefreshPagesRequestMessage Showing(DocumentViewModel document, int page, PageDisplayMode mode) => new()
    {
        Document = document,
        VisiblePages = new Range(page, page + 1),
        RealisedPages = new Range(page, page + 1),
        DisplayMode = mode
    };

    [AvaloniaFact]
    public async Task SinglePage_PrefetchesNeighboursWithoutShowingThem()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = NewLoadedDocument(pdfService, pageService, pageCount: 3);

        await pageService.RefreshPages(Showing(document, 2, PageDisplayMode.SinglePage));

        Assert.True(await WaitUntil(() =>
            document.Pages[1].PdfPicture is not null &&
            pdfService.RenderCountFor(1) == 1 &&
            pdfService.RenderCountFor(3) == 1));
        Assert.Null(document.Pages[0].PdfPicture);
        Assert.Null(document.Pages[2].PdfPicture);
    }

    [AvaloniaFact]
    public async Task Continuous_DoesNotPrefetch()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = NewLoadedDocument(pdfService, pageService, pageCount: 3);

        await pageService.RefreshPages(Showing(document, 2, PageDisplayMode.Continuous));
        Assert.True(await WaitUntil(() => document.Pages[1].PdfPicture is not null));
        await Task.Delay(100);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, pdfService.RenderCountFor(1));
        Assert.Equal(0, pdfService.RenderCountFor(3));
    }

    [AvaloniaFact]
    public async Task SinglePage_TurningToAPrefetchedPage_DoesNotRenderItAgain()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = NewLoadedDocument(pdfService, pageService, pageCount: 3);

        await pageService.RefreshPages(Showing(document, 1, PageDisplayMode.SinglePage));
        Assert.True(await WaitUntil(() => pdfService.RenderCountFor(2) == 1));

        await pageService.RefreshPages(Showing(document, 2, PageDisplayMode.SinglePage));
        Assert.True(await WaitUntil(() => document.Pages[1].PdfPicture is not null));

        Assert.Equal(1, pdfService.RenderCountFor(2));
    }

    [AvaloniaFact]
    public async Task SinglePage_TurningWhileTheNextPageIsStillPrefetching_DoesNotRenderItTwice()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = NewLoadedDocument(pdfService, pageService, pageCount: 3);
        pdfService.RenderGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await pageService.RefreshPages(Showing(document, 1, PageDisplayMode.SinglePage));
        Assert.True(await WaitUntil(() => pdfService.RenderCountFor(2) == 1)); // Prefetch of page 2 in flight

        // Turn to page 2 before its prefetch has finished rendering.
        await pageService.RefreshPages(Showing(document, 2, PageDisplayMode.SinglePage));
        pdfService.RenderGate.SetResult();

        Assert.True(await WaitUntil(() => document.Pages[1].PdfPicture is not null));
        Assert.Equal(1, pdfService.RenderCountFor(2));
    }

    [AvaloniaTheory]
    [InlineData(PageDisplayMode.SinglePage, 1)]
    [InlineData(PageDisplayMode.Continuous, 2)]
    public async Task ReturningThreePagesBack_OnlyContinuousRendersAgain(PageDisplayMode mode, int expectedRenders)
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = NewLoadedDocument(pdfService, pageService, pageCount: 8);

        await pageService.RefreshPages(Showing(document, 1, mode));
        Assert.True(await WaitUntil(() => document.Pages[0].PdfPicture is not null));

        await pageService.RefreshPages(Showing(document, 4, mode));
        Assert.True(await WaitUntil(() => document.Pages[3].PdfPicture is not null));

        await pageService.RefreshPages(Showing(document, 1, mode));
        Assert.True(await WaitUntil(() => document.Pages[0].PdfPicture is not null));

        Assert.Equal(expectedRenders, pdfService.RenderCountFor(1));
    }
}
