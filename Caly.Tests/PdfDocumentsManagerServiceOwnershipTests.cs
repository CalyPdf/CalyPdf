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
/// Ownership behaviour of <see cref="PdfDocumentsManagerService"/> once documents can live
/// in more than one window. Before detach support the service captured a single
/// <see cref="MainViewModel"/>, so closing a detached tab would silently no-op while its DI
/// scope was disposed, and activating a document in one window would blank the other.
/// </summary>
public class PdfDocumentsManagerServiceOwnershipTests
{
    /// <summary>
    /// A never-opened document service. <see cref="DocumentViewModel"/>'s constructor asserts
    /// the document has no pages yet. Members not needed before a document is opened throw.
    /// </summary>
    private sealed class UnopenedPdfDocumentService : IPdfDocumentService
    {
        public int NumberOfPages => 0;
        public string? FileName => "unopened.pdf";
        public bool IsActive { get; set; }

        public double PpiScale => throw new NotImplementedException();
        public long? FileSize => throw new NotImplementedException();
        public string? LocalPath => throw new NotImplementedException();
        public bool IsPasswordProtected => throw new NotImplementedException();

        public Task<DocumentOpeningState> OpenDocument(IStorageFile? storageFile, string? password, CancellationToken token)
            => throw new NotImplementedException();

        public Task<DocumentPropertiesViewModel?> GetDocumentPropertiesAsync(CancellationToken token)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<PdfBookmarkNode>?> GetPdfBookmark(CancellationToken token)
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

    private sealed class StubFilesService : IFilesService
    {
        public int PickerShownCount { get; private set; }

        public Window? LastPickerOwner { get; private set; }

        public Task<IStorageFile?> OpenPdfFileAsync(Window? owner = null)
        {
            PickerShownCount++;
            LastPickerOwner = owner;
            return Task.FromResult<IStorageFile?>(null);
        }

        public Task<IStorageFile?> SaveFileAsync(ReadOnlyMemory<byte> data, string? fileName = null)
            => Task.FromResult<IStorageFile?>(null);

        public Task<IStorageFile?> SaveTempFileAsync(ReadOnlyMemory<byte> data, string? fileName = null)
            => Task.FromResult<IStorageFile?>(null);

        public Task<IStorageFile?> TryGetFileFromPathAsync(string path) => Task.FromResult<IStorageFile?>(null);
    }

    private sealed class StubDialogService : IDialogService
    {
        public Task<string?> ShowPdfPasswordDialogAsync() => Task.FromResult<string?>(null);

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

    private static DocumentViewModel NewDocument()
    {
        var pdfService = new UnopenedPdfDocumentService();
        return new DocumentViewModel(pdfService, new PdfPageService(pdfService), new NoopTextSearchService());
    }

    private static MainViewModel NewMainViewModel()
    {
        var vm = new MainViewModel();
        vm.Dispose();
        return vm;
    }

    /// <summary>
    /// Constructed on the UI thread: the service's constructor calls
    /// <c>Debug.ThrowNotOnUiThread()</c>.
    /// </summary>
    private static PdfDocumentsManagerService NewService(ICalyWindowRegistry registry) =>
        NewService(registry, new StubFilesService());

    private static PdfDocumentsManagerService NewService(ICalyWindowRegistry registry, StubFilesService files) =>
        new(registry, files, new StubDialogService(), new StubClipboardService());

    /// <summary>
    /// An empty DI scope: the records under test are only ever disposed, never resolved from.
    /// </summary>
    private static AsyncServiceScope NewScope() =>
        new ServiceCollection().BuildServiceProvider().CreateAsyncScope();

    [AvaloniaFact]
    public void RemoveDocumentFromOwnerWindow_RemovesFromTheOwningWindowOnly()
    {
        var registry = new CalyWindowRegistry();
        var primary = NewMainViewModel();
        var secondary = NewMainViewModel();
        registry.Register(new CalyWindowContext { ViewModel = primary, Window = null, IsPrimary = true });
        registry.Register(new CalyWindowContext { ViewModel = secondary, Window = null, IsPrimary = false });

        using var service = NewService(registry);

        var stayingDocument = NewDocument();
        var closingDocument = NewDocument();
        primary.PdfDocuments.Add(stayingDocument);
        secondary.PdfDocuments.Add(closingDocument);
        Dispatcher.UIThread.RunJobs();

        service.RemoveDocumentFromOwnerWindow(closingDocument);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(secondary.PdfDocuments);
        Assert.Single(primary.PdfDocuments);
        Assert.Same(stayingDocument, primary.PdfDocuments[0]);
    }

