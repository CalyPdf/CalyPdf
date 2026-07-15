using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Core.Services.Interfaces;
using Caly.Core.Utilities;
using Caly.Core.ViewModels;
using Caly.Pdf.Models;
using SkiaSharp;

namespace Caly.Tests;

/// <summary>
/// Tests that the active bookmark (the highlighted row in the bookmarks tree) tracks the
/// viewport. The viewport position is the (<see cref="DocumentViewModel.SelectedPageNumber"/>,
/// <c>ScrollOffset</c>) pair: page navigation (e.g. Page Up/Down) can bring another page into
/// view while the offset within the selected page stays the same, so both halves must trigger
/// the update.
/// </summary>
public class DocumentViewModelBookmarkSyncTests
{
    /// <summary>
    /// A not-yet-opened document service (<see cref="DocumentViewModel"/>'s constructor asserts
    /// 0 pages) that serves a fixed bookmark tree. Members not exercised by these tests throw.
    /// </summary>
    private sealed class BookmarkedPdfDocumentService : IPdfDocumentService
    {
        private readonly IReadOnlyList<PdfBookmarkNode> _bookmarks;

        public BookmarkedPdfDocumentService(IReadOnlyList<PdfBookmarkNode> bookmarks)
        {
            _bookmarks = bookmarks;
        }

        public int NumberOfPages => 0;
        public string? FileName => "bookmarked.pdf";
        // IsActive has an internal setter in the interface; implemented directly via InternalsVisibleTo.
        public bool IsActive { get; set; }

        public Task<IReadOnlyList<PdfBookmarkNode>?> GetPdfBookmark(CancellationToken token)
            => Task.FromResult<IReadOnlyList<PdfBookmarkNode>?>(_bookmarks);

        public double PpiScale => throw new NotImplementedException();
        public long? FileSize => throw new NotImplementedException();
        public string? LocalPath => throw new NotImplementedException();
        public bool IsPasswordProtected => throw new NotImplementedException();

        public Task<DocumentOpeningState> OpenDocument(IStorageFile? storageFile, string? password, CancellationToken token)
            => throw new NotImplementedException();

        public Task<DocumentPropertiesViewModel?> GetDocumentPropertiesAsync(CancellationToken token)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<PdfEmbeddedFileViewModel>?> GetEmbeddedFileAsync(CancellationToken token)
            => throw new NotImplementedException();

        public Task<UglyToad.PdfPig.Rendering.Skia.PdfPageSize?> GetPageSizeAsync(int pageNumber, CancellationToken token)
            => throw new NotImplementedException();

        public Task<PdfTextLayer?> GetPageTextLayerAsync(int pageNumber, CancellationToken token)
            => throw new NotImplementedException();

        public Task<IRef<SKPicture>?> GetRenderPageAsync(int pageNumber, CancellationToken token)
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

    private static DocumentViewModel NewDocumentWithBookmarks(IReadOnlyList<PdfBookmarkNode> bookmarks, int pageCount)
    {
        var pdfService = new BookmarkedPdfDocumentService(bookmarks);
        var pageService = new PdfPageService(pdfService);
        var doc = new DocumentViewModel(pdfService, pageService, new NoopTextSearchService(), new ApplicationStates())
        {
            PageCount = pageCount,
            TextSelection = new TextSelection(pageCount)
        };

        for (int p = 1; p <= pageCount; ++p)
        {
            doc.Pages.Add(new PageViewModel(p, doc.TextSelection!, pageService.TileRenderService, 1.0, doc.CopyTextCommand)
            {
                Size = new Size(500, 1000)
            });
        }

        return doc;
    }

