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
/// viewport, as given by <see cref="DocumentViewModel.ReadingPoint"/> (the viewport centre on the
/// unrotated page, computed by PageItemsControl). Until the view reports one, the sync falls back
/// to the top of <see cref="DocumentViewModel.SelectedPageNumber"/>.
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
        public Func<CancellationToken, Task<string?>>? PasswordPrompt { get; set; }

        public string? Title => throw new NotImplementedException();

        public PdfPreferences? Preferences => throw new NotImplementedException();

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
        var doc = new DocumentViewModel(pdfService, pageService, new NoopTextSearchService())
        {
            PageCount = pageCount,
            TextSelection = new TextSelection(pageCount)
        };

        // Mirrors LoadDocumentCore's own post-success assignment - SelectedPageNumber has no
        // field initialiser any more (it must stay null until PageCount is real), so this
        // helper has to set it explicitly to reproduce "a document the user has already opened".
        if (pageCount > 0)
        {
            doc.SelectedPageNumber = 1;
        }

        for (int p = 1; p <= pageCount; ++p)
        {
            doc.Pages.Add(new PageViewModel(p, doc.TextSelection!, pageService.TileRenderService, 1.0, doc.CopyTextCommand)
            {
                Size = new Size(500, 1000)
            });
        }

        return doc;
    }

    /// <summary>
    /// A single root bookmark holding <paramref name="count"/> - 1 children, all pointing to
    /// page 1.
    /// </summary>
    private static IReadOnlyList<PdfBookmarkNode> BookmarksWithCount(int count)
    {
        var children = new List<PdfBookmarkNode>(count - 1);
        for (int i = 1; i < count; ++i)
        {
            children.Add(new PdfBookmarkNode($"Child {i}", 1, null, null));
        }

        return [new PdfBookmarkNode("Root", 1, null, children)];
    }

    [AvaloniaFact]
    public async Task PageNavigation_UpdatesActiveBookmark()
    {
        var doc = NewDocumentWithBookmarks(
        [
            new PdfBookmarkNode("Chapter 1", 1, null, null),
            new PdfBookmarkNode("Chapter 2", 2, null, null)
        ], pageCount: 2);

        var source = await doc.BookmarksSource;
        Assert.NotNull(source);
        Dispatcher.UIThread.RunJobs();
        // No reading point yet: the selected page's top.
        Assert.Equal("Chapter 1", source!.RowSelection!.SelectedItem?.Title);

        // Page Down: the reading point moves to the same spot on the next page.
        doc.ReadingPoint = new PageReadingPoint(2, new Point(250, 0));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Chapter 2", source.RowSelection.SelectedItem?.Title);

        // And back up.
        doc.ReadingPoint = new PageReadingPoint(1, new Point(250, 0));
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

        // The resulting navigation nudges the reading point, queuing a sync.
        doc.ReadingPoint = new PageReadingPoint(1, new Point(250, 1));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("C", source.RowSelection.SelectedItem?.Title);

        // The tie must not trap the selection either: moving to another page still re-syncs.
        doc.ReadingPoint = new PageReadingPoint(2, new Point(250, 1));
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

        // The viewport centre lands on the shared target: 1000 (page height) - 500 (PDF offset) = 500.
        doc.ReadingPoint = new PageReadingPoint(1, new Point(250, 500));
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

        doc.ReadingPoint = new PageReadingPoint(1, new Point(250, 800));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Details", source.RowSelection.SelectedItem?.Title);
    }

    [AvaloniaFact]
    public async Task ActiveBookmark_IsTheClosestToTheReadingPoint()
    {
        // Viewport targets on the 1000-high page: Intro 50, Middle 400, Details 900.
        var doc = NewDocumentWithBookmarks(
        [
            new PdfBookmarkNode("Intro", 1, 950, null),
            new PdfBookmarkNode("Middle", 1, 600, null),
            new PdfBookmarkNode("Details", 1, 100, null)
        ], pageCount: 1);

        var source = await doc.BookmarksSource;
        Assert.NotNull(source);
        Dispatcher.UIThread.RunJobs();

        // Viewport 0..800: its top is closest to Intro, its centre (400) is on Middle.
        doc.ReadingPoint = new PageReadingPoint(1, new Point(250, 400));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Middle", source!.RowSelection!.SelectedItem?.Title);
    }

    [AvaloniaFact]
    public async Task ClickedBookmark_WhoseTargetCannotBeCentred_IsNotStolenByViewportSync()
    {
        // Intro sits at the very top of the document: navigating to it cannot centre it, the
        // viewport stays at the top, and its centre is on Middle.
        var doc = NewDocumentWithBookmarks(
        [
            new PdfBookmarkNode("Intro", 1, 950, null),
            new PdfBookmarkNode("Middle", 1, 600, null),
            new PdfBookmarkNode("Details", 1, 100, null)
        ], pageCount: 1);
        var page = doc.Pages[0];

        var source = await doc.BookmarksSource;
        Assert.NotNull(source);
        Dispatcher.UIThread.RunJobs();

        source!.RowSelection!.Select(new IndexPath(2));
        source.RowSelection.Select(new IndexPath(0));
        Assert.Equal("Intro", doc.SelectedBookmark?.Title);

        page.VisibleArea = new Rect(0, 0, 500, 800);
        doc.ReadingPoint = new PageReadingPoint(1, new Point(250, 400));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Intro", source.RowSelection.SelectedItem?.Title);

        // Once the user scrolls the clicked target off screen, the sync resumes.
        page.VisibleArea = new Rect(0, 500, 500, 500);
        doc.ReadingPoint = new PageReadingPoint(1, new Point(250, 750));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Details", source.RowSelection.SelectedItem?.Title);
    }

    // Page 500 x 1000 unrotated, so 1000 x 500 on screen when sideways. VisibleArea is in unrotated
    // page coordinates (top = 0, increasing downward): "Intro" targets v = 50, "Details" v = 900.
    // At 90 the unrotated top edge is on the right (v = 1000 - screen x); at 270 on the left (v = screen x).
    // The reading point is the centre of the visible part, as the viewport is no wider than the page.
    private static void SetVisibleV(DocumentViewModel doc, double top, double bottom)
    {
        doc.Pages[0].VisibleArea = new Rect(0, top, 500, bottom - top);
        doc.ReadingPoint = new PageReadingPoint(1, new Point(250, (top + bottom) / 2));
    }

    [AvaloniaTheory]
    [InlineData(90, 700, 1000, "Details", 0, 300, "Intro")]  // screen x 0..300, then 700..1000
    [InlineData(270, 850, 1000, "Details", 0, 300, "Intro")] // screen x 850..1000, then 0..300
    public async Task ScrollAcrossSidewaysPage_UpdatesActiveBookmark(int rotation,
        double firstTop, double firstBottom, string firstExpected,
        double secondTop, double secondBottom, string secondExpected)
    {
        var doc = NewDocumentWithBookmarks(
        [
            new PdfBookmarkNode("Intro", 1, 950, null),
            new PdfBookmarkNode("Details", 1, 100, null)
        ], pageCount: 1);
        var page = doc.Pages[0];
        page.Rotation = rotation;

        var source = await doc.BookmarksSource;
        Assert.NotNull(source);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Intro", source!.RowSelection!.SelectedItem?.Title);

        SetVisibleV(doc, firstTop, firstBottom);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(firstExpected, source.RowSelection.SelectedItem?.Title);

        SetVisibleV(doc, secondTop, secondBottom);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(secondExpected, source.RowSelection.SelectedItem?.Title);
    }

    [AvaloniaTheory]
    [InlineData(90)]
    [InlineData(270)]
    public async Task ClickedBookmark_OnSidewaysPageFittingTheWidth_IsNotStolenByViewportSync(int rotation)
    {
        // The whole page width is on screen, so navigating to "Details" cannot scroll its target
        // to the viewport edge: the sync must keep the clicked bookmark.
        var doc = NewDocumentWithBookmarks(
        [
            new PdfBookmarkNode("Intro", 1, 950, null),
            new PdfBookmarkNode("Details", 1, 100, null)
        ], pageCount: 1);
        var page = doc.Pages[0];
        page.Rotation = rotation;
        SetVisibleV(doc, 0, 1000);

        var source = await doc.BookmarksSource;
        Assert.NotNull(source);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Intro", source!.RowSelection!.SelectedItem?.Title);

        source.RowSelection.Select(new IndexPath(1));
        Assert.Equal("Details", doc.SelectedBookmark?.Title);

        doc.ReadingPoint = new PageReadingPoint(1, new Point(250, 501));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Details", source.RowSelection.SelectedItem?.Title);
    }

    [AvaloniaFact]
    public async Task Bookmarks_BelowExpandThreshold_AreExpanded()
    {
        const int count = 499; // comfortably below the threshold: the ordinary outline case
        var doc = NewDocumentWithBookmarks(BookmarksWithCount(count), pageCount: 1);

        var source = await doc.BookmarksSource;
        Assert.NotNull(source);

        Assert.Equal(count, source!.Rows.Count);
    }

    [AvaloniaFact]
    public async Task Bookmarks_AtExpandThreshold_AreExpanded()
    {
        // Exactly at the threshold. The check is inclusive, so this still expands.
        const int count = DocumentViewModel.MaxAutoExpandBookmarkCount;
        var doc = NewDocumentWithBookmarks(BookmarksWithCount(count), pageCount: 1);

        var source = await doc.BookmarksSource;
        Assert.NotNull(source);

        Assert.Equal(count, source!.Rows.Count);
    }

    [AvaloniaFact]
    public async Task Bookmarks_AboveExpandThreshold_AreNotExpanded()
    {
        // One past the threshold: only the root is realised, the outline stays collapsed.
        const int count = DocumentViewModel.MaxAutoExpandBookmarkCount + 1;
        var doc = NewDocumentWithBookmarks(BookmarksWithCount(count), pageCount: 1);

        var source = await doc.BookmarksSource;
        Assert.NotNull(source);

        Assert.Single(source!.Rows);
    }
}