    [AvaloniaFact]
    public void RemoveDocumentFromOwnerWindow_MovesTheOwningWindowsSelectionToANeighbour()
    {
        var registry = new CalyWindowRegistry();
        var primary = NewMainViewModel();
        registry.Register(new CalyWindowContext { ViewModel = primary, Window = null, IsPrimary = true });

        using var service = NewService(registry);

        var first = NewDocument();
        var second = NewDocument();
        primary.PdfDocuments.Add(first);
        primary.PdfDocuments.Add(second);
        primary.SelectedDocumentIndex = 1;
        Dispatcher.UIThread.RunJobs();

        service.RemoveDocumentFromOwnerWindow(second);
        Dispatcher.UIThread.RunJobs();

        Assert.Single(primary.PdfDocuments);
        Assert.Equal(0, primary.SelectedDocumentIndex);
        Assert.Same(first, primary.SelectedDocument);
    }

    [AvaloniaFact]
    public void RemoveDocumentFromOwnerWindow_IsANoOpWhenNoWindowOwnsTheDocument()
    {
        var registry = new CalyWindowRegistry();
        var primary = NewMainViewModel();
        registry.Register(new CalyWindowContext { ViewModel = primary, Window = null, IsPrimary = true });

        using var service = NewService(registry);

        var owned = NewDocument();
        primary.PdfDocuments.Add(owned);
        Dispatcher.UIThread.RunJobs();

        // Must not throw: the document was already removed from every window.
        service.RemoveDocumentFromOwnerWindow(NewDocument());
        Dispatcher.UIThread.RunJobs();

        Assert.Single(primary.PdfDocuments);
    }

    /// <summary>
    /// Two windows each keep their own selected document live. The old single-window rule
    /// deactivated every document but one app-wide, which would blank the other window's
    /// page view as soon as the user touched a tab.
    /// </summary>
    [AvaloniaFact]
    public void ShouldBeActive_IsTrueForTheSelectedDocumentOfEveryWindow()
    {
        var registry = new CalyWindowRegistry();
        var primary = NewMainViewModel();
        var secondary = NewMainViewModel();
        registry.Register(new CalyWindowContext { ViewModel = primary, Window = null, IsPrimary = true });
        registry.Register(new CalyWindowContext { ViewModel = secondary, Window = null, IsPrimary = false });

        using var service = NewService(registry);

        var inPrimary = NewDocument();
        var inSecondaryVisible = NewDocument();
        var inSecondaryHidden = NewDocument();

        primary.PdfDocuments.Add(inPrimary);
        secondary.PdfDocuments.Add(inSecondaryVisible);
        secondary.PdfDocuments.Add(inSecondaryHidden);
        secondary.SelectedDocumentIndex = 0;
        Dispatcher.UIThread.RunJobs();

        Assert.True(service.ShouldBeActive(inPrimary));
        Assert.True(service.ShouldBeActive(inSecondaryVisible));
        Assert.False(service.ShouldBeActive(inSecondaryHidden));
    }

    [AvaloniaFact]
    public void ShouldBeActive_IsFalseForADocumentNoWindowOwns()
    {
        var registry = new CalyWindowRegistry();
        var primary = NewMainViewModel();
        registry.Register(new CalyWindowContext { ViewModel = primary, Window = null, IsPrimary = true });

        using var service = NewService(registry);

        Assert.False(service.ShouldBeActive(NewDocument()));
    }

    /// <summary>
    /// Removing a document must not close the window by itself. Caly's close path unloads the
    /// document from the opened-files map first and only then asks the registry to close an
    /// empty window - if closing happened here, a failure or re-entrancy during Window.Close
    /// would leave a stale entry that makes the file impossible to reopen.
    /// </summary>
    [AvaloniaFact]
    public void RemoveDocumentFromOwnerWindow_ReturnsTheOwnerWithoutClosingItsWindow()
    {
        var registry = new CalyWindowRegistry();
        var primary = NewMainViewModel();
        var detached = NewMainViewModel();
        var detachedWindow = new Window();
        registry.Register(new CalyWindowContext { ViewModel = primary, Window = null, IsPrimary = true });
        registry.Register(new CalyWindowContext { ViewModel = detached, Window = detachedWindow, IsPrimary = false });

        using var service = NewService(registry);

        var document = NewDocument();
        detached.PdfDocuments.Add(document);
        Dispatcher.UIThread.RunJobs();

        bool closed = false;
        detachedWindow.Closed += (_, _) => closed = true;

        MainViewModel? owner = service.RemoveDocumentFromOwnerWindow(document);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(detached, owner);
        Assert.Empty(detached.PdfDocuments);
        Assert.False(closed);

        // The caller closes, once the unload bookkeeping has completed.
        registry.CloseWindowIfEmpty(owner!);
        Assert.True(closed);
    }

