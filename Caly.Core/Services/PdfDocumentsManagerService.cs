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
using Avalonia.Controls.Notifications;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Caly.Core.Services.Interfaces;
using Caly.Core.ViewModels;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Caly.Core.Utilities;

namespace Caly.Core.Services;

internal sealed partial class PdfDocumentsManagerService : IPdfDocumentsManagerService, IDisposable
{
    /// <summary>
    /// A queued open, together with the window the user asked for it in.
    /// <para>
    /// The target is captured when the user acts, not resolved when the queue drains: the file
    /// picker is owned by a window, so showing it activates that window and would otherwise
    /// move the "active window" out from under the request.
    /// </para>
    /// </summary>
    private readonly record struct OpenDocumentRequest(IStorageFile? File, MainViewModel? Target);

    private sealed class PdfDocumentRecord
    {
        public required AsyncServiceScope Scope { get; init; }

        public required DocumentViewModel Document { get; init; }

        /// <summary>
        /// Whether the document has been put into a window's tab strip yet. UI thread only -
        /// it is set and read from dispatcher callbacks.
        /// <para>
        /// Before that point the document belongs to no window simply because its open has not
        /// got that far, which must not be mistaken for its window having closed.
        /// </para>
        /// </summary>
        public bool IsShownInAWindow { get; set; }
    }

    /// <summary>
    /// What an existing <see cref="_openedFiles"/> entry means for a request to open that same
    /// file again.
    /// </summary>
    internal enum OpenedFileState
    {
        /// <summary>
        /// The owning window was brought forward with the document selected. Nothing to do.
        /// </summary>
        Shown,

        /// <summary>
        /// Another request is still opening this file. It will appear on its own; opening it a
        /// second time here would tear down that request's document mid-parse.
        /// </summary>
        Opening,

        /// <summary>
        /// The record outlived its window. Drop it and open the file fresh.
        /// </summary>
        Stale
    }

    private readonly ICalyWindowRegistry _windowRegistry;
    private readonly IFilesService _filesService;
    private readonly IDialogService _dialogService;
    private readonly IClipboardService _clipboardService;

    private readonly ChannelWriter<OpenDocumentRequest> _channelWriter;
    private readonly ChannelReader<OpenDocumentRequest> _channelReader;
    private readonly CancellationTokenSource _processingQueueCts = new();

    private readonly ConcurrentDictionary<string, PdfDocumentRecord> _openedFiles = new();

    private async Task ProcessDocumentsQueue(CancellationToken token)
    {
        try
        {
            Debug.ThrowOnUiThread();

            await Parallel.ForEachAsync(_channelReader.ReadAllAsync(token), token, async (d, ct) =>
            {
                try
                {
                    if (d.File is not null)
                    {
                        await OpenLoadDocumentInternal(d, null, ct);
                    }
                }
                catch (Exception e)
                {
                    await _dialogService.ShowExceptionWindowAsync(e);
                }
            });
        }
        catch (OperationCanceledException)
        { /* No op */ }
        catch (Exception e)
        {
            // Critical error - can't open document anymore
            System.Diagnostics.Debug.WriteLine($"ERROR in WorkerProc {e}");
            Debug.WriteExceptionToFile(e);
            await _dialogService.ShowExceptionWindowAsync(e);
            throw;
        }
    }

    public PdfDocumentsManagerService(ICalyWindowRegistry windowRegistry, IFilesService filesService, IDialogService dialogService, IClipboardService clipboardService)
    {
        Debug.ThrowNotOnUiThread();

        _windowRegistry = windowRegistry ?? throw new NullReferenceException("Missing window registry instance.");

        _filesService = filesService ?? throw new NullReferenceException("Missing File Service instance.");
        _dialogService = dialogService ?? throw new NullReferenceException("Missing Dialog Service instance.");
        _clipboardService = clipboardService ?? throw new NullReferenceException("Missing clipboard Service instance.");

        Channel<OpenDocumentRequest> fileChannel = Channel.CreateUnbounded<OpenDocumentRequest>(new UnboundedChannelOptions()
        {
            AllowSynchronousContinuations = false,
            SingleReader = false,
            SingleWriter = false
        });

        _channelWriter = fileChannel.Writer;
        _channelReader = fileChannel.Reader;

        _windowRegistry.DocumentsOrphaned += OnDocumentsOrphaned;

        RegisterMessagesHandlers();

        _ = Task.Run(() => ProcessDocumentsQueue(_processingQueueCts.Token));
    }

