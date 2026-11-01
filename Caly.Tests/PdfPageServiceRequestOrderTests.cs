using Avalonia.Headless.XUnit;
using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Tests.Fakes;
using static Caly.Tests.Fakes.DocumentTestHarness;

namespace Caly.Tests;

public class PdfPageServiceRequestOrderTests
{
    /// <summary>
    /// PdfPig reads one page at a time. A page that comes into view must wait for the render
    /// already running, never for lower-priority requests that were queued before it.
    /// </summary>
    [AvaloniaFact]
    public async Task VisiblePagePicture_RendersBeforeThumbnailsQueuedEarlier()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = NewLoadedDocument(pdfService, pageService, pageCount: 8);
        pdfService.RenderGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await pageService.RefreshThumbnails(new RefreshPagesRequestMessage
        {
            Document = document,
            VisibleThumbnails = new Range(3, 8),
            RealisedThumbnails = new Range(3, 8),
            DisplayMode = PageDisplayMode.Continuous
        });
        Assert.True(await WaitUntil(() => pdfService.RenderCountFor(3) == 1)); // Thumbnail of page 3 rendering

        await pageService.RefreshPages(new RefreshPagesRequestMessage
        {
            Document = document,
            VisiblePages = new Range(2, 3),
            RealisedPages = new Range(2, 3),
            DisplayMode = PageDisplayMode.Continuous
        });
        pdfService.RenderGate.SetResult();

        Assert.True(await WaitUntil(() => document.Pages[1].PdfPicture is not null));
        Assert.Equal([3, 2], pdfService.RenderOrder.Take(2));
    }
}
