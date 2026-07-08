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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Caly.Core.Controls;

/// <summary>
/// Shared visibility-tracking machinery for the virtualized items controls
/// (<see cref="PageItemsControl"/> and <see cref="ThumbnailItemsControl"/>):
/// debounced visibility updates, realized-index range queries against the
/// <see cref="VirtualizingStackPanel"/>, and the realized-containers visibility
/// workaround for https://github.com/CalyPdf/Caly/issues/11.
/// </summary>
internal sealed class VirtualizedVisibilityTracker
{
    private readonly ItemsControl _owner;
    private readonly Action _updateVisibility;

    private bool _isUpdateScheduled;

    /// <param name="owner">The virtualized items control being tracked.</param>
    /// <param name="updateVisibility">The owner's visibility-update pass, invoked by
    /// <see cref="PostUpdateVisibility"/> on the UI thread at Loaded priority.</param>
    public VirtualizedVisibilityTracker(ItemsControl owner, Action updateVisibility)
    {
        ArgumentNullException.ThrowIfNull(owner, nameof(owner));
        ArgumentNullException.ThrowIfNull(updateVisibility, nameof(updateVisibility));
        _owner = owner;
        _updateVisibility = updateVisibility;
    }

    /// <summary>
    /// Schedules a visibility update on the UI thread at Loaded priority, coalescing
    /// requests so bursts of scroll/size events produce a single update.
    /// </summary>
    public void PostUpdateVisibility()
    {
        if (_isUpdateScheduled)
        {
            return;
        }

        _isUpdateScheduled = true;
        Dispatcher.UIThread.Post(() =>
        {
            _isUpdateScheduled = false;
            _updateVisibility();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Clears the pending-update flag, e.g. when the owner's DataContext changes.
    /// An already-queued update still runs.
    /// </summary>
    public void Reset()
    {
        _isUpdateScheduled = false;
    }

    /// <summary>
    /// First realized item index. Starts at 0. Inclusive.
    /// </summary>
    public int GetFirstRealizedIndex()
    {
        if (_owner.ItemsPanelRoot is VirtualizingStackPanel v)
        {
            return v.FirstRealizedIndex;
        }

        return 0;
    }

    /// <summary>
    /// Last realized item index. Starts at 0. Exclusive.
    /// <para>-1 if none realized.</para>
    /// </summary>
    public int GetLastRealizedIndex()
    {
        if (_owner.ItemsPanelRoot is VirtualizingStackPanel v)
        {
            if (v.LastRealizedIndex == -1)
            {
                return -1;
            }

            return Math.Min(_owner.ItemCount, v.LastRealizedIndex + 1);
        }

        return _owner.ItemCount;
    }

    public bool HasRealizedItems()
    {
        return _owner.ItemsPanelRoot is VirtualizingStackPanel vsp &&
               vsp.FirstRealizedIndex != -1 && vsp.LastRealizedIndex != -1;
    }

    /// <summary>
    /// Hides panel children that are still visible but no longer realized.
    /// This is a hack to ensure only valid containers (realised) are visible.
    /// See https://github.com/CalyPdf/Caly/issues/11
    /// </summary>
    public void EnsureValidContainersVisibility()
    {
        if (_owner.ItemsPanelRoot is null)
        {
            return;
        }

        var realised = _owner.GetRealizedContainers();
        var visibleChildren = _owner.ItemsPanelRoot.Children.Where(c => c.IsVisible);

        foreach (var child in visibleChildren.Except(realised))
        {
            child.SetCurrentValue(Visual.IsVisibleProperty, false);
        }
    }
}