    public async Task OpenLoadDocument(MainViewModel? target, CancellationToken cancellationToken)
    {
        Debug.ThrowNotOnUiThread();

        // Resolved before the picker opens, and the picker is then shown over that same window.
        // The dialog is owned by a window and showing it activates it, so resolving the target
        // afterwards would send the document to whichever window the dialog happened to raise.
        CalyWindowContext? context = target is not null
            ? _windowRegistry.FindContext(target)
            : _windowRegistry.Active;

        if (context is null)
        {
            // Every window has closed, or the asking one has: there is nowhere to put the
            // document and no window to show the picker over.
            return;
        }

        IStorageFile? file = await _filesService.OpenPdfFileAsync(context.Window);

        MainViewModel resolved = context.ViewModel;

        await Task.Run(() => EnqueueOpenRequest(file, resolved, cancellationToken), cancellationToken);
    }

    public async Task OpenLoadDocument(string? path, CancellationToken cancellationToken)
    {
        Debug.ThrowOnUiThread();

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            // TODO - Log
            return;
        }

        var file = await _filesService.TryGetFileFromPathAsync(path);

        // No window in mind: a path comes from the command line or a second instance.
        await OpenLoadDocument(file, null, cancellationToken);
    }

    public async Task OpenLoadDocument(IStorageFile? storageFile, MainViewModel? target, CancellationToken cancellationToken)
    {
        Debug.ThrowOnUiThread();

        // Callers that know which window asked - a drop lands on one specific window - pass it
        // in, because a drop does not activate the window it lands on. The rest fall back to
        // the active window, captured now rather than when the queue drains.
        target ??= await Dispatcher.UIThread.InvokeAsync(() => _windowRegistry.Active?.ViewModel);

        await EnqueueOpenRequest(storageFile, target, cancellationToken);
    }

    private async Task EnqueueOpenRequest(IStorageFile? storageFile, MainViewModel? target, CancellationToken cancellationToken)
    {
        await _channelWriter.WriteAsync(new OpenDocumentRequest(storageFile, target), cancellationToken);
    }

    /// <summary>
    /// Logs a task's failure without awaiting it, so an abandoned task cannot resurface as an
    /// <see cref="TaskScheduler.UnobservedTaskException"/> when it is finalized. Cancellation is
    /// already excluded: a canceled task never runs an <c>OnlyOnFaulted</c> continuation.
    /// </summary>
    private static void ObserveFailure(Task task)
    {
        _ = task.ContinueWith(static t => Debug.WriteExceptionToFile(t.Exception!),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>
    /// The window a queued open should land in: the one captured when the user acted, unless
    /// that window has since closed, in which case the currently active one.
    /// </summary>
    internal MainViewModel? ResolveOpenTarget(MainViewModel? capturedTarget)
    {
        if (capturedTarget is not null && _windowRegistry.FindContext(capturedTarget) is not null)
        {
            return capturedTarget;
        }

        // Null when every window has closed while the open sat in the queue: there is nowhere
        // to show the document, so the open aborts cleanly instead of failing.
        return _windowRegistry.Active?.ViewModel;
    }

    public async Task<int> OpenLoadDocuments(IEnumerable<IStorageItem?> storageFiles, MainViewModel? target, CancellationToken cancellationToken)
    {
        Debug.ThrowOnUiThread();

        // Resolved once, so every file of one drop lands in the same window even if the user
        // activates another while the batch is still queueing.
        target ??= await Dispatcher.UIThread.InvokeAsync(() => _windowRegistry.Active?.ViewModel);

        int count = 0;
        foreach (IStorageItem? item in storageFiles)
        {
            if (item is not IStorageFile file)
            {
                continue;
            }

            await OpenLoadDocument(file, target, cancellationToken);
            count++;
        }

        return count;
    }

    public async Task CloseUnloadDocument(DocumentViewModel? document)
    {
        Debug.ThrowOnUiThread();

        if (document is null)
        {
            return;
        }

        if (string.IsNullOrEmpty(document.LocalPath))
        {
            throw new Exception($"Invalid {nameof(document.LocalPath)} value for view model.");
        }

        MainViewModel? owner = await Dispatcher.UIThread.InvokeAsync(() => RemoveDocumentFromOwnerWindow(document));

        // Unload before closing the window, never after: if closing threw or re-entered, a
        // stale entry would be left in _openedFiles and the file could never be reopened.
        await UnloadDocumentRecord(document);

        if (owner is not null)
        {
            await Dispatcher.UIThread.InvokeAsync(() => _windowRegistry.CloseWindowIfEmpty(owner));
        }

        Helpers.RequestGcCollect();
    }

    /// <summary>
    /// Removes <paramref name="document"/> from the window that currently holds it and moves
    /// that window's selection to a neighbouring tab. UI thread only.
    /// <para>
    /// The owner is resolved now rather than remembered: a detached or re-docked tab lives in
    /// a different window's collection than the one it was opened into.
    /// </para>
    /// </summary>
    internal MainViewModel? RemoveDocumentFromOwnerWindow(DocumentViewModel document)
    {
        Debug.ThrowNotOnUiThread();

        if (_windowRegistry.FindOwnerOf(document)?.ViewModel is not { } owner)
        {
            return null;
        }

        int currentIndex = owner.PdfDocuments.IndexOf(document);
        owner.PdfDocuments.Remove(document);
        owner.SelectedDocumentIndex = Math.Min(Math.Max(0, currentIndex), owner.PdfDocuments.Count - 1);

        return owner;
    }

    /// <summary>
    /// Undoes an open that failed: takes <paramref name="document"/> back out of the window
    /// holding it and drops its <see cref="_openedFiles"/> entry, so the file can be opened
    /// again. Returns the window it was removed from, or <c>null</c> if none held it.
    /// <para>
    /// The owner is resolved now rather than taken from the window the open was headed for. A
    /// tab that is still opening is already in the strip, reading "Opening '...'", so the user
    /// can drag it into another window while the document parses; removing it from the
    /// original window would then be a no-op, and the caller disposes the DI scope regardless -
    /// leaving a live tab bound to a disposed <see cref="IPdfDocumentService"/>.
    /// </para>
    /// <para>
    /// The window is deliberately left open even if this empties it. Closing is intent-driven -
    /// Tabalonia's <c>LastTabClosedAction</c>, or Caly's own close-tab path - and a failed open
    /// carries no such intent: dropping an unreadable file on an empty window must not make
    /// that window vanish out from under the error it is about to show.
    /// </para>
    /// </summary>
    internal async Task<MainViewModel?> RevertFailedOpen(DocumentViewModel document, string key)
    {
        MainViewModel? owner = await Dispatcher.UIThread.InvokeAsync(
            () => RemoveDocumentFromOwnerWindow(document));

        TryRemoveRecord(key, document, out _);

        return owner;
    }

    /// <summary>
    /// Drops <paramref name="document"/> from the opened-files map and disposes its DI scope.
    /// Safe to call for a document that was never registered.
    /// <para>
    /// Removal matches on the record, not just on the path: unloads run in the background and
    /// are serialised behind each other's teardown, so the same path can already have been
    /// reopened into a brand new record by the time this runs. Removing by key alone would
    /// dispose that new document's scope and drop it from the map.
    /// </para>
    /// </summary>
    private async Task UnloadDocumentRecord(DocumentViewModel document)
    {
        if (ResolveKey(document) is not { Length: > 0 } key)
        {
            return;
        }

        if (TryRemoveRecord(key, document, out PdfDocumentRecord? record))
        {
            await record.Scope.DisposeAsync();
        }
    }

    /// <summary>
    /// The <see cref="_openedFiles"/> key <paramref name="document"/> is filed under.
    /// </summary>
    private string? ResolveKey(DocumentViewModel document)
    {
        if (document.LocalPath is { Length: > 0 } localPath)
        {
            return localPath;
        }

        // LocalPath is assigned inside LoadDocument. Fall back to identity so a document that
        // never got that far cannot leave a record behind that nothing can ever remove.
        foreach (var pair in _openedFiles)
        {
            if (ReferenceEquals(pair.Value.Document, document))
            {
                return pair.Key;
            }
        }

        return null;
    }

    /// <summary>
    /// The record <paramref name="document"/> is filed under, or <c>null</c> if the map no
    /// longer holds it - it was unloaded, or another document took over its path.
    /// </summary>
    private PdfDocumentRecord? FindRecord(DocumentViewModel document)
    {
        return ResolveKey(document) is { Length: > 0 } key &&
               _openedFiles.TryGetValue(key, out PdfDocumentRecord? record) &&
               ReferenceEquals(record.Document, document)
            ? record
            : null;
    }

    /// <summary>
    /// Brings the already-open <paramref name="document"/> to the user, and reports what its
    /// existing record means for the request that asked for it. UI thread only.
    /// </summary>
    internal OpenedFileState ShowExistingDocument(DocumentViewModel document)
    {
        Debug.ThrowNotOnUiThread();

        if (_windowRegistry.FindOwnerOf(document) is not { } ownerContext)
        {
            // Belonging to no window is only evidence that the record outlived its window once
            // the document has actually been put into one. Before that it means a concurrent
            // request is still opening this very file, and dropping its record here would
            // dispose the DI scope out from under a running parse.
            return FindRecord(document)?.IsShownInAWindow == true
                ? OpenedFileState.Stale
                : OpenedFileState.Opening;
        }

        int index = ownerContext.ViewModel.PdfDocuments.IndexOf(document);
        if (index != -1 && ownerContext.ViewModel.SelectedDocumentIndex != index)
        {
            ownerContext.ViewModel.SelectedDocumentIndex = index;
        }

        // Reopening a file that lives in another window should bring that window
        // forward rather than silently doing nothing in the active one.
        ownerContext.Window?.Activate();

        return OpenedFileState.Shown;
    }

    /// <summary>
    /// Records that <paramref name="document"/> has reached a window's tab strip, so a later
    /// request for the same file can tell "its window closed" from "it is still opening".
    /// UI thread only.
    /// </summary>
    internal void MarkShownInAWindow(DocumentViewModel document)
    {
        Debug.ThrowNotOnUiThread();

        if (FindRecord(document) is { } record)
        {
            record.IsShownInAWindow = true;
        }
    }

    /// <summary>
    /// Registers <paramref name="document"/> as the open document for <paramref name="key"/>,
    /// failing if that file is already open.
    /// </summary>
    internal bool TryAddRecord(string key, DocumentViewModel document, AsyncServiceScope scope)
    {
        return _openedFiles.TryAdd(key, new PdfDocumentRecord
        {
            Scope = scope,
            Document = document
        });
    }

    /// <summary>
    /// Exposed for tests; production code goes through <see cref="UnloadDocumentRecord"/>.
    /// </summary>
    internal bool TryRemoveRecord(string key, DocumentViewModel document) =>
        TryRemoveRecord(key, document, out _);

    /// <summary>
    /// Removes the <see cref="_openedFiles"/> entry for <paramref name="key"/> only while it
    /// still holds <paramref name="document"/>, so a record that has since been replaced is
    /// left alone.
    /// </summary>
    private bool TryRemoveRecord(string key, DocumentViewModel document, [NotNullWhen(true)] out PdfDocumentRecord? record)
    {
        if (_openedFiles.TryGetValue(key, out PdfDocumentRecord? current) &&
            ReferenceEquals(current.Document, document) &&
            // Value-matching overload: fails if another thread swapped the record in between.
            _openedFiles.TryRemove(new KeyValuePair<string, PdfDocumentRecord>(key, current)))
        {
            record = current;
            return true;
        }

        record = null;
        return false;
    }

    /// <summary>
    /// Unloads the documents of a window that has closed. Without this they stay in
    /// <see cref="_openedFiles"/> with no window owning them, and every later attempt to open
    /// those files silently does nothing.
    /// </summary>
    private void OnDocumentsOrphaned(object? sender, IReadOnlyList<DocumentViewModel> documents)
    {
        // Raised on the UI thread while a window closes; the unload must not block it.
        DocumentViewModel[] orphaned = [.. documents];

        _ = Task.Run(async () =>
        {
            foreach (DocumentViewModel document in orphaned)
            {
                try
                {
                    await UnloadDocumentRecord(document);
                }
                catch (Exception ex)
                {
                    Debug.WriteExceptionToFile(ex);
                }
            }

            Helpers.RequestGcCollect();
        });
    }

    private async Task OpenLoadDocumentInternal(OpenDocumentRequest request, string? password, CancellationToken cancellationToken)
    {
        Debug.ThrowOnUiThread();

        IStorageFile? storageFile = request.File;

        if (storageFile is null)
        {
            // TODO - Log
            return;
        }

        // TODO - Look into Avalonia bookmark
        // string? id = await storageFile.SaveBookmarkAsync();

        // Check if file is already open
        if (_openedFiles.TryGetValue(storageFile.Path.LocalPath, out var doc))
        {
            OpenedFileState existing = await Dispatcher.UIThread.InvokeAsync(
                () => ShowExistingDocument(doc.Document));

            if (existing != OpenedFileState.Stale)
            {
                return;
            }

            // The record outlived its window, so the file is not really open. Drop it and fall
            // through to open the file fresh; returning here is what made reopening a closed
            // document silently do nothing.
            await UnloadDocumentRecord(doc.Document);
        }

        var scope = App.Current!.Services!.CreateAsyncScope();

        var document = scope.ServiceProvider.GetRequiredService<DocumentViewModel>();
        document.FileName = $"Opening '{Path.GetFileNameWithoutExtension(storageFile.Path.LocalPath)}'...";

        if (TryAddRecord(storageFile.Path.LocalPath, document, scope))
        {
            // Do not await just yet - We need the WaitOpenAsync() to be created but we also
            // want to add the document to PdfDocuments before opening it.
            var openDocTask = document.LoadDocument(storageFile, password, cancellationToken);

            // Captured, not re-resolved: opening is asynchronous, so by the time a failed open
            // unwinds below, the active window may be a different one.
            MainViewModel? targetViewModel = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                MainViewModel? target = ResolveOpenTarget(request.Target);
                if (target is null)
                {
                    return null;
                }

                target.PdfDocuments.Add(document);
                target.SelectedDocumentIndex = Math.Max(0, target.PdfDocuments.Count - 1);

                // From here on "no window owns this document" means its window closed, rather
                // than this open not having got that far yet.
                MarkShownInAWindow(document);
                return target;
            });

            if (targetViewModel is null)
            {
                // No window can show it, but the load is already running and nothing below will
                // await it. Observe its failure so it does not resurface as an unobserved task
                // exception; the scope teardown waits for the parse either way.
                ObserveFailure(openDocTask);

                TryRemoveRecord(storageFile.Path.LocalPath, document, out _);
                await scope.DisposeAsync();
                return;
            }

            var state = DocumentOpeningState.Error;
            try
            {
                state = await openDocTask;
            }
            catch (OperationCanceledException)
            {
                // Typically the owning window closed while the document was still loading:
                // disposing its DI scope cancels _mainCts, which cancels this load. That is a
                // normal user action, not a failure - do not log it or alarm the user below.
                state = DocumentOpeningState.Canceled;
            }
            catch (Exception ex)
            {
                Debug.WriteExceptionToFile(ex);
            }

            if (state == DocumentOpeningState.Success)
            {
                // Document opened successfully (we don't dispose the scope)
                return;
            }
            
            // Document is not valid (or opening failed). Take it back out of the UI and wait
            // for that to complete before disposing the scope below.
            MainViewModel? owner = await RevertFailedOpen(document, storageFile.Path.LocalPath);

            // Reported in the window that was actually holding the tab, not the active one and
            // not the one the open was headed for: a file dropped on an unfocused window failed
            // there, and a tab dragged mid-open failed in the window it was dragged into - so
            // that is where the user is looking. Falls back to the original target, which
            // DialogService in turn resolves to the active window if that window has closed.
            MainViewModel? notificationTarget = owner ?? targetViewModel;

            if (document.IsPasswordProtected && state == DocumentOpeningState.Password)
            {
                App.Messenger.Send(new ShowNotificationMessage(NotificationType.Error, "Critical error",
                    "Could not open password protected document.", notificationTarget));
            }
            else if (state != DocumentOpeningState.Canceled)
            {
                App.Messenger.Send(new ShowNotificationMessage(NotificationType.Error, "Critical error",
                    "Cannot load pages because something wrong happened while opening the document.", notificationTarget));
            }
        }

        // TODO - Log error
        await scope.DisposeAsync();
    }

    public void Dispose()
    {
        _windowRegistry.DocumentsOrphaned -= OnDocumentsOrphaned;
        _processingQueueCts.Cancel();
        _processingQueueCts.Dispose();
        App.Messenger.UnregisterAll(this);
    }
}
