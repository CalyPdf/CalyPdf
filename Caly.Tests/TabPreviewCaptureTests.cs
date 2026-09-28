using Avalonia;
using Avalonia.Headless.XUnit;
using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Core.Services.Rendering;
using Caly.Core.ViewModels;
using Caly.Tests.Fakes;
using CommunityToolkit.Mvvm.Input;

namespace Caly.Tests;

/// <summary>
/// The tab hover preview is captured while the page the user was on is still in the picture
/// cache. These pin the two halves of that: the geometry, and the peek that must never render.
/// </summary>
public class TabPreviewCaptureTests
{
    // PageViewModel's parameterless constructor is design-mode-only (it throws otherwise), so
    // geometry tests use the same real constructor as PageViewModelThumbnailSizeTests.
    private static PageViewModel PageOfSize(double width, double height)
    {
        var page = new PageViewModel(
            pageNumber: 1,
            textSelection: new TextSelection(1),
            tileRenderService: new TileRenderService(),
            ppiScale: 1.0,
            copyTextCommand: new RelayCommand(() => { }));

        page.SetSize(new Size(width, height));
        return page;
    }

    [AvaloniaFact]
    public void TabPreviewSize_UsesTheFullHeightForAPortraitPage()
    {
        var page = PageOfSize(200, 400); // aspect 0.5

        Assert.Equal(new PixelSize(160, 320), page.TabPreviewSize);
    }

    [AvaloniaFact]
    public void TabPreviewSize_CapsTheWidthForALandscapePage()
    {
        var page = PageOfSize(400, 200); // aspect 2.0

        // Width would be 640, so the cap drives the height instead.
        Assert.Equal(new PixelSize(320, 160), page.TabPreviewSize);
    }

    [AvaloniaFact]
    public async Task TryCapturePreview_ReturnsNullAndRendersNothingWhenThePageIsNotCached()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = DocumentTestHarness.NewLoadedDocument(pdfService, pageService);

        // Off the UI thread here to match how CaptureTabPreview usually calls it - not required
        // for correctness (TryCapturePreview carries no thread assertion), just realistic.
        var preview = await Task.Run(() =>
            pageService.TryCapturePreview(1, document.Pages[0].TabPreviewSize, document.Pages[0].Size));