    /// <summary>
    /// A tab that is still opening can be dragged into another window while its document
    /// parses - it is already in the strip, reading "Opening '...'". If that open then fails,
    /// the cleanup has to remove the document from the window that holds it *now*.
    /// <para>
    /// The failed-open path used to remove from the <see cref="MainViewModel"/> captured when
    /// the open started, which is a no-op once the tab has moved. The record was dropped and
    /// the DI scope disposed regardless, leaving a live tab in the new window bound to a
    /// disposed <c>IPdfDocumentService</c>.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public async Task RevertFailedOpen_FollowsADocumentDraggedWhileItWasStillOpening()
    {
        var registry = new CalyWindowRegistry();
        var openedInto = NewMainViewModel();
        var draggedInto = NewMainViewModel();
        registry.Register(new CalyWindowContext { ViewModel = openedInto, Window = null, IsPrimary = true });
        registry.Register(new CalyWindowContext { ViewModel = draggedInto, Window = null, IsPrimary = false });

        using var service = NewService(registry);

        // The open registers the file and adds the pending tab to the window it was headed for.
        const string key = @"C:\pdf\unreadable.pdf";
        var document = NewDocument();
        Assert.True(service.TryAddRecord(key, document, NewScope()));
        openedInto.PdfDocuments.Add(document);
        Dispatcher.UIThread.RunJobs();

        // The user drags that still-opening tab into the other window. Tabalonia moves models
        // between collections with a bare Remove + Add.
        openedInto.PdfDocuments.Remove(document);
        draggedInto.PdfDocuments.Add(document);
        Dispatcher.UIThread.RunJobs();

        // The open now fails and unwinds.
        MainViewModel? owner = await service.RevertFailedOpen(document, key);
        Dispatcher.UIThread.RunJobs();

        // Removed from the window that actually held it, not the one it was opened into.
        Assert.Same(draggedInto, owner);
        Assert.Empty(draggedInto.PdfDocuments);
        Assert.Empty(openedInto.PdfDocuments);

        // ...and the record is gone, so the file can be opened again.
        Assert.False(service.TryRemoveRecord(key, document));
    }

    /// <summary>
    /// The straightforward case, so the test above is not the only thing describing the
    /// contract: a document that never moved is removed from the window it was opened into.
    /// </summary>
    [AvaloniaFact]
    public async Task RevertFailedOpen_RemovesTheDocumentAndItsRecord()
    {
        var registry = new CalyWindowRegistry();
        var primary = NewMainViewModel();
        registry.Register(new CalyWindowContext { ViewModel = primary, Window = null, IsPrimary = true });

        using var service = NewService(registry);

        const string key = @"C:\pdf\unreadable.pdf";
        var document = NewDocument();
        Assert.True(service.TryAddRecord(key, document, NewScope()));
        primary.PdfDocuments.Add(document);
        Dispatcher.UIThread.RunJobs();

        MainViewModel? owner = await service.RevertFailedOpen(document, key);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(primary, owner);
        Assert.Empty(primary.PdfDocuments);
        Assert.False(service.TryRemoveRecord(key, document));
    }

