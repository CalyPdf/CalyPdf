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
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Core.Services.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Caly.Core.ViewModels;

/// <summary>
/// View model that represent a PDF document.
/// </summary>
[DebuggerDisplay("[{_pdfService?.FileName}]")]
public sealed partial class DocumentViewModel : ViewModelBase
{
    public override string ToString()
    {
        return _pdfService?.FileName ?? "FileName NOT SET";
    }

    private readonly IPdfDocumentService _pdfService;
    private readonly PdfPageService _pdfPageService;

    private readonly CancellationTokenSource _mainCts = new();
    private readonly CancellationToken _mainToken;

    internal string? LocalPath { get; private set; }

    public bool IsActive => _pdfService.IsActive;

    /// <summary>
    /// Errors about a document belong to the window showing it. Resolved per notification and
    /// never cached: a document changes window whenever its tab is dragged.
    /// </summary>
    private protected override MainViewModel? NotificationTarget =>
        App.Current?.Services?.GetService<ICalyWindowRegistry>()?.FindOwnerOf(this)?.ViewModel;

    [ObservableProperty] private ObservableCollection<PageViewModel> _pages = [];

    [ObservableProperty] private int _selectedTabIndex;

    [ObservableProperty] private TextSelection? _textSelection;

    [ObservableProperty] private Range? _visiblePages;

    [ObservableProperty] private Range? _realisedPages;

    [ObservableProperty] private Range? _visibleThumbnails;

    [ObservableProperty] private Range? _realisedThumbnails;

    [ObservableProperty] private string? _interactiveActionOver;

    [ObservableProperty] private bool _isPagesLoading = true; // Start state is true, even if pages have not started loading just yet

    /// <summary>
    /// Snapshot of the page the user was last on, shown in this document's tab hover preview.
    /// <para>
    /// Captured as the document goes inactive, while its picture is still cached. Replaced on
    /// every later deactivation; the bitmap it replaces is disposed only after the new one is
    /// published, because the old one can still be on screen in an open tooltip.
    /// </para>
    /// </summary>
    [ObservableProperty] private WriteableBitmap? _tabPreview;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TabPreviewCaption))]
    private int? _tabPreviewPageNumber;

    /// <summary>
    /// Rotation of the previewed page, snapshotted with the image: the raster itself is
    /// unrotated, exactly as page pictures and sidebar thumbnails are.
    /// </summary>
    [ObservableProperty] private int _tabPreviewRotation;

    /// <summary>
    /// Whether a hover-triggered preview render (<see cref="EnsureTabPreview"/>) is currently
    /// in flight. Bound by <see cref="Controls.TabPreviewControl"/> to show a loading indicator
    /// in the image's place until the preview lands.
    /// <para>
    /// Scoped to the hover path only - the eager capture on deactivation is never something the
    /// user is watching, so it gets no loading state of its own.
    /// </para>
    /// </summary>
    [ObservableProperty] private bool _isTabPreviewLoading;

    public string? TabPreviewCaption =>
        TabPreviewPageNumber is { } pageNumber ? $"Page {pageNumber} of {PageCount}" : null;

    /// <summary>
    /// <c>null</c> until the document has finished loading (there is nothing valid to select
    /// while <see cref="PageCount"/> is still <c>0</c>) or if not selected. Set to <c>1</c> once
    /// loading succeeds; ends at <see cref="PageCount"/>.
    /// </summary>
    public int? SelectedPageNumber
    {
        get;
        set
        {
            if (value.HasValue)
            {
                if (value.Value <= 0)
                {
                    throw new ArgumentException("Selected page should exist in the document.", nameof(SelectedPageNumber));
                }

                if (value.Value > PageCount)
                {
                    throw new ArgumentException("Selected page should exist in the document.", nameof(SelectedPageNumber));
                }
            }

            if (!SetProperty(ref field, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedPageIndex));
            GoToPreviousPageCommand.NotifyCanExecuteChanged();
            GoToNextPageCommand.NotifyCanExecuteChanged();
            QueueActiveBookmarkUpdate();
        }
    }

    /// <summary>
    /// Starts at <c>0</c>, ends at <see cref="PageCount"/> <c>- 1</c>.
    /// <para><c>-1</c> if not selected.</para>
    /// </summary>
    public int SelectedPageIndex
    {
        get
        {
            if (SelectedPageNumber.HasValue)
            {
                return SelectedPageNumber.Value - 1;
            }

            return -1;
        }
        set
        {
            if (value != -1)
            {
                SelectedPageNumber = value + 1;
                return;
            }

            SelectedPageNumber = null;
        }
    }

    [ObservableProperty] private int _pageCount;

    [ObservableProperty] private string? _fileName;

    private readonly TaskCompletionSource<Task> _pagesLoadOutcome = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Completes once this document's page list has finished populating, or immediately if the
    /// document failed to open. Safe to await regardless of whether <see cref="LoadDocument"/>
    /// has been called yet, it just waits.
    /// </summary>
    public Task LoadPagesTask => _pagesLoadOutcome.Task.WaitAsync(_mainToken).Unwrap();

    private readonly IDisposable _searchResultsDisposable;

    private readonly ITextSearchService _textSearchService;

    /// <summary>
    /// How long the tab hover preview's render/rasterise (<see cref="CaptureTabPreview"/>,
    /// <see cref="EnsureTabPreview"/>) is allowed to take before giving up. A malformed PDF
    /// page can make Skia's picture replay hang; a <see cref="CancellationToken"/> cannot
    /// interrupt that blocking native call, so this can only mean "stop waiting on it", not
    /// "stop the work" - the abandoned render keeps running until it (if ever) returns.
    /// </summary>
    private readonly TimeSpan _tabPreviewTimeout;

