using Avalonia.Headless.XUnit;
using Caly.Core.Services;
using Caly.Core.ViewModels;
using Caly.Tests.Fakes;

namespace Caly.Tests;

/// <summary>
/// A document deactivated before it ever rendered has no captured preview. Hovering its tab
/// may render one - but it must leave the document holding nothing afterwards, exactly as it
/// found it.
/// </summary>
public class TabPreviewLazyRenderTests
{
    private static async Task<DocumentViewModel> NewInactiveDocument(
        RenderingPdfDocumentService pdfService, PdfPageService pageService,
        TimeSpan? tabPreviewTimeout = null)
    {
        var document = DocumentTestHarness.NewLoadedDocument(pdfService, pageService,
            tabPreviewTimeout: tabPreviewTimeout);

        document.SetInactive();
        Assert.True(await DocumentTestHarness.WaitUntil(() => !document.IsActive),
            "the document to settle as inactive");

        return document;
    }

    [AvaloniaFact]
    public async Task EnsureTabPreview_RendersAPreviewForADocumentThatNeverGotOne()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = await NewInactiveDocument(pdfService, pageService);

        Assert.Null(document.TabPreview);

        // Fired, not awaited: the render's continuations come back through the dispatcher, and
        // only WaitUntil pumps it. Awaiting here would deadlock against that.
        document.EnsureTabPreviewCommand.Execute(null);

        Assert.True(await DocumentTestHarness.WaitUntil(() => document.TabPreview is not null),
            "the hover to produce a preview");

