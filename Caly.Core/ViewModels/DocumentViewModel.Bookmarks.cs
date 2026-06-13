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
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Media;
using Avalonia.Threading;
using Caly.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Caly.Core.ViewModels;

public partial class DocumentViewModel
{
    private readonly Lazy<Task<HierarchicalTreeDataGridSource<PdfBookmarkNode>?>> _bookmarksTask;
    public Task<HierarchicalTreeDataGridSource<PdfBookmarkNode>?> BookmarksSource => _bookmarksTask.Value;

    private FrozenDictionary<int, IReadOnlyList<PdfBookmarkLocation>>? _bookmarkLocations;

    private HierarchicalTreeDataGridSource<PdfBookmarkNode>? _bookmarksSource;

    private bool _isSyncingBookmarkFromScroll;

    private bool _activeBookmarkUpdateQueued;

    [ObservableProperty] public partial PdfBookmarkNode? SelectedBookmark { get; set; }

    private async Task<HierarchicalTreeDataGridSource<PdfBookmarkNode>?> GetBookmarks()
    {
        try
        {
            _mainToken.ThrowIfCancellationRequested();

            var bookmarks = await Task.Run(() => _pdfService.GetPdfBookmark(_mainToken), _mainToken) ?? [];
            if (bookmarks.Count > 0)
            {
                _bookmarksSource = new HierarchicalTreeDataGridSource<PdfBookmarkNode>(bookmarks)
                {
                    Columns =
                    {
                        new HierarchicalExpanderColumn<PdfBookmarkNode>(
                            new TextColumn<PdfBookmarkNode, string>(null,
                                x => x.Title, options: new TextColumnOptions<PdfBookmarkNode>()
                                {
                                    CanUserSortColumn = false,
                                    IsTextSearchEnabled = false,
                                    TextWrapping = TextWrapping.WrapWithOverflow,
                                    TextAlignment = TextAlignment.Left,
                                    MaxWidth = new GridLength(400)
                                }), x => x.Nodes)
                    }
                };
                _bookmarksSource.RowSelection!.SingleSelect = true;
                _bookmarksSource.RowSelection.SelectionChanged += BookmarksSelectionChanged;
                _bookmarksSource.ExpandAll();

                _bookmarkLocations = FlattenBookmarks(bookmarks);

                UpdateActiveBookmark();

                return _bookmarksSource;
            }
        }
        catch (OperationCanceledException)
        { /* No op */ }

        return null;
    }

    private void BookmarksSelectionChanged(object? sender, Avalonia.Controls.Selection.TreeSelectionModelSelectionChangedEventArgs<PdfBookmarkNode> e)
    {
        if (_isSyncingBookmarkFromScroll)
        {
            return;
        }

        if (e.SelectedItems.Count == 0)
        {
            return;
        }

        SelectedBookmark = e.SelectedItems[0];
    }

    partial void OnScrollOffsetChanged(Vector value)
    {
        QueueActiveBookmarkUpdate();
    }

    private void QueueActiveBookmarkUpdate()
    {
        if (_activeBookmarkUpdateQueued)
        {
            return;
        }

        _activeBookmarkUpdateQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _activeBookmarkUpdateQueued = false;
            UpdateActiveBookmark();
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Highlights the bookmark matching the current viewport, without triggering navigation.
    /// No-op until bookmarks have been loaded.
    /// </summary>
    private void UpdateActiveBookmark()
    {
        var source = _bookmarksSource;
        var locations = _bookmarkLocations;
        var selectedPageNumber = SelectedPageNumber;
        var pages = Pages;
        if (!selectedPageNumber.HasValue ||
            source?.RowSelection is null ||
            locations is null ||
            locations.Count == 0)
        {
            return;
        }

        int activePage = selectedPageNumber.Value;
        if (activePage < 1 || activePage > pages.Count)
        {
            return;
        }

        var currentPath = source.RowSelection.SelectedIndex;

        double offsetY = ScrollOffset.Y;

        // A negative offset means the viewport top is above the selected page's top (sits inside the previous page)
        if (offsetY < 0 && activePage > 1)
        {
            activePage--;
            offsetY += pages[activePage - 1].DisplayHeight;
        }

        PdfBookmarkLocation? active = null;
        if (locations.TryGetValue(activePage, out var pageLocations))
        {
            active = SelectClosestOnPage(pages[activePage - 1], pageLocations, offsetY);
        }
        else
        {
            // No bookmark on the current page: fall back to the last bookmark (in reading order)
            // on the nearest previous page, since the viewport has scrolled past all of them.
            for (int p = activePage - 1; p >= 1; --p)
            {
                if (locations.TryGetValue(p, out var prevLocations))
                {
                    active = prevLocations[^1];
                    break;
                }
            }
        }

        if (active is null || currentPath == active.Path)
        {
            return;
        }

        _isSyncingBookmarkFromScroll = true;
        try
        {
            source.RowSelection.Select(active.Path);
        }
        finally
        {
            _isSyncingBookmarkFromScroll = false;
        }

        return;

        static PdfBookmarkLocation? SelectClosestOnPage(PageViewModel page, IReadOnlyList<PdfBookmarkLocation> pageLocations, double offsetY)
        {
            if (pageLocations.Count == 1)
            {
                return pageLocations[0];
            }

            // A 90 or 270 rotation lays the bookmarks out along the horizontal axis, for which we have no
            // X offset, so fall back to the first (top) bookmark on the page.
            if (!page.IsPortrait)
            {
                return pageLocations[0];
            }

            double height = page.Size.Height;

            PdfBookmarkLocation? best = null;
            double minDist = double.MaxValue;
            foreach (var loc in pageLocations)
            {
                double offset = loc.Node.OffsetY ?? 0;
                double target = page.Rotation == 180 ? offset : height - offset;
                double dist = Math.Abs(offsetY - target);
                if (dist < minDist)
                {
                    minDist = dist;
                    best = loc;
                }
            }

            return best;
        }
    }

    private static FrozenDictionary<int, IReadOnlyList<PdfBookmarkLocation>> FlattenBookmarks(IReadOnlyList<PdfBookmarkNode> roots)
    {
        var list = new List<PdfBookmarkLocation>();

        void Recurse(IReadOnlyList<PdfBookmarkNode> nodes, IndexPath parent)
        {
            for (int i = 0; i < nodes.Count; ++i)
            {
                var node = nodes[i];
                var path = parent.Append(i);

                if (node.PageNumber.HasValue)
                {
                    list.Add(new PdfBookmarkLocation(node, path));
                }

                if (node.Nodes is { Count: > 0 } children)
                {
                    Recurse(children, path);
                }
            }
        }

        Recurse(roots, default);
        return list.GroupBy(x => x.Node.PageNumber!.Value)
            .ToFrozenDictionary(g => g.Key,
                IReadOnlyList<PdfBookmarkLocation> (g) => g.ToArray()); ;
    }
}
