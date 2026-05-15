// Copyright (c) 2025 BobLd
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

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Caly.Core.Services.Interfaces;
using Caly.Core.Utilities;
using Caly.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Caly.Core.Services;

internal sealed partial class PdfDocumentsManagerService : IPdfDocumentsManagerService, IDisposable
{
    private sealed class PdfDocumentRecord
    {
        public required AsyncServiceScope Scope { get; init; }

        public required DocumentViewModel Document { get; init; }
    }

    private readonly MainViewModel _mainViewModel;
    private readonly IFilesService _filesService;
    private readonly IDialogService _dialogService;
    private readonly IClipboardService _clipboardService;

    private readonly ChannelWriter<IStorageFile?> _channelWriter;
    private readonly ChannelReader<IStorageFile?> _channelReader;
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
                    if (d is not null)
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

    public PdfDocumentsManagerService(Visual target, IFilesService filesService, IDialogService dialogService, IClipboardService clipboardService)
    {
        Debug.ThrowNotOnUiThread();

        if (target.DataContext is not MainViewModel mvm)
        {
            throw new ArgumentException("Could not get a valid DataContext for the main window.");
        }

        _mainViewModel = mvm;

        _filesService = filesService ?? throw new NullReferenceException("Missing File Service instance.");
        _dialogService = dialogService ?? throw new NullReferenceException("Missing Dialog Service instance.");
        _clipboardService = clipboardService ?? throw new NullReferenceException("Missing clipboard Service instance.");

        Channel<IStorageFile?> fileChannel = Channel.CreateUnbounded<IStorageFile?>(new UnboundedChannelOptions()
        {
            AllowSynchronousContinuations = false,
            SingleReader = false,
            SingleWriter = false
        });

        _channelWriter = fileChannel.Writer;
        _channelReader = fileChannel.Reader;

        RegisterMessagesHandlers();

        _ = Task.Run(() => ProcessDocumentsQueue(_processingQueueCts.Token));
    }

    public async Task OpenLoadDocument(CancellationToken cancellationToken)
    {
        Debug.ThrowNotOnUiThread();

        IStorageFile? file = await _filesService.OpenPdfFileAsync();

        await Task.Run(() => OpenLoadDocument(file, cancellationToken), cancellationToken);
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

        await OpenLoadDocument(file, cancellationToken);
    }

    public async Task OpenLoadDocument(IStorageFile? storageFile, CancellationToken cancellationToken)
    {
        Debug.ThrowOnUiThread();

        await _channelWriter.WriteAsync(storageFile, cancellationToken);
    }

    public async Task<int> OpenLoadDocuments(IEnumerable<IStorageItem?> storageFiles, CancellationToken cancellationToken)
    {
        Debug.ThrowOnUiThread();

        int count = 0;
        foreach (IStorageItem? item in storageFiles)
        {
            if (item is not IStorageFile file)
            {
                continue;
            }

            await OpenLoadDocument(file, cancellationToken);
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

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _mainViewModel.PdfDocuments.Remove(document);
        });
        

        if (_openedFiles.TryRemove(document.LocalPath, out var docRecord))
        {
            await docRecord.Scope.DisposeAsync();
        }
        else
        {
            // TODO - Log error
        }

        // Attempt to collect garbage as much as possible
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
    }

    private async Task OpenLoadDocumentInternal(IStorageFile? storageFile, string? password, CancellationToken cancellationToken)
    {
        Debug.ThrowOnUiThread();

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
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                int index = _mainViewModel.PdfDocuments.IndexOf(doc.Document);
                if (index != -1 && _mainViewModel.SelectedDocumentIndex != index)
                {
                    _mainViewModel.SelectedDocumentIndex = index;
                }
            });

            return;
        }

        // We use a named mutex to ensure a single file with the same path is only opened once
        using (new Mutex(true, GetMutexName(storageFile.Path.LocalPath), out bool created))
        {
            if (!created)
            {
                // Already processing
                return;
            }

            var scope = App.Current!.Services!.CreateAsyncScope();

            var documentViewModel = scope.ServiceProvider.GetRequiredService<DocumentViewModel>();
            documentViewModel.FileName = $"Opening '{Path.GetFileNameWithoutExtension(storageFile.Path.LocalPath)}'...";

            var docRecord = new PdfDocumentRecord()
            {
                Scope = scope,
                Document = documentViewModel
            };

            if (_openedFiles.TryAdd(storageFile.Path.LocalPath, docRecord))
            {
                // Do not await just yet - We need the WaitOpenAsync() to be created but we also
                // want to add the document to PdfDocuments before opening it.
                Task<int> openDocTask = documentViewModel.OpenDocument(storageFile, password, cancellationToken);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _mainViewModel.PdfDocuments.Add(documentViewModel);
                    _mainViewModel.SelectedDocumentIndex = Math.Max(0, _mainViewModel.PdfDocuments.Count - 1);
                });

                int pageCount = 0;
                try
                {
                    pageCount = await openDocTask;
                }
                catch (Exception ex)
                {
                    Debug.WriteExceptionToFile(ex);
                    Dispatcher.UIThread.Post(() => _mainViewModel.PdfDocuments.Remove(documentViewModel));
                    _openedFiles.TryRemove(storageFile.Path.LocalPath, out _);
                }

                if (pageCount > 0)
                {
                    // Document opened successfully
                    return;
                }

                // Document is not valid
                Dispatcher.UIThread.Post(() => _mainViewModel.PdfDocuments.Remove(documentViewModel));
                _openedFiles.TryRemove(storageFile.Path.LocalPath, out _);
            }

            // TODO - Log error
            await scope.DisposeAsync();
        }
    }

    private static string GetMutexName(string path)
    {
        // The backslash character (\) is reserved for mutex names
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(path)).Replace('\\', '#');
    }

    public void Dispose()
    {
        _processingQueueCts.Cancel();
        _processingQueueCts.Dispose();
        App.Messenger.UnregisterAll(this);
    }
}