    /// <summary>
    /// A failed open must not close the window it emptied. Closing is intent-driven -
    /// Tabalonia's <c>LastTabClosedAction</c>, or Caly's own close-tab path - and dropping an
    /// unreadable file on an empty window carries no such intent; the window has to survive to
    /// show the error.
    /// </summary>
    [AvaloniaFact]
    public async Task RevertFailedOpen_LeavesTheWindowOpenToReportTheFailureInto()
    {
        var registry = new CalyWindowRegistry();
        var primary = NewMainViewModel();
        var detached = NewMainViewModel();
        var detachedWindow = new Window();
        registry.Register(new CalyWindowContext { ViewModel = primary, Window = null, IsPrimary = true });
        registry.Register(new CalyWindowContext { ViewModel = detached, Window = detachedWindow, IsPrimary = false });

        using var service = NewService(registry);

        // The only document in the detached window is a file that will not open.
        const string key = @"C:\pdf\unreadable.pdf";
        var document = NewDocument();
        Assert.True(service.TryAddRecord(key, document, NewScope()));
        detached.PdfDocuments.Add(document);
        Dispatcher.UIThread.RunJobs();

        bool closed = false;
        detachedWindow.Closed += (_, _) => closed = true;

        MainViewModel? owner = await service.RevertFailedOpen(document, key);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(detached, owner);
        Assert.Empty(detached.PdfDocuments);

        // The window survives, even emptied, so the error notification has somewhere to land.
        Assert.False(closed);
    }

    /// <summary>
    /// Regression for M12: Ctrl+O in a detached window opened the document in the main window.
    /// The file picker is owned by a window, so showing it activates that window and moves
    /// "the active window" out from under the request. The target is therefore captured when
    /// the user acts and honoured later, whatever became active in the meantime.
    /// </summary>
    [AvaloniaFact]
    public void ResolveOpenTarget_HonoursTheCapturedWindowEvenWhenAnotherBecameActive()
    {
        var registry = new CalyWindowRegistry();
        var primary = NewMainViewModel();
        var detached = NewMainViewModel();
        var primaryContext = new CalyWindowContext { ViewModel = primary, Window = null, IsPrimary = true };
        var detachedContext = new CalyWindowContext { ViewModel = detached, Window = null, IsPrimary = false };
        registry.Register(primaryContext);
        registry.Register(detachedContext);

        using var service = NewService(registry);

        // The user pressed Ctrl+O in the detached window...
        MainViewModel captured = detached;

        // ...and the file picker then activated the main window.
        registry.SetActive(primaryContext);

        Assert.Same(detached, service.ResolveOpenTarget(captured));
    }

    [AvaloniaFact]
    public void ResolveOpenTarget_FallsBackToActiveWhenTheCapturedWindowHasClosed()
    {
        var registry = new CalyWindowRegistry();
        var primary = NewMainViewModel();
        var detached = NewMainViewModel();
        var primaryContext = new CalyWindowContext { ViewModel = primary, Window = null, IsPrimary = true };
        var detachedContext = new CalyWindowContext { ViewModel = detached, Window = null, IsPrimary = false };
        registry.Register(primaryContext);
        registry.Register(detachedContext);

        using var service = NewService(registry);

        registry.Unregister(detachedContext);

        Assert.Same(primary, service.ResolveOpenTarget(detached));
    }

    [AvaloniaFact]
    public void ResolveOpenTarget_FallsBackToActiveWhenNothingWasCaptured()
    {
        var registry = new CalyWindowRegistry();
        var primary = NewMainViewModel();
        var detached = NewMainViewModel();
        registry.Register(new CalyWindowContext { ViewModel = primary, Window = null, IsPrimary = true });
        var detachedContext = new CalyWindowContext { ViewModel = detached, Window = null, IsPrimary = false };
        registry.Register(detachedContext);
        registry.SetActive(detachedContext);

        using var service = NewService(registry);

        Assert.Same(detached, service.ResolveOpenTarget(null));
    }

    /// <summary>
    /// Every window closed while the open sat in the queue. Resolving must yield null so the
    /// open aborts cleanly - asking the registry for the active window would throw, which
    /// would surface as an error dialog while the app is shutting down.
    /// </summary>
    [AvaloniaFact]
    public void ResolveOpenTarget_ReturnsNullWhenNoWindowsRemain()
    {
        var registry = new CalyWindowRegistry();
        var only = NewMainViewModel();
        var context = new CalyWindowContext { ViewModel = only, Window = null, IsPrimary = true };
        registry.Register(context);

        using var service = NewService(registry);

        registry.Unregister(context);

        Assert.Empty(registry.Windows);
        Assert.Null(service.ResolveOpenTarget(only));
        Assert.Null(service.ResolveOpenTarget(null));
    }

