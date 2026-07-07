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

using Avalonia.Collections;
using Caly.Core.Services;
using Caly.Core.Services.Interfaces;
using Caly.Core.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Tabalonia.Controls;

namespace Caly.Core.ViewModels;

public sealed partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly IDisposable _documentCollectionDisposable;

    public ObservableCollection<DocumentViewModel> PdfDocuments { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedDocument))]
    private int _selectedDocumentIndex;

    [ObservableProperty] private bool _isSettingsPaneOpen;

    /// <summary>
    /// App-level state of the document side pane, shared across all documents.
    /// </summary>
    public DocumentPaneState Pane { get; }

    public DocumentViewModel? SelectedDocument
    {
        get
        {
            // Explicit range check instead of try/catch: the index is transiently
            // out of range while PdfDocumentsManagerService removes a document
            // before correcting SelectedDocumentIndex on the next line.
            int index = SelectedDocumentIndex;
            return index >= 0 && index < PdfDocuments.Count ? PdfDocuments[index] : null;
        }
    }

    public string Version => CalyExtensions.CalyVersion;

    /// <summary>
    /// The document the last <see cref="SelectedDocumentChangedMessage"/> was sent for,
    /// so switching between index/collection updates that resolve to the same document
    /// does not re-announce it.
    /// </summary>
    private DocumentViewModel? _lastAnnouncedSelectedDocument;

    private bool _isAnnounceSelectedDocumentScheduled;

    partial void OnSelectedDocumentIndexChanged(int value)
    {
        ScheduleAnnounceSelectedDocumentChanged();
    }

    /// <summary>
    /// Schedules a single <see cref="AnnounceSelectedDocumentChanged"/> on the UI thread.
    /// Index and collection changes come in non-atomic bursts (e.g.
    /// PdfDocumentsManagerService removes a document and only then corrects
    /// <see cref="SelectedDocumentIndex"/>; Tabalonia reorders tabs with Remove + Add),
    /// so announcing synchronously would observe transient states and activate the
    /// wrong document. Deferring to a posted callback coalesces the burst and runs
    /// once the state is settled.
    /// </summary>
    private void ScheduleAnnounceSelectedDocumentChanged()
    {
        if (_isAnnounceSelectedDocumentScheduled)
        {
            return;
        }

        _isAnnounceSelectedDocumentScheduled = true;
        Dispatcher.UIThread.Post(() =>
        {
            _isAnnounceSelectedDocumentScheduled = false;
            AnnounceSelectedDocumentChanged();
        });
    }

    /// <summary>
    /// Sends <see cref="SelectedDocumentChangedMessage"/> when the effective
    /// <see cref="SelectedDocument"/> instance changed. Triggered by index changes and
    /// collection changes, because the selected document can change without the index
    /// moving (first document added at index 0, or the selected document being removed).
    /// </summary>
    private void AnnounceSelectedDocumentChanged()
    {
        DocumentViewModel? selected = SelectedDocument;
        if (ReferenceEquals(_lastAnnouncedSelectedDocument, selected))
        {
            return;
        }

        _lastAnnouncedSelectedDocument = selected;
        if (selected is not null)
        {
            App.Messenger.Send(new SelectedDocumentChangedMessage(selected));
        }
    }

    /// <summary>
    /// Design-time constructor.
    /// </summary>
    public MainViewModel() : this(new DocumentPaneState())
    {
    }

    public MainViewModel(DocumentPaneState paneState)
    {
        ArgumentNullException.ThrowIfNull(paneState, nameof(paneState));
        Pane = paneState;

        // Documents are added/removed on the UI thread; the selected document can
        // change on collection changes without SelectedDocumentIndex changing.
        PdfDocuments.CollectionChanged += (_, _) => ScheduleAnnounceSelectedDocumentChanged();

        _documentCollectionDisposable = PdfDocuments
            .GetWeakCollectionChangedObservable()
            .ObserveOn(Scheduler.Default)
            .Subscribe(async e =>
            {
                Debug.ThrowOnUiThread();

                // NB: Tabalonia uses a Remove + Add when moving tabs
                try
                {
                    if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems?.Count > 0)
                    {
                        foreach (var newDoc in e.NewItems.OfType<DocumentViewModel>())
                        {
                            if (newDoc.WaitOpenAsync is null)
                            {
                                throw new Exception("WaitOpenAsync is null");
                            }

                            await newDoc.WaitOpenAsync; // Make sure the doc is open before proceeding
                            await newDoc.LoadPagesTask;
                        }
                    }
                    else if (e.Action == NotifyCollectionChangedAction.Remove)
                    {
                        if (PdfDocuments.Count == 0)
                        {
                            // We want to clear any possible reference to the last PdfDocumentViewModel.
                            // The collection keeps a reference of the last document in e.OldItems
                            // We trigger a NotifyCollectionChangedAction.Reset to flush.
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                if (PdfDocuments.Count == 0)
                                {
                                    PdfDocuments.Clear();
                                }
                            });
                        }
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
    }

    public void Dispose()
    {
        _documentCollectionDisposable.Dispose();
    }
    
    [RelayCommand]
    private async Task OpenFile(CancellationToken token)
    {
        try
        {
            var pdfDocumentsService = App.Current?.Services?.GetRequiredService<IPdfDocumentsManagerService>();
            if (pdfDocumentsService is null)
            {
                throw new NullReferenceException($"Missing {nameof(IPdfDocumentsManagerService)} instance.");
            }

            await pdfDocumentsService.OpenLoadDocument(token);
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
    }

    [RelayCommand]
    private async Task CloseTab(object tabItem, CancellationToken token)
    {
        // TODO - Finish proper dispose / unload of document on close 
        if (((DragTabItem)tabItem)?.DataContext is DocumentViewModel vm)
        {
            await CloseDocumentInternal(vm, token);
        }
    }

    [RelayCommand]
    private async Task CloseDocument(CancellationToken token)
    {
        DocumentViewModel? vm = SelectedDocument;
        if (vm is null)
        {
            return;
        }

        await CloseDocumentInternal(vm, token);
    }

    private static async Task CloseDocumentInternal(DocumentViewModel vm, CancellationToken token)
    {
        var pdfDocumentsService = App.Current?.Services?.GetRequiredService<IPdfDocumentsManagerService>()!;
        await Task.Run(() => pdfDocumentsService.CloseUnloadDocument(vm), token);
    }

    [RelayCommand]
    private Task PrintDocument(CancellationToken token)
    {
        DocumentViewModel? vm = SelectedDocument;
        if (vm is null)
        {
            return Task.CompletedTask;
        }

        return vm.PrintCommand.ExecuteAsync(token);
    }

    [RelayCommand]
    private void ActivateSearchTextTab()
    {
        Pane.IsDocumentPaneOpen = true;
        SelectedDocument?.SelectedTabIndex = 2;
    }

    [RelayCommand]
    private Task CopyText(CancellationToken token)
    {
        DocumentViewModel? vm = SelectedDocument;
        return vm is null ? Task.CompletedTask : vm.CopyTextCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void ActivateNextDocument()
    {
        int lastIndex = PdfDocuments.Count - 1;

        if (lastIndex <= 0)
        {
            return;
        }

        int newIndex = SelectedDocumentIndex + 1;

        if (newIndex > lastIndex)
        {
            newIndex = 0;
        }

        SelectedDocumentIndex = newIndex;
    }

    [RelayCommand]
    private void ActivatePreviousDocument()
    {
        int lastIndex = PdfDocuments.Count - 1;

        if (lastIndex <= 0)
        {
            return;
        }

        int newIndex = SelectedDocumentIndex - 1;

        if (newIndex < 0)
        {
            newIndex = lastIndex;
        }

        SelectedDocumentIndex = newIndex;
    }
}