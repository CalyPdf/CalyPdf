// Copyright (c) BobLd
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Headless.XUnit;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Core.Services.Interfaces;
using Caly.Core.Utilities;
using Caly.Core.ViewModels;
using Caly.Pdf.Models;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace Caly.Tests;

/// <summary>
/// Becoming inactive releases everything a document has rendered; becoming active again has to
/// put it back.
/// <para>
/// Dropping a file Caly cannot parse is the case that exposed this. The failed document is
/// added to the tab strip and selected while it opens, which deactivates the document the user
/// was reading and clears it; when the open fails the tab is taken away again and the original
/// document is re-selected. Nothing re-requested its pages, and because the teardown is
/// asynchronous it could also land after the reactivation and cancel the render generation that
/// reactivation had just started - leaving the page on its loading skeleton and the thumbnails
/// on their placeholder for good.
/// </para>
/// </summary>
public class DocumentActivationRestoreTests
{
    /// <summary>
    /// A document service that renders a solid picture per page and counts the renders.
    /// <para>
    /// <see cref="NumberOfPages"/> stays 0 until <see cref="Publish"/>, because
    /// <see cref="DocumentViewModel"/>'s constructor asserts the document is not open yet.
    /// </para>
    /// </summary>
    private sealed class RenderingPdfDocumentService : IPdfDocumentService
    {
        private int _numberOfPages;
        private int _renderCount;

        public void Publish(int numberOfPages) => _numberOfPages = numberOfPages;

        /// <summary>How many times a page picture has actually been rendered.</summary>
        public int RenderCount => Volatile.Read(ref _renderCount);

        public int NumberOfPages => _numberOfPages;
        public string? FileName => "rendering.pdf";
        public bool IsActive { get; set; }
        public double PpiScale => 1.0;

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

    private sealed class NoopTextSearchService : ITextSearchService
    {
        public Task BuildPdfDocumentIndex(IProgress<int> progress, CancellationToken token) => Task.CompletedTask;

        public IEnumerable<TextSearchResult> Search(string text, IReadOnlyCollection<int> pagesToSkip, CancellationToken token) => [];

        public void Dispose()
        {
        }
    }

    private sealed class StubFilesService : IFilesService
    {
        public Task<IStorageFile?> OpenPdfFileAsync(Window? owner = null) => Task.FromResult<IStorageFile?>(null);

        public Task<IStorageFile?> SaveFileAsync(ReadOnlyMemory<byte> data, string? fileName = null)
            => Task.FromResult<IStorageFile?>(null);

        public Task<IStorageFile?> SaveTempFileAsync(ReadOnlyMemory<byte> data, string? fileName = null)
            => Task.FromResult<IStorageFile?>(null);

        public Task<IStorageFile?> TryGetFileFromPathAsync(string path) => Task.FromResult<IStorageFile?>(null);
    }

    private sealed class StubDialogService : IDialogService
    {
        public void ShowNotification(string? title, string? message, NotificationType type, MainViewModel? target = null) { }

        public void ShowNotification(CalyNotification notification, MainViewModel? target = null) { }

        public Task ShowExceptionWindowAsync(Exception exception) => Task.CompletedTask;

        public Task ShowExceptionWindowAsync(ExceptionViewModel exception) => Task.CompletedTask;

        public void ShowExceptionWindow(Exception exception) { }

        public void ShowExceptionWindow(ExceptionViewModel exception) { }

        public Task ShowPrintDialogAsync(IPdfDocumentService documentService, int currentPage, CancellationToken token)
            => Task.CompletedTask;
    }

    private sealed class StubClipboardService : IClipboardService
    {
        public Task<bool> SetAsync(TextSelection selection, PdfPageService pdfPageService, CancellationToken token)
            => Task.FromResult(true);

        public Task SetAsync(string text) => Task.CompletedTask;

        public Task ClearAsync() => Task.CompletedTask;
    }

    /// <summary>
    /// A document with <paramref name="pageCount"/> ready-to-render pages whose view has already
    /// reported what is on screen - the state a document is in once the user is reading it.
    /// </summary>
    private static DocumentViewModel NewLoadedDocument(RenderingPdfDocumentService pdfService,
        PdfPageService pageService, int pageCount = 2)
    {
        var document = new DocumentViewModel(pdfService, pageService, new NoopTextSearchService());

        pdfService.Publish(pageCount);
        pageService.Initialise();

        document.PageCount = pageCount;
        document.TextSelection = new TextSelection(pageCount);

        for (int p = 1; p <= pageCount; ++p)
        {
            var page = new PageViewModel(p, document.TextSelection, pageService.TileRenderService,
                pdfService.PpiScale, document.CopyTextCommand);
            page.SetSize(new Size(100, 100));
            document.Pages.Add(page);
        }

        document.RealisedPages = new Range(1, pageCount + 1);
        document.VisiblePages = new Range(1, 2);

        return document;
    }

