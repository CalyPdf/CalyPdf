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
/// Ownership resolution for <see cref="CalyWindowRegistry"/>. Ownership is derived by
/// scanning live windows rather than cached, because Tabalonia moves document models
/// between collections with bare Remove/Add (and uses Remove + Add for plain in-strip
/// reordering), so any cached owner would desync.
/// </summary>
public class CalyWindowRegistryTests
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
        public Func<CancellationToken, Task<string?>>? PasswordPrompt { get; set; }

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

    private static DocumentViewModel NewDocument()
    {
        var pdfService = new UnopenedPdfDocumentService();
        return new DocumentViewModel(pdfService, new PdfPageService(pdfService), new NoopTextSearchService());
    }

    /// <summary>
    /// Creates a <see cref="MainViewModel"/> with its background document-load pipeline
    /// detached (it expects documents added through the manager service, i.e. with a pending
    /// open, and logs failures to disk otherwise).
    /// </summary>
    private static MainViewModel NewMainViewModel()
    {
        var vm = new MainViewModel();
        vm.Dispose();
        return vm;
    }

    private static CalyWindowContext NewContext(MainViewModel vm, bool isPrimary, Window? window = null) =>
        new() { ViewModel = vm, Window = window, IsPrimary = isPrimary };

    [AvaloniaFact]
    public void FindOwnerOf_ReturnsTheWindowHoldingTheDocument()
    {
        var registry = new CalyWindowRegistry();
        var primary = NewMainViewModel();
        var secondary = NewMainViewModel();
        var primaryContext = NewContext(primary, isPrimary: true);
        var secondaryContext = NewContext(secondary, isPrimary: false);
        registry.Register(primaryContext);
        registry.Register(secondaryContext);

        var document = NewDocument();
        secondary.PdfDocuments.Add(document);

        Assert.Same(secondaryContext, registry.FindOwnerOf(document));
    }

    [AvaloniaFact]
    public void FindOwnerOf_FollowsTheDocumentWhenItMovesBetweenWindows()
    {
        var registry = new CalyWindowRegistry();
        var primary = NewMainViewModel();
        var secondary = NewMainViewModel();
        var primaryContext = NewContext(primary, isPrimary: true);
        var secondaryContext = NewContext(secondary, isPrimary: false);
        registry.Register(primaryContext);
        registry.Register(secondaryContext);

        var document = NewDocument();
        primary.PdfDocuments.Add(document);
        Assert.Same(primaryContext, registry.FindOwnerOf(document));

        // How Tabalonia transfers a tab: bare Remove on the source, bare Add on the target.
        primary.PdfDocuments.Remove(document);
        secondary.PdfDocuments.Add(document);

        Assert.Same(secondaryContext, registry.FindOwnerOf(document));
    }

    [AvaloniaFact]
    public void FindOwnerOf_ReturnsNullWhenNoWindowHoldsTheDocument()
    {
        var registry = new CalyWindowRegistry();
        registry.Register(NewContext(NewMainViewModel(), isPrimary: true));

        Assert.Null(registry.FindOwnerOf(NewDocument()));
        Assert.Null(registry.FindOwnerOf(null));
    }

    [AvaloniaFact]
    public void Active_FallsBackToPrimaryAndIgnoresUnregisteredWindows()
    {
        var registry = new CalyWindowRegistry();
        var primaryContext = NewContext(NewMainViewModel(), isPrimary: true);
        var secondaryContext = NewContext(NewMainViewModel(), isPrimary: false);
        registry.Register(primaryContext);
        registry.Register(secondaryContext);

        Assert.Same(primaryContext, registry.Primary);
        Assert.Same(primaryContext, registry.Active);

        registry.SetActive(secondaryContext);
        Assert.Same(secondaryContext, registry.Active);

        registry.Unregister(secondaryContext);
        Assert.Same(primaryContext, registry.Active);
        Assert.Single(registry.Windows);
    }

    [AvaloniaFact]
    public void FindContext_ResolvesTheContextForAViewModel()
    {
        var registry = new CalyWindowRegistry();
        var vm = NewMainViewModel();
        var context = NewContext(vm, isPrimary: true);
        registry.Register(context);

        Assert.Same(context, registry.FindContext(vm));
        Assert.Null(registry.FindContext(NewMainViewModel()));
    }

    [AvaloniaFact]
    public void CloseWindowIfEmpty_ClosesANonPrimaryEmptyWindowWhileOthersRemain()
    {
        var registry = new CalyWindowRegistry();
        var primary = NewMainViewModel();
        var secondary = NewMainViewModel();
        var window = new Window();
        registry.Register(NewContext(primary, isPrimary: true));
        registry.Register(NewContext(secondary, isPrimary: false, window));

        bool closed = false;
        window.Closed += (_, _) => closed = true;

        registry.CloseWindowIfEmpty(secondary);

        Assert.True(closed);
    }

    /// <summary>
    /// The startup window gets no special treatment: once another window exists, emptying it
    /// (by dragging its last tab into that other window) closes it like any other.
    /// </summary>
    [AvaloniaFact]
    public void CloseWindowIfEmpty_ClosesThePrimaryWindowWhenAnotherWindowRemains()
    {
        var registry = new CalyWindowRegistry();
        var primary = NewMainViewModel();
        var secondary = NewMainViewModel();
        var window = new Window();
        registry.Register(NewContext(primary, isPrimary: true, window));
        registry.Register(NewContext(secondary, isPrimary: false));

        bool closed = false;
        window.Closed += (_, _) => closed = true;

        registry.CloseWindowIfEmpty(primary);

        Assert.True(closed);
    }

    /// <summary>
    /// Regression: with the primary window already closed, a detached window is the only one
    /// left. Closing its last tab must leave it on the splash screen - closing it would take
    /// the whole app down (ShutdownMode.OnLastWindowClose) with no way back in.
    /// </summary>
    [AvaloniaFact]
    public void CloseWindowIfEmpty_LeavesTheLastRemainingWindowOpen()
    {
        var registry = new CalyWindowRegistry();
        var onlyLeft = NewMainViewModel();
        var window = new Window();
        registry.Register(NewContext(onlyLeft, isPrimary: false, window));

        bool closed = false;
        window.Closed += (_, _) => closed = true;

        registry.CloseWindowIfEmpty(onlyLeft);

        Assert.False(closed);
    }

    [AvaloniaFact]
    public void CloseWindowIfEmpty_LeavesAWindowThatStillHasDocuments()
    {
        var registry = new CalyWindowRegistry();
        var primary = NewMainViewModel();
        var secondary = NewMainViewModel();
        var window = new Window();
        registry.Register(NewContext(primary, isPrimary: true));
        registry.Register(NewContext(secondary, isPrimary: false, window));
        secondary.PdfDocuments.Add(NewDocument());

        bool closed = false;
        window.Closed += (_, _) => closed = true;

        registry.CloseWindowIfEmpty(secondary);

        Assert.False(closed);
    }

    /// <summary>
    /// Regression for "Cannot re-show a closed window". When a tab is dragged onto another
    /// window's strip, Tabalonia empties the floating window's collection but only *hides* the
    /// window (passing suppressEmptySourceAction: true), so it can Show() it again if the user
    /// drags back out. Nothing may close a window merely because its collection went empty -
    /// closing is driven by LastTabClosedAction and Caly's own close path, both of which carry
    /// that intent.
    /// </summary>
    [AvaloniaFact]
    public void EmptyingTheCollection_DoesNotCloseTheWindowOnItsOwn()
    {
        var registry = new CalyWindowRegistry();
        var primary = NewMainViewModel();
        var floating = NewMainViewModel();
        var window = new Window();
        registry.Register(NewContext(primary, isPrimary: true));
        registry.Register(NewContext(floating, isPrimary: false, window));

        var document = NewDocument();
        floating.PdfDocuments.Add(document);
        Dispatcher.UIThread.RunJobs();

        bool closed = false;
        window.Closed += (_, _) => closed = true;

        // Exactly what Tabalonia's MoveItemToAnotherTabsControl does to the floating host.
        floating.PdfDocuments.Remove(document);
        Dispatcher.UIThread.RunJobs();

        Assert.False(closed);
    }

    /// <summary>
    /// Regression: a window that closes while it still holds documents must hand them over,
    /// otherwise they stay in the opened-files map with no owner and the app silently refuses
    /// to reopen those files for the rest of the session.
    /// </summary>
    [AvaloniaFact]
    public void Unregister_ReportsTheDocumentsAClosingWindowStillHeld()
    {
        var registry = new CalyWindowRegistry();
        var primary = NewMainViewModel();
        var detached = NewMainViewModel();
        var context = NewContext(detached, isPrimary: false);
        registry.Register(NewContext(primary, isPrimary: true));
        registry.Register(context);

        var first = NewDocument();
        var second = NewDocument();
        detached.PdfDocuments.Add(first);
        detached.PdfDocuments.Add(second);

        IReadOnlyList<DocumentViewModel>? orphaned = null;
        registry.DocumentsOrphaned += (_, docs) => orphaned = docs;

        registry.Unregister(context);

        Assert.NotNull(orphaned);
        Assert.Equal(2, orphaned!.Count);
        Assert.Contains(first, orphaned);
        Assert.Contains(second, orphaned);
    }

    [AvaloniaFact]
    public void Unregister_ReportsNothingForAnAlreadyEmptyWindow()
    {
        var registry = new CalyWindowRegistry();
        var primary = NewMainViewModel();
        var detached = NewMainViewModel();
        var context = NewContext(detached, isPrimary: false);
        registry.Register(NewContext(primary, isPrimary: true));
        registry.Register(context);

        bool raised = false;
        registry.DocumentsOrphaned += (_, _) => raised = true;

        registry.Unregister(context);

        Assert.False(raised);
    }

    /// <summary>
    /// The sidebar is window state, not app state: toggling it in one window must leave the
    /// other alone. It lives on MainViewModel, one per window - a document cannot own it,
    /// because a document moves between windows when its tab is dragged.
    /// </summary>
    [AvaloniaFact]
    public void IsDocumentPaneOpen_IsIndependentPerWindow()
    {
        var first = NewMainViewModel();
        var second = NewMainViewModel();

        Assert.True(first.IsDocumentPaneOpen);
        Assert.True(second.IsDocumentPaneOpen);

        first.IsDocumentPaneOpen = false;

        Assert.False(first.IsDocumentPaneOpen);
        Assert.True(second.IsDocumentPaneOpen);
    }

    /// <summary>
    /// Ctrl+F opens the sidebar of the window it was pressed in, and only that one.
    /// </summary>
    [AvaloniaFact]
    public void ActivateSearchTextTab_OpensOnlyItsOwnWindowsPane()
    {
        var first = NewMainViewModel();
        var second = NewMainViewModel();
        first.IsDocumentPaneOpen = false;
        second.IsDocumentPaneOpen = false;

        first.ActivateSearchTextTabCommand.Execute(null);

        Assert.True(first.IsDocumentPaneOpen);
        Assert.False(second.IsDocumentPaneOpen);
    }

    /// <summary>
    /// Pane width is window state alongside pane visibility: widening the sidebar in one window
    /// must not resize the other. The persisted setting is still app-wide — the last window to
    /// resize wins — so a new session starts every window at that width.
    /// </summary>
    [AvaloniaFact]
    public void PaneSize_IsIndependentPerWindow()
    {
        var first = NewMainViewModel();
        var second = NewMainViewModel();

        first.PaneSize = 420;
        second.PaneSize = 260;

        Assert.Equal(420, first.PaneSize);
        Assert.Equal(260, second.PaneSize);
    }

    /// <summary>
    /// The last window closing is an ordinary user action, so an empty registry must answer
    /// "no window" rather than throw. Callers reach for <c>Active</c> from teardown paths -
    /// a notification, the bring-to-front pipe command, a queued open draining - where an
    /// exception would be raised exactly when nothing is left to report it to.
    /// </summary>
    [AvaloniaFact]
    public void ActiveAndPrimary_AreNullOnceEveryWindowHasClosed()
    {
        var registry = new CalyWindowRegistry();
        var context = NewContext(NewMainViewModel(), isPrimary: true);
        registry.Register(context);

        Assert.NotNull(registry.Active);
        Assert.NotNull(registry.Primary);

        registry.Unregister(context);

        Assert.Empty(registry.Windows);
        Assert.Null(registry.Active);
        Assert.Null(registry.Primary);
    }

    /// <summary>
    /// Android recreates the activity - on rotation, or any configuration change - by calling
    /// <c>MainViewFactory</c> again, which builds a fresh <see cref="MainViewModel"/>. This
    /// lifetime has no <see cref="Window"/>, so a context carries no <c>Closed</c> event to
    /// unregister itself: the factory has to drop the previous one by hand. Without that,
    /// <c>Primary</c> keeps resolving to the dead view model no view is bound to, and documents
    /// open into nothing.
    /// </summary>
    [AvaloniaFact]
    public void ReRegisteringAfterUnregister_ReplacesTheContextRatherThanAccumulating()
    {
        var registry = new CalyWindowRegistry();

        var beforeRecreation = NewContext(NewMainViewModel(), isPrimary: true);
        registry.Register(beforeRecreation);

        // The activity is recreated: the factory drops the old context and registers the new.
        registry.Unregister(beforeRecreation);

        var afterRecreation = NewContext(NewMainViewModel(), isPrimary: true);
        registry.Register(afterRecreation);

        Assert.Single(registry.Windows);
        Assert.Same(afterRecreation, registry.Primary);
        Assert.Same(afterRecreation, registry.Active);
    }

    /// <summary>
    /// Tabalonia asks for the detached host <b>before</b> it commits to the detach, and can
    /// still give up afterwards without ever showing the window it asked for. Such a window has
    /// no <c>Closed</c> event to fire, so a context registered eagerly could never come back
    /// out - and a stuck extra entry makes the last real window think it is not the last one,
    /// so closing its final tab would exit the app instead of falling back to the splash screen.
    /// </summary>
    [AvaloniaFact]
    public void RegisterWhenOpened_RegistersOnlyOnceTheWindowIsActuallyShown()
    {
        var registry = new CalyWindowRegistry();
        var window = new Window { Width = 400, Height = 300 };
        var context = NewContext(NewMainViewModel(), isPrimary: false, window);

        registry.RegisterWhenOpened(context);

        // Abandoned here, nothing would ever have been registered.
        Assert.Empty(registry.Windows);

        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Single(registry.Windows);
        Assert.Same(context, registry.Windows[0]);

        // And it still unregisters itself the ordinary way.
        window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(registry.Windows);
    }

    /// <summary>
    /// Single-view lifetimes have no window to wait on, so there is nothing that could abandon
    /// the context - it registers straight away.
    /// </summary>
    [AvaloniaFact]
    public void RegisterWhenOpened_RegistersImmediatelyWithoutAWindow()
    {
        var registry = new CalyWindowRegistry();
        var context = NewContext(NewMainViewModel(), isPrimary: true);

        registry.RegisterWhenOpened(context);

        Assert.Single(registry.Windows);
        Assert.Same(context, registry.Primary);
    }
}