        Assert.Equal(1, document.TabPreviewPageNumber);
    }

    /// <summary>
    /// The lazy render repopulates the picture cache of a document that is meant to be holding
    /// nothing. It has to put that back.
    /// </summary>
    [AvaloniaFact]
    public async Task EnsureTabPreview_LeavesTheInactiveDocumentHoldingNoPicture()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = await NewInactiveDocument(pdfService, pageService);

        document.EnsureTabPreviewCommand.Execute(null);
        Assert.True(await DocumentTestHarness.WaitUntil(() =>
            document.TabPreview is not null &&
            document.EnsureTabPreviewCommand.ExecutionTask is { IsCompleted: true }),
            "the hover to produce a preview and finish tidying up");

        // TryCapturePreview only succeeds from the cache, so a null here proves it is empty.
        var cached = await Task.Run(() =>
            pageService.TryCapturePreview(1, document.Pages[0].TabPreviewSize, document.Pages[0].Size));

        Assert.Null(cached);
    }

    [AvaloniaFact]
    public async Task EnsureTabPreview_DoesNothingWhenAPreviewAlreadyExists()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = DocumentTestHarness.NewLoadedDocument(pdfService, pageService);

        document.SetActive();
        Assert.True(await DocumentTestHarness.WaitUntil(() => document.Pages[0].PdfPicture is not null),
            "the page to render for the active document");
        document.SetInactive();
        Assert.True(await DocumentTestHarness.WaitUntil(() => document.TabPreview is not null),
            "the eager capture to produce a preview");

        var captured = document.TabPreview;
        int rendersBefore = pdfService.RenderCount;

        document.EnsureTabPreviewCommand.Execute(null);
        Assert.True(await DocumentTestHarness.WaitUntil(() =>
            document.EnsureTabPreviewCommand.ExecutionTask is { IsCompleted: true }),
            "the command to return");

        Assert.Same(captured, document.TabPreview);
        Assert.Equal(rendersBefore, pdfService.RenderCount);
    }

    [AvaloniaFact]
    public async Task EnsureTabPreview_DoesNothingForTheActiveDocument()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = DocumentTestHarness.NewLoadedDocument(pdfService, pageService);

        document.SetActive();
        Assert.True(await DocumentTestHarness.WaitUntil(() => document.Pages[0].PdfPicture is not null),
            "the page to render for the active document");

        document.EnsureTabPreviewCommand.Execute(null);
        Assert.True(await DocumentTestHarness.WaitUntil(() =>
            document.EnsureTabPreviewCommand.ExecutionTask is { IsCompleted: true }),
            "the command to return");

        Assert.Null(document.TabPreview);
    }

    /// <summary>
    /// The user selected the tab while the hover render was in flight. Tearing the cache down
    /// then would blank the document they are now looking at.
    /// </summary>
    [AvaloniaFact]
    public async Task EnsureTabPreview_DoesNotTearDownADocumentThatWasReactivatedMeanwhile()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = await NewInactiveDocument(pdfService, pageService);

        document.EnsureTabPreviewCommand.Execute(null);

        // Reactivate without pumping first, so it lands while the render is still running.
        document.SetActive();

        Assert.True(await DocumentTestHarness.WaitUntil(() => document.Pages[0].PdfPicture is not null),
            "the reactivated document to hold a rendered page");
    }

    /// <summary>
    /// The tooltip shows a loading indicator while a hover render is actually in flight - this
    /// pins the flag's on/off transitions, using a controllable hang so "in flight" is
    /// observable rather than assumed from timing.
    /// </summary>
    [AvaloniaFact]
    public async Task EnsureTabPreview_SetsIsTabPreviewLoadingWhileTheRenderIsInFlight()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = await NewInactiveDocument(pdfService, pageService);

        Assert.False(document.IsTabPreviewLoading);

        pdfService.HangOnPpiScaleAccess = true;
        try
        {
            document.EnsureTabPreviewCommand.Execute(null);

            Assert.True(await DocumentTestHarness.WaitUntil(() => document.IsTabPreviewLoading),
                "the loading flag to be set while the render is in flight");

            // Nothing published yet - the rasterisation is still blocked.
            Assert.Null(document.TabPreview);
        }
        finally
        {
            pdfService.ReleaseHang();
        }

        Assert.True(await DocumentTestHarness.WaitUntil(() =>
            document.EnsureTabPreviewCommand.ExecutionTask is { IsCompleted: true }),
            "the render to finish once released");

        Assert.False(document.IsTabPreviewLoading);
        Assert.NotNull(document.TabPreview);
    }

    /// <summary>
    /// A rasterisation that never returns must not leave the hover waiting - or the loading
    /// indicator spinning - forever.
    /// </summary>
    [AvaloniaFact]
    public async Task EnsureTabPreview_GivesUpAfterTheTimeoutAndLeavesNoPreview()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = await NewInactiveDocument(pdfService, pageService,
            tabPreviewTimeout: TimeSpan.FromMilliseconds(50));

        pdfService.HangOnPpiScaleAccess = true;
        try
        {
            document.EnsureTabPreviewCommand.Execute(null);

            Assert.True(await DocumentTestHarness.WaitUntil(() =>
                document.EnsureTabPreviewCommand.ExecutionTask is { IsCompleted: true }),
                "the command to give up waiting once the timeout elapses");

            Assert.Null(document.TabPreview);
            Assert.False(document.IsTabPreviewLoading);
        }
        finally
        {
            pdfService.ReleaseHang();
        }

        // The abandoned render still populated the picture cache before hanging on the
        // rasterise step; the timeout path must tear that back down like any other giving-up.
        Assert.True(await DocumentTestHarness.WaitUntil(() =>
            pageService.TryCapturePreview(1, document.Pages[0].TabPreviewSize, document.Pages[0].Size) is null),
            "the timed-out render to still leave the document holding no picture");
    }

    [AvaloniaFact]
    public async Task CancelTabPreview_StopsAnInFlightHoverRender()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = await NewInactiveDocument(pdfService, pageService);

        document.EnsureTabPreviewCommand.Execute(null);
        document.CancelTabPreview();

        // Cancellation is swallowed - the pointer leaving a tab is not a failure - so the
        // command completes rather than faulting.
        Assert.True(await DocumentTestHarness.WaitUntil(() =>
            document.EnsureTabPreviewCommand.ExecutionTask is { IsCompleted: true }),
            "the hover render to unwind");

        Assert.Null(document.TabPreview);
    }
}