    /// <summary>
    /// Renders run on the thread pool but hand their result back through the dispatcher, which
    /// only drains while the test thread pumps it.
    /// </summary>
    private static async Task<bool> WaitUntil(Func<bool> condition)
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

    private static async Task<bool> WaitForPicture(DocumentViewModel document) =>
        await WaitUntil(() => document.Pages[0].PdfPicture is not null);

    [AvaloniaFact]
    public async Task SetActive_PutsBackThePictureSetInactiveReleased()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = NewLoadedDocument(pdfService, pageService);

        document.SetActive();
        Assert.True(await WaitForPicture(document), "the page to render for the active document");

        document.SetInactive();
        Assert.True(await WaitUntil(() => document.Pages[0].PdfPicture is null),
            "the page picture to be released while the document is inactive");

        document.SetActive();

        Assert.True(await WaitForPicture(document),
            "the page picture to come back once the document is active again");
    }

    /// <summary>
    /// The reported failure: the reactivation arrives while the deactivation's teardown is still
    /// running. The two must be sequenced, or the teardown cancels the render generation the
    /// reactivation started and the page never renders again.
    /// </summary>
    [AvaloniaFact]
    public async Task SetActive_ImmediatelyAfterSetInactive_StillEndsUpWithARenderedPage()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = NewLoadedDocument(pdfService, pageService);

        document.SetActive();
        Assert.True(await WaitForPicture(document), "the page to render for the active document");

        // No pumping in between: the teardown is still in flight when the document comes back.
        document.SetInactive();
        document.SetActive();

        Assert.True(await WaitForPicture(document),
            "the page picture to survive a deactivation the reactivation overtook");
    }

    /// <summary>
    /// A document that is inactive for good keeps releasing what it rendered - the teardown is
    /// how Caly gives back the memory of documents the user is not looking at.
    /// </summary>
    [AvaloniaFact]
    public async Task SetInactive_StillReleasesThePictureWhenTheDocumentStaysInactive()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = NewLoadedDocument(pdfService, pageService);

        document.SetActive();
        Assert.True(await WaitForPicture(document), "the page to render for the active document");

        document.SetInactive();

        Assert.True(await WaitUntil(() => document.Pages[0].PdfPicture is null),
            "the page picture to be released");
    }

    /// <summary>
    /// End to end through the manager, on the message that actually drives activation: a
    /// document that fails to open takes the selection for a moment and gives it straight back,
    /// which must leave the document the user was reading exactly as it was.
    /// </summary>
    [AvaloniaFact]
    public async Task AFailedOpenStealingTheSelectionLeavesTheOpenDocumentRendered()
    {
        var registry = new CalyWindowRegistry();
        var window = new MainViewModel();
        window.Dispose(); // Only the tab collection is needed, not its background subscription.
        registry.Register(new CalyWindowContext { ViewModel = window, Window = null, IsPrimary = true });

        using var manager = new PdfDocumentsManagerService(registry, new StubFilesService(),
            new StubDialogService(), new StubClipboardService());

        var readingService = new RenderingPdfDocumentService();
        await using var readingPageService = new PdfPageService(readingService);
        var reading = NewLoadedDocument(readingService, readingPageService);

        var failingService = new RenderingPdfDocumentService();
        await using var failingPageService = new PdfPageService(failingService);
        var failing = new DocumentViewModel(failingService, failingPageService, new NoopTextSearchService());

        Assert.True(manager.TryAddRecord("reading.pdf", reading, NewScope()));
        Assert.True(manager.TryAddRecord("failing.png", failing, NewScope()));

        window.PdfDocuments.Add(reading);
        window.SelectedDocumentIndex = 0;
        Dispatcher.UIThread.RunJobs();

        Assert.True(await WaitForPicture(reading), "the page to render for the document being read");

        // The failed open puts its tab in the strip and selects it while it parses.
        window.PdfDocuments.Add(failing);
        window.SelectedDocumentIndex = 1;
        Dispatcher.UIThread.RunJobs();

        // ... and takes it back out again when the parse fails.
        window.PdfDocuments.Remove(failing);
        window.SelectedDocumentIndex = 0;
        manager.TryRemoveRecord("failing.png", failing);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(reading, window.SelectedDocument);
        Assert.True(reading.IsActive);
        Assert.True(await WaitForPicture(reading),
            "the document the user was reading to still have a rendered page");
    }

    /// <summary>
    /// An empty DI scope: the records under test are only ever disposed, never resolved from.
    /// </summary>
    private static AsyncServiceScope NewScope() =>
        new ServiceCollection().BuildServiceProvider().CreateAsyncScope();
}
