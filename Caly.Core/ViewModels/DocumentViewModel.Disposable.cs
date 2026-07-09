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

using System;
using System.Linq;
using System.Threading.Tasks;

namespace Caly.Core.ViewModels;

public partial class DocumentViewModel : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        Debug.ThrowOnUiThread();

        await _mainCts.CancelAsync();

        Parallel.ForEach(Pages.Chunk(100),
            new ParallelOptions() { MaxDegreeOfParallelism = 4 },
            (pages, _) =>
            {
                foreach (var page in pages)
                {
                    page.Dispose();
                }
            });

        Pages.Clear();
        
        _searchResultsDisposable.Dispose();

        if (SearchResultsSource?.RowSelection is not null)
        {
            SearchResultsSource.RowSelection.SelectionChanged -= TextSearchSelectionChanged;
        }

        try
        {
            var bookmarks = await BookmarksSource;
            if (bookmarks?.RowSelection is not null)
            {
                bookmarks.RowSelection.SelectionChanged -= BookmarksSelectionChanged;
            }
        }
        catch (OperationCanceledException)
        { /* No op */ }
        catch (Exception e)
        {
            Debug.WriteExceptionToFile(e);
        }

        /*
         Do we want to await the following?
         - await EmbeddedFiles;
         - Properties
        */

        SearchResults.Clear();

        _mainCts.Dispose();
        _pendingSearchTaskCts?.Dispose();
    }
}