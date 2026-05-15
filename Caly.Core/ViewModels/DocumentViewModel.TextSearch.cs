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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Caly.Core.Models;
using Caly.Core.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Caly.Core.ViewModels;

public partial class DocumentViewModel
{
    private readonly Lazy<Task> _buildSearchIndex;
    private Task? _pendingSearchTask;
    private CancellationTokenSource? _pendingSearchTaskCts;

    private bool _isSearchQueryError;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(BuildingIndex))]
    private int _buildIndexProgress;

    public bool BuildingIndex => BuildIndexProgress != 0 && BuildIndexProgress != 100;

    [ObservableProperty] private string _searchStatus = string.Empty;

    [ObservableProperty] private string? _textSearch;

    [ObservableProperty]
    private SortedObservableCollection<TextSearchResult> _searchResults = new(m => m.PageNumber);

    [ObservableProperty] private HierarchicalTreeDataGridSource<TextSearchResult> _searchResultsSource;

    [ObservableProperty] private TextSearchResult? _selectedTextSearchResult;

    partial void OnTextSearchChanged(string? value)
    {
        SearchTextCommand.Execute(null);
    }

    [RelayCommand]
    private void ActivateSearchTextTab()
    {
        IsDocumentPaneOpen = true;
        SelectedTabIndex = 2;
    }

    private async Task BuildSearchIndex()
    {
        _mainToken.ThrowIfCancellationRequested();
        var progress = new Progress<int>(done =>
        {
            BuildIndexProgress = (int)Math.Ceiling((done / (double)PageCount) * 100);
        });

        await Task.Run(() => _textSearchService.BuildPdfDocumentIndex(progress, _mainToken), _mainToken)
            .ConfigureAwait(false);

        SetSearchStatusFinal();
    }

    /// <summary>
    /// Search the document.
    /// <para>
    /// Takes care of cancelling any search task currently running before starting the new one.
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task SearchText()
    {
        // https://stackoverflow.com/questions/18999827/a-pattern-for-self-cancelling-and-restarting-task

        try
        {
            var previousCts = _pendingSearchTaskCts;
            var newCts = CancellationTokenSource.CreateLinkedTokenSource(_mainToken);
            _pendingSearchTaskCts = newCts;

            if (previousCts is not null)
            {
                // cancel the previous session and wait for its termination
                System.Diagnostics.Debug.WriteLine("cancel the previous session and wait for its termination");
                await previousCts.CancelAsync();
                try
                {
                    if (_pendingSearchTask is null)
                    {
                        throw new Exception("No existing pending search task.");
                    }

                    await _pendingSearchTask;
                }
                catch (OperationCanceledException e)
                {
                    System.Diagnostics.Debug.WriteLine(e);
                    throw;
                }
                catch
                {
                    /* Ignore */
                }
                finally
                {
                    previousCts.Dispose();
                }
            }

            newCts.Token.ThrowIfCancellationRequested();
            _pendingSearchTask = SearchTextInternal(newCts.Token);
            await _pendingSearchTask;
        }
        catch (OperationCanceledException)
        {
        }
    }

    internal void SetSearchStatus(string status)
    {
        Dispatcher.UIThread.Invoke(() => { SearchStatus = status; });
    }

    private void SetSearchStatusFinal()
    {
        if (string.IsNullOrEmpty(TextSearch))
        {
            SetSearchStatus("");
        }
        else if (SearchResults.Count == 0)
        {
            if (!_isSearchQueryError)
            {
                SetSearchStatus("No Result Found");
            }
        }
        else
        {
            SetSearchStatus("");
        }
    }

    private async Task SearchTextInternal(CancellationToken token)
    {
        Debug.ThrowNotOnUiThread(); // TODO - Check if better not to be on UI thread

        try
        {
            ActivateSearchTextTab();
            SelectedTextSearchResult = null;
            SearchResults.Clear();

            Task indexBuildTask = _buildSearchIndex.Value;

            if (string.IsNullOrEmpty(TextSearch))
            {
                SetSearchStatus("");
                return;
            }

            Task searchTask = Task.Run(async () =>
            {
                Debug.ThrowOnUiThread();

                SetSearchStatus("Searching...");

                bool indexBuildTaskComplete;
                var pagesDone = new HashSet<int>();
                do
                {
                    token.ThrowIfCancellationRequested();
                    indexBuildTaskComplete = indexBuildTask.IsCompleted;
                    var searchResults = _textSearchService.Search(TextSearch, pagesDone, token);

                    foreach (var result in searchResults)
                    {
                        token.ThrowIfCancellationRequested();
                        if (result.PageNumber == -1)
                        {
                            break;
                        }

                        if (pagesDone.Contains(result.PageNumber))
                        {
                            continue;
                        }

                        await Dispatcher.UIThread.InvokeAsync(() => SearchResults.AddSorted(result));
                        pagesDone.Add(result.PageNumber);
                    }

                    await Task.Delay(indexBuildTaskComplete ? 0 : 500, token);
                } while (!indexBuildTaskComplete);
            }, token);

            if (!indexBuildTask.IsCompleted)
            {
                await Task.WhenAny(indexBuildTask, searchTask);
                if (indexBuildTask is { IsCompleted: true, Exception: not null })
                {
                    throw new Exception("Something wrong happened while indexing the document.",
                        indexBuildTask.Exception);
                }
            }
            else
            {
                await searchTask;
            }

            _isSearchQueryError = searchTask is { IsCompleted: true, Exception: not null };

            if (_isSearchQueryError)
            {
                SetSearchStatus(string.Join(' ', searchTask.Exception!.InnerExceptions.Select(e => e.Message)));
            }

            SetSearchStatusFinal();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteExceptionToFile(ex);
            Dispatcher.UIThread.Post(() => Exception = new ExceptionViewModel(ex));
        }
    }

    private void TextSearchSelectionChanged(object? sender,
        Avalonia.Controls.Selection.TreeSelectionModelSelectionChangedEventArgs<TextSearchResult> e)
    {
        if (e.SelectedItems.Count == 0)
        {
            return;
        }

        SelectedTextSearchResult = e.SelectedItems[0];
    }
}