#if DEBUG
    public DocumentViewModel()
    {
        if (!Design.IsDesignMode)
        {
            throw new InvalidOperationException("Should only be called in Design mode.");
        }

        _mainToken = _mainCts.Token;
        _tabPreviewTimeout = TimeSpan.FromSeconds(5);
        _searchResultsDisposable = null!;
        _propertiesTask = null!;
        _bookmarksTask = null!;
        _buildSearchIndex = null!;
        _searchResultsSource = null!;

        _pdfService = new PdfPigDocumentService(new JsonSettingsService(null!));

        IsPasswordProtected = _pdfService.IsPasswordProtected;
        FileName = _pdfService.FileName;
        LocalPath = _pdfService.LocalPath;
        PageCount = _pdfService.NumberOfPages;
        TextSelection = new TextSelection(PageCount);
    }
#endif

    public DocumentViewModel(IPdfDocumentService pdfService, PdfPageService pdfPageService,
        ITextSearchService textSearchService, TimeSpan? tabPreviewTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(pdfService, nameof(pdfService));

        System.Diagnostics.Debug.Assert(pdfService.NumberOfPages == 0);

        _mainToken = _mainCts.Token;
        _pdfService = pdfService;
        _pdfPageService = pdfPageService;
        _textSearchService = textSearchService;
        _tabPreviewTimeout = tabPreviewTimeout ?? TimeSpan.FromSeconds(5);

        _pdfService.PasswordPrompt = RequestPasswordAsync;

        _buildSearchIndex = new Lazy<Task>(BuildSearchIndex);

        _bookmarksTask = new Lazy<Task<HierarchicalTreeDataGridSource<PdfBookmarkNode>?>>(GetBookmarks);
        _propertiesTask = new Lazy<Task<DocumentPropertiesViewModel?>>(GetProperties);
        _embeddedFilesTask = new Lazy<Task<IReadOnlyList<PdfEmbeddedFileViewModel>>>(GetEmbeddedFiles);

        _searchResultsDisposable = SearchResults
            .GetWeakCollectionChangedObservable()
            .ObserveOn(Scheduler.Default)
            .Subscribe(e =>
            {
                Debug.ThrowOnUiThread();

                try
                {
                    switch (e.Action)
                    {
                        case NotifyCollectionChangedAction.Reset:
                            // Clear selection highlights
                            foreach (var page in Pages)
                            {
                                page.UpdateSearchResultsRanges(null);
                            }
                            break;

                        case NotifyCollectionChangedAction.Add:
                            if (e.NewItems?.Count > 0)
                            {
                                var searchResults = e.NewItems.OfType<TextSearchResult>().ToArray();

                                if (searchResults.Length == 0 || searchResults[0].PageNumber <= 0)
                                {
                                    // Clear selection highlights
                                    foreach (var page in Pages)
                                    {
                                        if (page.SearchResults is not null)
                                        {
                                            page.UpdateSearchResultsRanges(null);
                                        }
                                    }
                                }
                                else
                                {
                                    foreach (var result in searchResults)
                                    {
                                        System.Diagnostics.Debug.Assert(result.Nodes is not null);

                                        var searchRange = result.Nodes
                                            .Where(x => x is
                                                { ItemType: SearchResultItemType.Word, WordIndex: not null })
                                            .Select(x => new Range(new Index(x.WordIndex!.Value),
                                                new Index(x.WordIndex.Value + x.WordCount!.Value - 1))).ToArray();
                                        
                                        var page = GetPage(result.PageNumber);
                                        if (page is null)
                                        {
                                            continue; // Pages might still be loading
                                        }

                                        page.UpdateSearchResultsRanges(searchRange);
                                    }
                                }
                            }
                            break;

                        case NotifyCollectionChangedAction.Remove:
                        case NotifyCollectionChangedAction.Replace:
                        case NotifyCollectionChangedAction.Move:
                            throw new NotImplementedException($"SearchResults Action '{e.Action}'.");
                    }
                }
                catch (OperationCanceledException)
                {
                    // No op
                }
                catch (Exception ex)
                {
                    Debug.WriteExceptionToFile(ex);
                    Dispatcher.UIThread.Post(() => Exception = new ExceptionViewModel(ex));
                }
            });

        SearchResultsSource = new HierarchicalTreeDataGridSource<TextSearchResult>(SearchResults)
        {
            Columns =
            {
                new HierarchicalExpanderColumn<TextSearchResult>(
                    new TextColumn<TextSearchResult, string>(null, x => x.ToString()),
                    x => x.Nodes)
            }
        };

        Dispatcher.UIThread.Invoke(() =>
        {
            SearchResultsSource.RowSelection!.SingleSelect = true;
            SearchResultsSource.RowSelection.SelectionChanged += TextSearchSelectionChanged;
        }, DispatcherPriority.Send, _mainToken);
    }

    /// <summary>
    /// The activation transition currently in flight. UI thread only - both
    /// <see cref="SetActive"/> and <see cref="SetInactive"/> are called from the
    /// selected-document message handler.
    /// </summary>
    private Task _activityTransition = Task.CompletedTask;

    /// <summary>
    /// Makes this the document the user is looking at, and puts back whatever the last
    /// <see cref="SetInactive"/> released.
    /// </summary>
    public void SetActive()
    {
        Debug.ThrowNotOnUiThread();

        _pdfService.IsActive = true;
        _activityTransition = QueueActivityTransition(RestoreContent);
    }

    /// <summary>
    /// Steps this document out of view and releases everything rendered for it.
    /// </summary>
    public void SetInactive()
    {
        Debug.ThrowNotOnUiThread();

        _pdfService.IsActive = false;
        _activityTransition = QueueActivityTransition(Clear);
    }

    /// <summary>
    /// Runs <paramref name="transition"/> once the transition before it has finished.
    /// <para>
    /// The teardown is asynchronous, so without this chaining it can land on a document that
    /// has since been reactivated: <see cref="PdfPageService.CancelAndClear"/> cancels
    /// whichever render generation is current when it runs, which by then is the one the
    /// reactivation just started. Its queued renders are then dropped as they are picked up
    /// and the page never leaves its loading skeleton.
    /// </para>
    /// </summary>
    private async Task QueueActivityTransition(Func<Task> transition)
    {
        Task previous = _activityTransition;

        try
        {
            // Faults and cancellation are already reported by the transition that produced
            // them; this one only needs to know that it is done.
            await previous.ConfigureAwait(false);
        }
        catch
        { /* No op */ }

        try
        {
            await transition().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        { /* No op */ }
        catch (Exception e)
        {
            Debug.WriteExceptionToFile(e);
        }
    }

    /// <summary>
    /// Re-requests what <see cref="Clear"/> released, so that becoming active again is the
    /// exact inverse of becoming inactive.
    /// <para>
    /// Only the view knows which pages are on screen, so a document whose view has not
    /// reported a range yet is left alone: it has nothing to restore, and the view will
    /// request its first render itself.
    /// </para>
    /// </summary>
    private async Task RestoreContent()
    {
        if (!IsActive)
        {
            // Deactivated again while this transition waited its turn.
            return;
        }

        if (VisiblePages.HasValue && RealisedPages.HasValue)
        {
            await RefreshPages();
        }

        if (VisibleThumbnails.HasValue && RealisedThumbnails.HasValue)
        {
            await RefreshThumbnails();
        }
    }

    private Task<DocumentOpeningState>? _loadDocumentTask;
    private readonly Lock _loadDocumentLock = new();

    /// <summary>
    /// Open the pdf document.
    /// </summary>
    public Task<DocumentOpeningState> LoadDocument(IStorageFile? storageFile, string? password, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(storageFile, nameof(storageFile));

        // Ensure method is called only once (one instance per document)
        lock (_loadDocumentLock)
        {
            if (_loadDocumentTask is not null)
            {
                throw new InvalidOperationException("Attempt to load a pdf document more than once with the same DocumentViewModel.");
            }

            LocalPath = storageFile.Path.LocalPath;
            _loadDocumentTask = LoadDocumentCore(storageFile, password, token);
            return _loadDocumentTask;
        }
    }

    private async Task<DocumentOpeningState> LoadDocumentCore(IStorageFile? storageFile, string? password, CancellationToken token)
    {
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(_mainToken, token);

        var state = await _pdfService.OpenDocument(storageFile, password, combinedCts.Token).ConfigureAwait(false);

        System.Diagnostics.Debug.Assert(_pdfService.LocalPath == LocalPath);

        bool isPasswordProtected = _pdfService.IsPasswordProtected;
        string? fileName = _pdfService.FileName;
        int numberOfPages = _pdfService.NumberOfPages;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            IsPasswordProtected = isPasswordProtected;
            FileName = fileName;

            if (state == DocumentOpeningState.Success)
            {
                PageCount = numberOfPages;
                TextSelection = new TextSelection(numberOfPages);
                if (numberOfPages > 0)
                {
                    SelectedPageNumber = 1;
                }
            }
            else
            {
                IsPagesLoading = false;
            }
        });

        if (state == DocumentOpeningState.Success)
        {
            _pdfPageService.Initialise();
            _pagesLoadOutcome.TrySetResult(Task.Run(LoadPages, _mainToken));
        }
        else
        {
            _pagesLoadOutcome.TrySetResult(Task.CompletedTask);
        }

        return state;
    }

    private async Task LoadPages()
    {
        Debug.ThrowOnUiThread();

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsPagesLoading = true);

            System.Diagnostics.Debug.Assert(TextSelection is not null);

            // Use 1st page size as default page size
            var firstPage = new PageViewModel(1, TextSelection, _pdfPageService.TileRenderService, _pdfService.PpiScale, CopyTextCommand);
            var pageSize = await _pdfPageService.GetPageSize(1, _mainToken).ConfigureAwait(false);
            if (pageSize.HasValue)
            {
                // Page is not yet in the collection — no UI observer yet, safe to call from thread pool
                firstPage.SetSize(pageSize.Value);
            }

            var defaultSize = firstPage.Size;

            Dispatcher.UIThread.Invoke(() => Pages.Add(firstPage));

            for (int p = 2; p <= PageCount; ++p)
            {
                _mainToken.ThrowIfCancellationRequested();
                var newPage = new PageViewModel(p, TextSelection, _pdfPageService.TileRenderService, _pdfService.PpiScale, CopyTextCommand)
                {
                    Size = defaultSize
                };
                _pdfPageService.RequestPageSize(newPage);
                Dispatcher.UIThread.Invoke(() => Pages.Add(newPage),
                    DispatcherPriority.Send,
                    _mainToken); // Could do in batches
            }
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsPagesLoading = false);
        }
    }

    /// <summary>
    /// Retrieves the view model for the specified page number in the document, if available.
    /// </summary>
    /// <param name="pageNumber">The one-based page number to retrieve. Must be greater than zero and less than or equal to the total number of
    /// pages in the document.</param>
    /// <returns>The <see cref="PageViewModel"/> for the specified page number, or <see langword="null"/> if the page has not
    /// been loaded.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="pageNumber"/> is less than or equal to zero, or greater than the total number of pages
    /// in the document.</exception>
    public PageViewModel? GetPage(int pageNumber)
    {
        if (pageNumber <= 0 || pageNumber > PageCount)
        {
            throw new ArgumentException("Page number should exist in the document.", nameof(pageNumber));
        }

        int pageIndex = pageNumber - 1;
        if (pageIndex > Pages.Count - 1)
        {
            System.Diagnostics.Debug.WriteLine($"Page {pageNumber} is not loaded yet.");
            return null;
        }

        return Pages[pageIndex];
    }

    [RelayCommand(CanExecute = nameof(CanGoToPreviousPage))]
    private void GoToPreviousPage()
    {
        if (!SelectedPageNumber.HasValue)
        {
            return;
        }

        SelectedPageNumber = Math.Max(1, SelectedPageNumber.Value - 1);
    }

    private bool CanGoToPreviousPage()
    {
        if (!SelectedPageNumber.HasValue)
        {
            return false;
        }

        return SelectedPageNumber.Value > 1;
    }

    [RelayCommand(CanExecute = nameof(CanGoToNextPage))]
    private void GoToNextPage()
    {
        if (!SelectedPageNumber.HasValue)
        {
            return;
        }

        SelectedPageNumber = Math.Min(PageCount, SelectedPageNumber.Value + 1);
    }

    private bool CanGoToNextPage()
    {
        if (!SelectedPageNumber.HasValue)
        {
            return false;
        }

        return SelectedPageNumber.Value < PageCount;
    }
    
    [RelayCommand]
    private async Task CloseDocument(CancellationToken token)
    {
        var pdfDocumentsService = App.Current?.Services?.GetRequiredService<IPdfDocumentsManagerService>()!;
        await Task.Run(() => pdfDocumentsService.CloseUnloadDocument(this), token);
    }

    [RelayCommand]
    private async Task RefreshPages()
    {
        try
        {
            await _pdfPageService.RefreshPages(new RefreshPagesRequestMessage()
            {
                Document = this,
                VisiblePages = VisiblePages,
                RealisedPages = RealisedPages,
                VisibleThumbnails = VisibleThumbnails,
                RealisedThumbnails = RealisedThumbnails
            });
        }
        catch (OperationCanceledException)
        { }
        catch (Exception ex)
        {
            Debug.WriteExceptionToFile(ex);
        }
    }

    [RelayCommand]
    private async Task RefreshThumbnails()
    {
        try
        {
            await _pdfPageService.RefreshThumbnails(new RefreshPagesRequestMessage()
            {
                Document = this,
                VisiblePages = VisiblePages,
                RealisedPages = RealisedPages,
                VisibleThumbnails = VisibleThumbnails,
                RealisedThumbnails = RealisedThumbnails
            });
        }
        catch (OperationCanceledException)
        { }
        catch (Exception ex)
        {
            Debug.WriteExceptionToFile(ex);
        }
    }

    [RelayCommand]
    private void ClearSelection()
    {
        Debug.ThrowNotOnUiThread();

        if (TextSelection is null)
        {
            return;
        }

        System.Diagnostics.Debug.Assert(TextSelection.GetStartPageIndex() <= TextSelection.GetEndPageIndex());

        TextSelection.ResetSelection();
    }

    /// <summary>
    /// Snapshots the page the user was last on, for this document's tab hover preview.
    /// <para>
    /// Called from <see cref="Clear"/> after the pages have released their own references but
    /// before <see cref="PdfPageService.CancelAndClear"/> empties the picture cache. That is
    /// the one moment the snapshot is a downscale of work already done rather than a fresh
    /// render - which is why it must not move either side of that window.
    /// </para>
    /// <para>
    /// A capture failure is caught and logged here rather than propagated: it degrades to "no
    /// preview" instead of stopping <see cref="Clear"/> short of
    /// <see cref="PdfPageService.CancelAndClear"/> on the line after this call.
    /// </para>
    /// </summary>
    private async Task CaptureTabPreview()
    {
        try
        {
            if (SelectedPageNumber is not { } pageNumber)
            {
                return;
            }

            PageViewModel? page = GetPage(pageNumber);
            if (page is null || !page.IsSizeSet())
            {
                return;
            }

            // Snapshot on the calling thread - as EnsureTabPreview already does for its own
            // Task.Run - then rasterise off it. QueueActivityTransition only guarantees a
            // background thread when the previous transition was still running at the point of
            // the await; when it had already completed (the ordinary case, since the user has
            // usually been reading the document for a while before switching tabs), this method
            // would otherwise run synchronously on whatever thread called SetInactive(), which
            // is the UI thread.
            PixelSize target = page.TabPreviewSize;
            Size pageSize = page.Size;
            int rotation = page.Rotation;

            // Null when the page never rendered, so nothing was ever cached for it. The hover
            // path fills that in if the user asks for it.
            //
            // WaitAsync bounds how long we wait, not how long the rasterisation runs: Skia's
            // picture replay is a blocking native call a CancellationToken cannot interrupt, so
            // a malformed page can only be abandoned here, not stopped. TimeoutException falls
            // through to the catch below like any other capture failure. Linked to _mainToken
            // so a document disposed while this is waiting stops waiting immediately rather
            // than sitting out the full timeout.
            WriteableBitmap? preview = await Task.Run(
                    () => _pdfPageService.TryCapturePreview(pageNumber, target, pageSize))
                .WaitAsync(_tabPreviewTimeout, _mainToken);
            if (preview is null)
            {
                return;
            }

            await SetTabPreview(preview, pageNumber, rotation);
        }
        catch (Exception e)
        {
            Debug.WriteExceptionToFile(e);
        }
    }

    /// <summary>
    /// Publishes <paramref name="preview"/> and only then disposes the one it replaced - an
    /// open tooltip may still be drawing the old bitmap.
    /// </summary>
    private async Task SetTabPreview(WriteableBitmap preview, int pageNumber, int rotation)
    {
        WriteableBitmap? previous = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            WriteableBitmap? old = TabPreview;

            TabPreview = preview;
            TabPreviewPageNumber = pageNumber;
            TabPreviewRotation = rotation;

            return old;
        });

        previous?.Dispose();
    }

    /// <summary>
    /// The hover render in flight, if any. UI thread only - the command's prologue and
    /// <see cref="CancelTabPreview"/> are both driven by the tooltip opening and closing.
    /// </summary>
    private CancellationTokenSource? _tabPreviewCts;

    /// <summary>
    /// Renders a tab preview for a document whose deactivation had nothing to capture,
    /// because its page had not rendered yet.
    /// <para>
    /// Invoked when the tab's tooltip opens. The tooltip's show delay is the debounce - a
    /// pointer sweeping across the strip never gets here.
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task EnsureTabPreview()
    {
        Debug.ThrowNotOnUiThread();

        if (IsActive || TabPreview is not null)
        {
            return;
        }

        if (SelectedPageNumber is not { } pageNumber)
        {
            return;
        }

        PageViewModel? page = GetPage(pageNumber);
        if (page is null || !page.IsSizeSet())
        {
            return;
        }

        _tabPreviewCts?.Cancel();
        _tabPreviewCts?.Dispose();

        var cts = CancellationTokenSource.CreateLinkedTokenSource(_mainToken);
        _tabPreviewCts = cts;

        PixelSize target = page.TabPreviewSize;
        Size pageSize = page.Size;
        int rotation = page.Rotation;

        IsTabPreviewLoading = true;

        try
        {
            // WaitAsync bounds how long we wait, not how long the render/rasterise runs: a
            // malformed page can make GetRenderPageAsync or Skia's picture replay hang, and
            // neither is interruptible via cts.Token alone (Skia's replay is a blocking native
            // call). Linking the token means a cancelled hover (tooltip closed) stops waiting
            // immediately rather than sitting out the full timeout. TimeoutException falls
            // through to the catch below and is logged like any other failure - "the page
            // seems malformed" is worth knowing about, unlike a plain cancellation.
            WriteableBitmap? preview = await Task.Run(
                    () => _pdfPageService.RenderPreviewAsync(pageNumber, target, pageSize, cts.Token),
                    cts.Token)
                .WaitAsync(_tabPreviewTimeout, cts.Token);

            if (preview is null)
            {
                return;
            }

            if (cts.IsCancellationRequested)
            {
                preview.Dispose();
                return;
            }

            await SetTabPreview(preview, pageNumber, rotation);
        }
        catch (OperationCanceledException)
        { /* The pointer left the tab. Not a failure. */ }
        catch (Exception e)
        {
            Debug.WriteExceptionToFile(e);
        }
        finally
        {
            IsTabPreviewLoading = false;

            // This render put a picture back in the cache of a document that is meant to hold
            // nothing. Same guard as Clear(): the user may have selected the tab while it ran,
            // and tearing down then would blank the document they are now looking at.
            if (!IsActive)
            {
                try
                {
                    await _pdfPageService.CancelAndClear().ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    // A finally body's own exception is not caught by this method's try/catch
                    // above it. Left unguarded, it would escape EnsureTabPreview entirely and,
                    // since this command runs via ICommand.Execute on the UI thread, surface as
                    // an unhandled exception there instead of a logged error.
                    Debug.WriteExceptionToFile(e);
                }
            }
        }
    }

    /// <summary>
    /// Drops an in-flight hover render, when the tab's tooltip closes. UI thread only.
    /// </summary>
    /// <remarks>
    /// The source is cancelled but not disposed here: the render is still unwinding through
    /// it. Whichever comes first - the next <see cref="EnsureTabPreview"/> or
    /// <see cref="DisposeAsync"/> - disposes it.
    /// </remarks>
    internal void CancelTabPreview()
    {
        Debug.ThrowNotOnUiThread();

        _tabPreviewCts?.Cancel();
    }

    [RelayCommand]
    private async Task Clear()
    {
        if (IsActive)
        {
            // Reactivated while this teardown waited its turn: releasing now would blank the
            // document the user is looking at, and nothing would ask for it again.
            return;
        }

        // Capture pictures/thumbnails and clear all UI-bound page properties on the UI thread
        // in one batch, then dispose the captured resources off the UI thread.
        var toDispose = new List<IDisposable?>();

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (var page in Pages)
            {
                toDispose.Add(page.PdfPicture);
                toDispose.Add(page.Thumbnail);
                page.PdfTextLayer = null;
                page.PdfPicture = null;
                page.Thumbnail = null;
            }
        });

        foreach (var item in toDispose)
        {
            item?.Dispose();
        }

        // Before CancelAndClear empties the picture cache: the pages have dropped their own
        // references above, but the cache still holds its own, so this is still a downscale.
        await CaptureTabPreview().ConfigureAwait(false);

        await _pdfPageService.CancelAndClear().ConfigureAwait(false);

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, false);
    }
}