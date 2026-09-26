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
using System.Collections.Generic;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Caly.Core.Controls;

/// <summary>
/// Items panel for <see cref="PageItemsControl"/>'s single-page view: realises exactly one page,
/// measured at its natural size.
/// </summary>
public sealed class SinglePageVirtualizingPanel : VirtualizingPanel
{
    private static readonly AttachedProperty<object?> RecycleKeyProperty =
        AvaloniaProperty.RegisterAttached<SinglePageVirtualizingPanel, Control, object?>("RecycleKey");

    private static readonly object s_itemIsItsOwnContainer = new();

    private Dictionary<object, Stack<Control>>? _recyclePool;
    private Control? _realized;
    private int _realizedIndex = -1;

    /// <summary>
    /// The page index to show. Kept separate from <see cref="_realizedIndex"/> so a request made
    /// before the pages have loaded survives until they do.
    /// </summary>
    private int _requestedIndex = -1;

    private bool _isInLayout;

    /// <summary>
    /// Index of the realised page. Starts at 0. -1 if none.
    /// </summary>
    public int RealizedIndex => _realizedIndex;

    protected override Size MeasureOverride(Size availableSize)
    {
        _isInLayout = true;
        try
        {
            var items = Items;

            if (_requestedIndex < 0)
            {
                _requestedIndex = GetInitialIndex();
            }

            int index = Math.Min(_requestedIndex, items.Count - 1);

            if (index != _realizedIndex)
            {
                if (_realized is not null)
                {
                    RecycleElement(_realized);
                    _realized = null;
                    _realizedIndex = -1;
                }

                if (index >= 0)
                {
                    _realized = GetOrCreateElement(items, index);
                    _realizedIndex = index;
                }
            }

            if (_realized is null)
            {
                return default;
            }

            _realized.Measure(availableSize);
            return _realized.DesiredSize;
        }
        finally
        {
            _isInLayout = false;
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _realized?.Arrange(new Rect(finalSize));
        return finalSize;
    }

    protected override Control? ScrollIntoView(int index)
    {
        if (_isInLayout || index < 0 || index >= Items.Count)
        {
            return null;
        }

        if (index != _requestedIndex)
        {
            _requestedIndex = index;
            InvalidateMeasure();
        }

        if (index != _realizedIndex)
        {
            // Realise the page now so callers (GoToPage) can read its bounds straight away.
            UpdateLayout();
        }

        if (_realizedIndex != index)
        {
            return null;
        }

        _realized!.BringIntoView();
        return _realized;
    }

    protected override Control? ContainerFromIndex(int index)
    {
        return index == _realizedIndex ? _realized : null;
    }

    protected override int IndexFromContainer(Control container)
    {
        return container == _realized ? _realizedIndex : -1;
    }

    protected override IEnumerable<Control>? GetRealizedContainers()
    {
        return _realized is null ? null : [_realized];
    }

    protected override IInputElement? GetControl(NavigationDirection direction, IInputElement? from, bool wrap) => null;

    protected override void OnItemsChanged(IReadOnlyList<object?> items, NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(items, e);

        // Pages are only ever appended while a document loads. Any other change re-realises the
        // same index (clamped in MeasureOverride), which keeps the reader's place in the document.
        bool appendedAfterRealized = e.Action == NotifyCollectionChangedAction.Add &&
                                     e.NewStartingIndex > _realizedIndex;

        if (!appendedAfterRealized && _realized is not null)
        {
            RecycleElement(_realized);
            _realized = null;
            _realizedIndex = -1;
        }

        InvalidateMeasure();
    }

    private int GetInitialIndex()
    {
        return ItemsControl is PageItemsControl { SelectedPageNumber: int p } && p > 0 ? p - 1 : 0;
    }

    private Control GetOrCreateElement(IReadOnlyList<object?> items, int index)
    {
        var item = items[index];
        var generator = ItemContainerGenerator!;

        if (generator.NeedsContainer(item, index, out var recycleKey))
        {
            return GetRecycledElement(item, index, recycleKey) ?? CreateElement(item, index, recycleKey);
        }

        return GetItemAsOwnContainer(item, index);
    }

    private Control GetItemAsOwnContainer(object? item, int index)
    {
        var controlItem = (Control)item!;
        var generator = ItemContainerGenerator!;

        if (!controlItem.IsSet(RecycleKeyProperty))
        {
            generator.PrepareItemContainer(controlItem, controlItem, index);
            AddInternalChild(controlItem);
            controlItem.SetValue(RecycleKeyProperty, s_itemIsItsOwnContainer);
            generator.ItemContainerPrepared(controlItem, item, index);
        }

        controlItem.SetCurrentValue(IsVisibleProperty, true);
        return controlItem;
    }

    private Control? GetRecycledElement(object? item, int index, object? recycleKey)
    {
        if (recycleKey is null ||
            _recyclePool?.TryGetValue(recycleKey, out var pool) != true ||
            pool!.Count == 0)
        {
            return null;
        }

        var generator = ItemContainerGenerator!;
        var recycled = pool.Pop();
        recycled.SetCurrentValue(IsVisibleProperty, true);
        generator.PrepareItemContainer(recycled, item, index);
        generator.ItemContainerPrepared(recycled, item, index);
        return recycled;
    }

    private Control CreateElement(object? item, int index, object? recycleKey)
    {
        var generator = ItemContainerGenerator!;
        var container = generator.CreateContainer(item, index, recycleKey);

        container.SetValue(RecycleKeyProperty, recycleKey);
        generator.PrepareItemContainer(container, item, index);
        AddInternalChild(container);
        generator.ItemContainerPrepared(container, item, index);

        return container;
    }

    /// <summary>
    /// Same policy as <see cref="VirtualizingStackPanel"/>: recycled containers stay attached but
    /// hidden, ready to be reused for the next page.
    /// </summary>
    private void RecycleElement(Control element)
    {
        var recycleKey = element.GetValue(RecycleKeyProperty);

        if (recycleKey is null)
        {
            RemoveInternalChild(element);
            return;
        }

        if (recycleKey == s_itemIsItsOwnContainer)
        {
            element.SetCurrentValue(IsVisibleProperty, false);
            return;
        }

        ItemContainerGenerator!.ClearItemContainer(element);

        _recyclePool ??= new Dictionary<object, Stack<Control>>();
        if (!_recyclePool.TryGetValue(recycleKey, out var pool))
        {
            pool = new Stack<Control>();
            _recyclePool.Add(recycleKey, pool);
        }

        pool.Push(element);
        element.SetCurrentValue(IsVisibleProperty, false);
    }
}