        Assert.Null(preview);
        Assert.Equal(0, pdfService.RenderCount);
    }

    [AvaloniaFact]
    public async Task TryCapturePreview_RasterisesTheCachedPictureAtTheRequestedSize()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = DocumentTestHarness.NewLoadedDocument(pdfService, pageService);

        // Populate the picture cache the way a real render does, then let go of our reference.
        using (var picture = await pageService.GetPicture(1, CancellationToken.None))
        {
            Assert.NotNull(picture);
        }

        Assert.Equal(1, pdfService.RenderCount);

        PixelSize target = document.Pages[0].TabPreviewSize;
        using var preview = await Task.Run(() =>
            pageService.TryCapturePreview(1, target, document.Pages[0].Size));

        Assert.NotNull(preview);
        Assert.Equal(target, preview.PixelSize);

        // The capture is a downscale of a picture already in the cache, never a new render.
        Assert.Equal(1, pdfService.RenderCount);
    }

    [AvaloniaFact]
    public async Task SetInactive_CapturesAPreviewOfTheSelectedPage()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = DocumentTestHarness.NewLoadedDocument(pdfService, pageService,
            pageCount: 2, pageSize: new Size(200, 400));

        document.SelectedPageNumber = 2;
        // The harness's default VisiblePages only covers page 1; widen it so page 2 - the
        // page under test - actually renders.
        document.VisiblePages = new Range(2, 3);
        document.SetActive();
        Assert.True(await DocumentTestHarness.WaitUntil(() => document.Pages[1].PdfPicture is not null),
            "page 2 to render for the active document");

        document.SetInactive();

        Assert.True(await DocumentTestHarness.WaitUntil(() => document.TabPreview is not null),
            "a preview to be captured as the document goes inactive");

        Assert.Equal(2, document.TabPreviewPageNumber);
        Assert.Equal(new PixelSize(160, 320), document.TabPreview!.PixelSize);
        Assert.Equal("Page 2 of 2", document.TabPreviewCaption);
    }

    /// <summary>
    /// The capture happens inside the teardown. The teardown must not then throw the capture
    /// away - that is the whole point of taking it there.
    /// </summary>
    [AvaloniaFact]
    public async Task SetInactive_LeavesThePreviewBehindAfterTheTeardownHasFinished()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = DocumentTestHarness.NewLoadedDocument(pdfService, pageService);

        document.SetActive();
        Assert.True(await DocumentTestHarness.WaitUntil(() => document.Pages[0].PdfPicture is not null),
            "the page to render for the active document");

        document.SetInactive();

        Assert.True(await DocumentTestHarness.WaitUntil(() => document.Pages[0].PdfPicture is null),
            "the teardown to release the page picture");

        // The capture now genuinely hops off the UI thread (Task.Run), so it can still be in
        // flight at this point even though the page picture was released earlier in the same
        // Clear() - wait for it rather than asserting immediately.
        Assert.True(await DocumentTestHarness.WaitUntil(() => document.TabPreview is not null),
            "the capture to finish publishing the preview");
    }

    /// <summary>
    /// A tab switched away from before its first render has nothing to capture. That must be a
    /// quiet no-op, not a render kicked off inside a teardown.
    /// </summary>
    [AvaloniaFact]
    public async Task SetInactive_CapturesNothingAndRendersNothingWhenThePageNeverRendered()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = DocumentTestHarness.NewLoadedDocument(pdfService, pageService);

        // Never activated, so nothing was ever rendered or cached.
        document.SetInactive();
        Assert.True(await DocumentTestHarness.WaitUntil(() => !document.IsActive), "the document to settle");

        Assert.Null(document.TabPreview);
        Assert.Equal(0, pdfService.RenderCount);
    }

    [AvaloniaFact]
    public async Task SetInactive_ASecondTimeReplacesThePreviewAndDisposesTheOldOne()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = DocumentTestHarness.NewLoadedDocument(pdfService, pageService);

        document.SetActive();
        Assert.True(await DocumentTestHarness.WaitUntil(() => document.Pages[0].PdfPicture is not null),
            "the page to render for the active document");
        document.SetInactive();
        Assert.True(await DocumentTestHarness.WaitUntil(() => document.TabPreview is not null),
            "the first preview");

        var first = document.TabPreview!;
        // Captured before the second deactivation disposes it - reading it afterwards is
        // exactly the disposal check below.
        var firstSize = first.PixelSize;

        document.SetActive();
        Assert.True(await DocumentTestHarness.WaitUntil(() => document.Pages[0].PdfPicture is not null),
            "the page to render again");
        document.SetInactive();

        Assert.True(await DocumentTestHarness.WaitUntil(() =>
            document.TabPreview is not null && !ReferenceEquals(document.TabPreview, first)),
            "a second, different preview");

        // The replacement is usable...
        Assert.Equal(firstSize, document.TabPreview!.PixelSize);

        // ...and the one it replaced was actually disposed. A second Dispose on a live
        // WriteableBitmap would not throw, so this - drawing from it - is the only way to
        // prove disposal happened.
        Assert.Throws<ObjectDisposedException>(() => first.PixelSize);
    }

    /// <summary>
    /// A capture failure (e.g. an SKSurface allocation failure inside RasterisePicture) must
    /// degrade to "no preview" - not stop <see cref="DocumentViewModel.Clear"/> short of
    /// <c>PdfPageService.CancelAndClear</c>, which would leave the inactive document holding
    /// its entire picture cache forever.
    /// </summary>
    [AvaloniaFact]
    public async Task SetInactive_StillTearsDownTheCacheWhenTheCaptureThrows()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = DocumentTestHarness.NewLoadedDocument(pdfService, pageService);

        document.SetActive();
        Assert.True(await DocumentTestHarness.WaitUntil(() => document.Pages[0].PdfPicture is not null),
            "the page to render for the active document");

        // Force the capture's own rasterisation to throw.
        pdfService.ThrowOnPpiScaleAccess = true;

        document.SetInactive();

        // Poll the picture cache directly rather than via WaitUntil: while the capture is still
        // throwing (i.e. CancelAndClear has not run yet), TryCapturePreview itself throws too,
        // since it reaches the same PpiScale read. That is expected and not a test failure -
        // only a cache that is still populated once CancelAndClear should have run is.
        bool cacheCleared = false;
        for (int i = 0; i < 400 && !cacheCleared; i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            try
            {
                cacheCleared = pageService.TryCapturePreview(
                    1, document.Pages[0].TabPreviewSize, document.Pages[0].Size) is null;
            }
            catch (InvalidOperationException)
            {
                // The cache still holds the picture; CancelAndClear has not run yet.
            }

            if (!cacheCleared)
            {
                await Task.Delay(10);
            }
        }

        Assert.True(cacheCleared,
            "CancelAndClear to still run - and empty the picture cache - despite the capture throwing");

        // The capture itself failed, so nothing was ever published.
        Assert.Null(document.TabPreview);
    }

    /// <summary>
    /// The design spec requires the eager capture to run off the UI thread. The ordinary case
    /// is that the previous activity transition has already completed by the time
    /// <see cref="DocumentViewModel.SetInactive"/> runs (the user was reading the document for a
    /// while before switching tabs), so <c>Clear()</c> - and everything in it up to its first
    /// un-configured await - runs synchronously on the calling (UI) thread. The rasterisation
    /// itself must still not land there.
    /// </summary>
    [AvaloniaFact]
    public async Task SetInactive_RasterisesTheCaptureOffTheCallingThread()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = DocumentTestHarness.NewLoadedDocument(pdfService, pageService);

        document.SetActive();
        Assert.True(await DocumentTestHarness.WaitUntil(() => document.Pages[0].PdfPicture is not null),
            "the page to render for the active document");

        int callingThreadId = Environment.CurrentManagedThreadId;

        document.SetInactive();

        Assert.True(await DocumentTestHarness.WaitUntil(() => document.TabPreview is not null),
            "a preview to be captured");

        Assert.NotNull(pdfService.LastPpiScaleAccessThreadId);
        Assert.NotEqual(callingThreadId, pdfService.LastPpiScaleAccessThreadId!.Value);
    }

    /// <summary>
    /// A rasterisation that never returns (e.g. Skia hanging on a pathological picture) must
    /// not hang the teardown forever. Giving up after a bounded wait still has to let
    /// CancelAndClear run - same requirement as a capture that throws outright, just reached
    /// through a different failure mode.
    /// </summary>
    [AvaloniaFact]
    public async Task SetInactive_GivesUpCapturingAfterTheTimeoutAndStillTearsDownTheCache()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = DocumentTestHarness.NewLoadedDocument(pdfService, pageService,
            tabPreviewTimeout: TimeSpan.FromMilliseconds(50));

        document.SetActive();
        Assert.True(await DocumentTestHarness.WaitUntil(() => document.Pages[0].PdfPicture is not null),
            "the page to render for the active document");

        pdfService.HangOnPpiScaleAccess = true;
        try
        {
            document.SetInactive();

            // Clear() is not observable from outside (it runs as a plain delegate via
            // QueueActivityTransition, not through a command's ExecutionTask), and polling the
            // cache through TryCapturePreview would itself block on the same hang while the
            // picture is still cached - so wait out a generous multiple of the configured
            // timeout instead, then check the cache once the hang no longer matters.
            await Task.Delay(1000);
        }
        finally
        {
            // Safe to release now regardless of what ran: this only lets the abandoned
            // rasterisation actually finish and free its thread-pool thread.
            pdfService.ReleaseHang();
        }

        Assert.True(await DocumentTestHarness.WaitUntil(() =>
            pageService.TryCapturePreview(1, document.Pages[0].TabPreviewSize, document.Pages[0].Size) is null),
            "CancelAndClear to have emptied the picture cache despite the capture timing out");

        // The capture itself never finished, so nothing was ever published.
        Assert.Null(document.TabPreview);
    }

    [AvaloniaFact]
    public async Task DisposeAsync_ReleasesThePreview()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = DocumentTestHarness.NewLoadedDocument(pdfService, pageService);

        document.SetActive();
        Assert.True(await DocumentTestHarness.WaitUntil(() => document.Pages[0].PdfPicture is not null),
            "the page to render for the active document");
        document.SetInactive();
        Assert.True(await DocumentTestHarness.WaitUntil(() => document.TabPreview is not null),
            "a preview to capture");

        await Task.Run(async () => await document.DisposeAsync());

        Assert.Null(document.TabPreview);
    }
}