    /// <summary>
    /// A window that closes hands its documents over to be unloaded on a background task, and
    /// those unloads are serialised behind each other's teardown - which can take seconds for a
    /// window with several tabs. Reopening one of those files in the meantime replaces the
    /// record under the same path key. The pending unload must then leave that new record
    /// alone: removing by path alone disposed the freshly opened document's DI scope and
    /// dropped it from the opened-files map, leaving a live tab backed by a disposed service.
    /// </summary>
    [AvaloniaFact]
    public void TryRemoveRecord_LeavesARecordThatHasSinceBeenReplaced()
    {
        var registry = new CalyWindowRegistry();
        registry.Register(new CalyWindowContext { ViewModel = NewMainViewModel(), Window = null, IsPrimary = true });

        using var service = NewService(registry);

        const string path = @"C:\docs\report.pdf";

        DocumentViewModel closed = NewDocument();
        Assert.True(service.TryAddRecord(path, closed, NewScope()));

        // The stale record is dropped when the reopen finds no window owning it...
        Assert.True(service.TryRemoveRecord(path, closed));

        // ...and the same file is opened fresh under the same key.
        DocumentViewModel reopened = NewDocument();
        Assert.True(service.TryAddRecord(path, reopened, NewScope()));

        // Only now does the orphan unload get round to the closed document.
        Assert.False(service.TryRemoveRecord(path, closed));

        // The reopened document is still registered.
        Assert.True(service.TryRemoveRecord(path, reopened));
    }

    /// <summary>
    /// The window that asked for the picker has closed before the command ran. There is nowhere
    /// to put the document and no window to show the picker over, so the open aborts quietly -
    /// it must not fall back to another window, and must not throw on the empty registry.
    /// </summary>
    [AvaloniaFact]
    public async Task OpenLoadDocument_DoesNotShowThePickerWhenNoWindowRemains()
    {
        var registry = new CalyWindowRegistry();
        MainViewModel only = NewMainViewModel();
        var context = new CalyWindowContext { ViewModel = only, Window = null, IsPrimary = true };
        registry.Register(context);

        var files = new StubFilesService();
        using var service = NewService(registry, files);

        registry.Unregister(context);

        await service.OpenLoadDocument(only, CancellationToken.None);

        Assert.Equal(0, files.PickerShownCount);
    }

    /// <summary>
    /// Regression for the "+" button and Ctrl+O: the picker is shown over the window whose
    /// command ran, not over whichever window is active. The two only ever agreed because
    /// clicking a window activates it first - a coincidence, not a guarantee.
    /// </summary>
    [AvaloniaFact]
    public async Task OpenLoadDocument_ShowsThePickerOverTheAskingWindow()
    {
        var registry = new CalyWindowRegistry();
        MainViewModel primary = NewMainViewModel();
        MainViewModel detached = NewMainViewModel();

        var primaryWindow = new Window();
        var detachedWindow = new Window();

        var primaryContext = new CalyWindowContext { ViewModel = primary, Window = primaryWindow, IsPrimary = true };
        registry.Register(primaryContext);
        registry.Register(new CalyWindowContext { ViewModel = detached, Window = detachedWindow, IsPrimary = false });

        var files = new StubFilesService();
        using var service = NewService(registry, files);

        // The primary window is active, but the detached one is asking.
        registry.SetActive(primaryContext);

        await service.OpenLoadDocument(detached, CancellationToken.None);

        Assert.Equal(1, files.PickerShownCount);
        Assert.Same(detachedWindow, files.LastPickerOwner);
    }

    /// <summary>
    /// The ordinary case still removes: same document, same key.
    /// </summary>
    [AvaloniaFact]
    public void TryRemoveRecord_RemovesTheRecordItWasGiven()
    {
        var registry = new CalyWindowRegistry();
        registry.Register(new CalyWindowContext { ViewModel = NewMainViewModel(), Window = null, IsPrimary = true });

        using var service = NewService(registry);

        const string path = @"C:\docs\report.pdf";

        DocumentViewModel document = NewDocument();
        Assert.True(service.TryAddRecord(path, document, NewScope()));

        Assert.True(service.TryRemoveRecord(path, document));

        // Gone: a second unload of the same document is a no-op.
        Assert.False(service.TryRemoveRecord(path, document));
    }

