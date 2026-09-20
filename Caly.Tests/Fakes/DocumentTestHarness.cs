using Avalonia;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Core.Services.Interfaces;
using Caly.Core.Utilities;
using Caly.Core.ViewModels;
using Caly.Pdf.Models;
using SkiaSharp;

namespace Caly.Tests.Fakes;

/// <summary>
/// A document service that renders a solid picture per page and counts the renders.
/// <para>
/// <see cref="NumberOfPages"/> stays 0 until <see cref="Publish"/>, because
/// <see cref="DocumentViewModel"/>'s constructor asserts the document is not open yet.
/// </para>
/// </summary>
internal sealed class RenderingPdfDocumentService : IPdfDocumentService
{
    private int _numberOfPages;
    private int _renderCount;

    public void Publish(int numberOfPages) => _numberOfPages = numberOfPages;

    /// <summary>How many times a page picture has actually been rendered.</summary>
    public int RenderCount => Volatile.Read(ref _renderCount);

    public int NumberOfPages => _numberOfPages;
    public string? FileName => "rendering.pdf";
    public bool IsActive { get; set; }

    /// <summary>
    /// When set, reading <see cref="PpiScale"/> throws instead of returning a value - simulates
    /// a rasterisation failure (e.g. an SKSurface allocation failure) inside
    /// PdfPageService.RasterisePicture, which reads this as its last argument.
    /// </summary>
    public bool ThrowOnPpiScaleAccess { get; set; }

    /// <summary>
    /// When set, reading <see cref="PpiScale"/> blocks until <see cref="ReleaseHang"/> is
    /// called - simulates a pathological page whose rasterisation never returns, so a timeout
    /// can be exercised without waiting out a real one.
    /// </summary>
    public bool HangOnPpiScaleAccess { get; set; }

    private readonly ManualResetEventSlim _hangGate = new(initialState: false);

    /// <summary>
    /// Lets a <see cref="PpiScale"/> read blocked on <see cref="HangOnPpiScaleAccess"/> return.
    /// Always call this before the test ends, even after asserting a timeout fired: the render
    /// that timed out is abandoned, not stopped, so its thread-pool thread is only freed once
    /// this unblocks it.
    /// </summary>
    public void ReleaseHang() => _hangGate.Set();

    /// <summary>The managed thread id observed the last time <see cref="PpiScale"/> was read.</summary>
    public int? LastPpiScaleAccessThreadId { get; private set; }

    public double PpiScale
    {
        get
        {
            if (ThrowOnPpiScaleAccess)
            {
                throw new InvalidOperationException("Simulated PpiScale failure.");
            }

            if (HangOnPpiScaleAccess)
            {
                _hangGate.Wait();
            }

            LastPpiScaleAccessThreadId = Environment.CurrentManagedThreadId;
            return 1.0;
        }
    }

    public Task<IRef<SKPicture>?> GetRenderPageAsync(int pageNumber, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _renderCount);

        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(new SKRect(0, 0, 100, 100));
        using (var paint = new SKPaint { Color = SKColors.Red })
        {
            canvas.DrawRect(new SKRect(0, 0, 100, 100), paint);
        }

        return Task.FromResult<IRef<SKPicture>?>(RefCountable.Create(recorder.EndRecording()));
    }

    // Page sizes are set up front by the tests, so the render path never asks for one.
    public Task<UglyToad.PdfPig.Rendering.Skia.PdfPageSize?> GetPageSizeAsync(int pageNumber, CancellationToken token)
        => throw new NotImplementedException();

    public Task<PdfTextLayer?> GetPageTextLayerAsync(int pageNumber, CancellationToken token)
        => Task.FromResult<PdfTextLayer?>(null);

    public long? FileSize => throw new NotImplementedException();
    public string? LocalPath => throw new NotImplementedException();
    public bool IsPasswordProtected => throw new NotImplementedException();
    public Func<CancellationToken, Task<string?>>? PasswordPrompt { get; set; }

    public Task<DocumentOpeningState> OpenDocument(IStorageFile? storageFile, string? password, CancellationToken token)
        => throw new NotImplementedException();

    public Task<DocumentPropertiesViewModel?> GetDocumentPropertiesAsync(CancellationToken token)
        => throw new NotImplementedException();

    public Task<IReadOnlyList<PdfBookmarkNode>?> GetPdfBookmark(CancellationToken token)
        => throw new NotImplementedException();

    public Task<IReadOnlyList<PdfEmbeddedFileViewModel>?> GetEmbeddedFileAsync(CancellationToken token)
        => throw new NotImplementedException();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class NoopTextSearchService : ITextSearchService
{
    public Task BuildPdfDocumentIndex(IProgress<int> progress, CancellationToken token) => Task.CompletedTask;

    public IEnumerable<TextSearchResult> Search(string text, IReadOnlyCollection<int> pagesToSkip, CancellationToken token) => [];

    public void Dispose()
    {
    }
}

internal static class DocumentTestHarness
{
    /// <summary>
    /// A document with <paramref name="pageCount"/> ready-to-render pages whose view has
    /// already reported what is on screen - the state a document is in once the user is
    /// reading it.
    /// </summary>
    public static DocumentViewModel NewLoadedDocument(RenderingPdfDocumentService pdfService,
        PdfPageService pageService, int pageCount = 2, Size? pageSize = null,
        TimeSpan? tabPreviewTimeout = null)
    {
        var document = new DocumentViewModel(pdfService, pageService, new NoopTextSearchService(),
            tabPreviewTimeout);

        pdfService.Publish(pageCount);
        pageService.Initialise();

        document.PageCount = pageCount;
        document.TextSelection = new TextSelection(pageCount);

        // Mirrors LoadDocumentCore's own post-success assignment - SelectedPageNumber has no
        // field initialiser any more (it must stay null until PageCount is real, or its setter's
        // validation would trip against the default 0), so this harness has to set it explicitly
        // to reproduce "a document the user has already been reading".
        if (pageCount > 0)
        {
            document.SelectedPageNumber = 1;
        }

        for (int p = 1; p <= pageCount; ++p)
        {
            var page = new PageViewModel(p, document.TextSelection, pageService.TileRenderService,
                pdfService.PpiScale, document.CopyTextCommand);
            page.SetSize(pageSize ?? new Size(100, 100));
            document.Pages.Add(page);
        }

        document.RealisedPages = new Range(1, pageCount + 1);
        document.VisiblePages = new Range(1, 2);

        return document;
    }

    /// <summary>
    /// Renders run on the thread pool but hand their result back through the dispatcher,
    /// which only drains while the test thread pumps it.
    /// </summary>
    public static async Task<bool> WaitUntil(Func<bool> condition)
    {
        for (int i = 0; i < 400; ++i)
        {
            Dispatcher.UIThread.RunJobs();

            if (condition())
            {
                return true;
            }

            await Task.Delay(10);
        }

        Dispatcher.UIThread.RunJobs();
        return condition();
    }
}