    [AvaloniaFact]
    public async Task PageNavigation_WithUnchangedScrollOffset_UpdatesActiveBookmark()
    {
        var doc = NewDocumentWithBookmarks(
        [
            new PdfBookmarkNode("Chapter 1", 1, null, null),
            new PdfBookmarkNode("Chapter 2", 2, null, null)
        ], pageCount: 2);

        var source = await doc.BookmarksSource;
        Assert.NotNull(source);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Chapter 1", source!.RowSelection!.SelectedItem?.Title);

        // Page Down: the next page comes into view but the offset within the
        // selected page stays 0, so ScrollOffset never changes.
        doc.SelectedPageNumber = 2;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Chapter 2", source.RowSelection.SelectedItem?.Title);

        // And back up.
        doc.SelectedPageNumber = 1;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Chapter 1", source.RowSelection.SelectedItem?.Title);

        // Viewport-driven sync only highlights the row; SelectedBookmark stays untouched
        // because setting it would navigate (DocumentControl calls GoToPage on change).
        Assert.Null(doc.SelectedBookmark);
    }

    [AvaloniaFact]
    public async Task ClickedBookmark_AmongBookmarksWithoutLocation_IsNotStolenByViewportSync()
    {
        // Three bookmarks on page 1 with no location at all: they all resolve to the same
        // viewport target, so after click-navigation the sync must not steal the selection
        // back to the first of the tied bookmarks.
        var doc = NewDocumentWithBookmarks(
        [
            new PdfBookmarkNode("A", 1, null, null),
            new PdfBookmarkNode("B", 1, null, null),
            new PdfBookmarkNode("C", 1, null, null),
            new PdfBookmarkNode("Next", 2, null, null)
        ], pageCount: 2);

        var source = await doc.BookmarksSource;
        Assert.NotNull(source);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("A", source!.RowSelection!.SelectedItem?.Title);

        // User clicks the third bookmark in the tree.
        source.RowSelection.Select(new IndexPath(2));
        Assert.Equal("C", doc.SelectedBookmark?.Title);

        // The resulting navigation nudges the persisted scroll position, queuing a sync.
        doc.ScrollOffset = new Vector(0, 1);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("C", source.RowSelection.SelectedItem?.Title);

        // The tie must not trap the selection either: moving to another page still re-syncs.
        doc.SelectedPageNumber = 2;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Next", source.RowSelection.SelectedItem?.Title);
    }

    [AvaloniaFact]
    public async Task ClickedBookmark_AmongSameLocationBookmarks_IsNotStolenByViewportSync()
    {
        // Same as above, but the tied bookmarks share an explicit location (PDF coordinates,
        // bottom = 0): navigation scrolls the viewport exactly to that shared target.
        var doc = NewDocumentWithBookmarks(
        [
            new PdfBookmarkNode("A", 1, 500, null),
            new PdfBookmarkNode("B", 1, 500, null),
            new PdfBookmarkNode("C", 1, 500, null)
        ], pageCount: 1);

        var source = await doc.BookmarksSource;
        Assert.NotNull(source);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("A", source!.RowSelection!.SelectedItem?.Title);

        source.RowSelection.Select(new IndexPath(2));
        Assert.Equal("C", doc.SelectedBookmark?.Title);

        // Viewport lands on the shared target: 1000 (page height) - 500 (PDF offset) = 500.
        doc.ScrollOffset = new Vector(0, 500);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("C", source.RowSelection.SelectedItem?.Title);
    }

    [AvaloniaFact]
    public async Task ScrollWithinPage_UpdatesActiveBookmark()
    {
        // OffsetY is in PDF coordinates (bottom = 0): on a 1000-high upright page,
        // "Intro" sits near the top (viewport target 50) and "Details" near the
        // bottom (viewport target 900).
        var doc = NewDocumentWithBookmarks(
        [
            new PdfBookmarkNode("Intro", 1, 950, null),
            new PdfBookmarkNode("Details", 1, 100, null)
        ], pageCount: 1);

        var source = await doc.BookmarksSource;
        Assert.NotNull(source);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Intro", source!.RowSelection!.SelectedItem?.Title);

        doc.ScrollOffset = new Vector(0, 800);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Details", source.RowSelection.SelectedItem?.Title);
    }
}