    /// <summary>
    /// A document that was never registered - or was registered under another path - is not
    /// removed by a key that happens to hold someone else's record.
    /// </summary>
    [AvaloniaFact]
    public void TryRemoveRecord_IgnoresADocumentThatDoesNotHoldTheKey()
    {
        var registry = new CalyWindowRegistry();
        registry.Register(new CalyWindowContext { ViewModel = NewMainViewModel(), Window = null, IsPrimary = true });

        using var service = NewService(registry);

        const string path = @"C:\docs\report.pdf";

        DocumentViewModel owner = NewDocument();
        Assert.True(service.TryAddRecord(path, owner, NewScope()));

        Assert.False(service.TryRemoveRecord(path, NewDocument()));
        Assert.False(service.TryRemoveRecord(@"C:\docs\other.pdf", owner));

        Assert.True(service.TryRemoveRecord(path, owner));
    }

    /// <summary>
    /// Two requests for the same file can be in flight at once: the open queue drains through
    /// <c>Parallel.ForEachAsync</c>. The second request finds the first one's record before the
    /// first has put its document into a window, so no window owns it yet. That must read as
    /// "still opening", not as "the record outlived its window" - treating it as stale disposed
    /// the first request's DI scope in the middle of its parse.
    /// </summary>
    [AvaloniaFact]
    public void ShowExistingDocument_ReportsOpeningWhileAnotherRequestIsStillOpeningTheFile()
    {
        var registry = new CalyWindowRegistry();
        registry.Register(new CalyWindowContext { ViewModel = NewMainViewModel(), Window = null, IsPrimary = true });

        using var service = NewService(registry);

        // The first request has claimed the path but has not reached a window yet.
        DocumentViewModel opening = NewDocument();
        Assert.True(service.TryAddRecord(@"C:\docs\report.pdf", opening, NewScope()));

        Assert.Equal(PdfDocumentsManagerService.OpenedFileState.Opening, service.ShowExistingDocument(opening));
    }

    /// <summary>
    /// Once the document has reached a window, belonging to no window means that window closed.
    /// The record is then stale and the caller reopens the file fresh (M10 / M11).
    /// </summary>
    [AvaloniaFact]
    public void ShowExistingDocument_ReportsStaleOnceTheDocumentsWindowHasClosed()
    {
        var registry = new CalyWindowRegistry();
        MainViewModel window = NewMainViewModel();
        var context = new CalyWindowContext { ViewModel = window, Window = null, IsPrimary = true };
        registry.Register(context);

        using var service = NewService(registry);

        DocumentViewModel document = NewDocument();
        Assert.True(service.TryAddRecord(@"C:\docs\report.pdf", document, NewScope()));

        window.PdfDocuments.Add(document);
        service.MarkShownInAWindow(document);

        Assert.Equal(PdfDocumentsManagerService.OpenedFileState.Shown, service.ShowExistingDocument(document));

        // The window closes, taking its documents with it.
        registry.Unregister(context);

        Assert.Equal(PdfDocumentsManagerService.OpenedFileState.Stale, service.ShowExistingDocument(document));
    }

    /// <summary>
    /// Regression for M14: reopening a file that is already open selects its tab in the window
    /// that holds it, rather than opening a duplicate in the active one.
    /// </summary>
    [AvaloniaFact]
    public void ShowExistingDocument_SelectsTheDocumentInTheWindowThatOwnsIt()
    {
        var registry = new CalyWindowRegistry();
        MainViewModel primary = NewMainViewModel();
        MainViewModel detached = NewMainViewModel();
        var primaryContext = new CalyWindowContext { ViewModel = primary, Window = null, IsPrimary = true };
        registry.Register(primaryContext);
        registry.Register(new CalyWindowContext { ViewModel = detached, Window = null, IsPrimary = false });

        using var service = NewService(registry);

        DocumentViewModel first = NewDocument();
        DocumentViewModel second = NewDocument();
        detached.PdfDocuments.Add(first);
        detached.PdfDocuments.Add(second);
        detached.SelectedDocumentIndex = 1;

        Assert.True(service.TryAddRecord(@"C:\docs\report.pdf", first, NewScope()));
        service.MarkShownInAWindow(first);

        // The user asks for it from the primary window...
        registry.SetActive(primaryContext);

        Assert.Equal(PdfDocumentsManagerService.OpenedFileState.Shown, service.ShowExistingDocument(first));

        // ...and it is selected where it actually lives, leaving the primary window alone.
        Assert.Equal(0, detached.SelectedDocumentIndex);
        Assert.Empty(primary.PdfDocuments);
    }
}
