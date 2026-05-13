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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Caly.Core.Utilities;
using System;
using System.Linq;
using System.Windows.Input;
using Avalonia.Controls.Presenters;

namespace Caly.Core.Controls;

public sealed class ThumbnailItemsControl : ListBox
{
    private bool _isScrollingToPage;
    private bool _isUpdateThumbnailsVisibilityScheduled;

    private ScrollViewer? _scrollViewer;

    private readonly EventHandler<ScrollChangedEventArgs> _scrollChangedHandler;
    private readonly EventHandler<SizeChangedEventArgs> _sizeChangedHandler;
    private readonly EventHandler<RoutedEventArgs> _loadedHandler;

    protected override Type StyleKeyOverride => typeof(ListBox);

    /// <summary>
    /// Defines the <see cref="RealisedThumbnails"/> property. Starts at 1.
    /// </summary>
    public static readonly StyledProperty<Range?> RealisedThumbnailsProperty =
        AvaloniaProperty.Register<ThumbnailItemsControl, Range?>(nameof(RealisedThumbnails), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Defines the <see cref="VisibleThumbnails"/> property. Starts at 1.
    /// </summary>
    public static readonly StyledProperty<Range?> VisibleThumbnailsProperty =
        AvaloniaProperty.Register<ThumbnailItemsControl, Range?>(nameof(VisibleThumbnails), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Defines the <see cref="RefreshThumbnails"/> property.
    /// </summary>
    public static readonly StyledProperty<ICommand?> RefreshThumbnailsProperty =
        AvaloniaProperty.Register<ThumbnailItemsControl, ICommand?>(nameof(RefreshThumbnails));

    public ICommand? RefreshThumbnails
    {
        get => GetValue(RefreshThumbnailsProperty);
        set => SetValue(RefreshThumbnailsProperty, value);
    }

    /// <summary>
    /// Starts at 1.
    /// </summary>
    public Range? RealisedThumbnails
    {
        get => GetValue(RealisedThumbnailsProperty);
        set => SetValue(RealisedThumbnailsProperty, value);
    }

    /// <summary>
    /// Starts at 1.
    /// </summary>
    public Range? VisibleThumbnails
    {
        get => GetValue(VisibleThumbnailsProperty);
        set => SetValue(VisibleThumbnailsProperty, value);
    }
    
    public ThumbnailItemsControl()
    {
        _scrollChangedHandler = (_, _) => PostUpdateThumbnailsVisibility();
        _sizeChangedHandler = (_, _) => PostUpdateThumbnailsVisibility();
        _loadedHandler = (_, _) => PostUpdateThumbnailsVisibility();
        ResetState();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _scrollViewer = Scroll as ScrollViewer ?? throw new Exception("Scroll is not ScrollViewer.");
        _scrollViewer.AddHandler(ScrollViewer.ScrollChangedEvent, _scrollChangedHandler);
        _scrollViewer.AddHandler(SizeChangedEvent, _sizeChangedHandler, RoutingStrategies.Direct);
        _scrollViewer.AddHandler(LoadedEvent, _loadedHandler);
    }

    protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromLogicalTree(e);

        if (_scrollViewer is not null)
        {
            _scrollViewer.RemoveHandler(ScrollViewer.ScrollChangedEvent, _scrollChangedHandler);
            _scrollViewer.RemoveHandler(SizeChangedEvent, _sizeChangedHandler);
            _scrollViewer.RemoveHandler(LoadedEvent, _loadedHandler);
        }
    }

    private void PostUpdateThumbnailsVisibility()
    {
        if (_isUpdateThumbnailsVisibilityScheduled)
        {
            return;
        }

        _isUpdateThumbnailsVisibilityScheduled = true;
        Dispatcher.UIThread.Post(() =>
        {
            _isUpdateThumbnailsVisibilityScheduled = false;
            UpdateThumbnailsVisibility();
        }, DispatcherPriority.Loaded);
    }

    private bool UpdateThumbnailsVisibility()
    {
        if (_scrollViewer is null)
        {
            return false;
        }

        if (_isScrollingToPage)
        {
            return false;
        }

        int firstRealisedIndex = GetMinPageIndex();
        int lastRealisedIndex = GetMaxPageIndex();

        if (firstRealisedIndex == -1 || lastRealisedIndex == -1)
        {
            SetCurrentValue(RealisedThumbnailsProperty, null);

            if (VisibleThumbnails.HasValue)
            {
                SetCurrentValue(VisibleThumbnailsProperty, null);
                RefreshThumbnails?.Execute(null);
            }
            
            return true;
        }

        bool previousVisible = false;
        int firstVisibleIndex = -1;
        int lastVisibleIndex = -1;
        for (int index = firstRealisedIndex; index < lastRealisedIndex; ++index)
        {
            if (ContainerFromIndex(index) is not ListBoxItem thumbnailItem)
            {
                continue;
            }

            // Check thumbnails visibility
            if (_scrollViewer.GetViewportRect().Intersects(thumbnailItem.Bounds))
            {
                // Visible
                if (!previousVisible)
                {
                    firstVisibleIndex = index;
                    lastVisibleIndex = index;
                    previousVisible = true;
                }
                else
                {
                    lastVisibleIndex = index;
                }
            }
            else
            {
                // Not visible
                if (previousVisible)
                {
                    break;
                }
            }
        }

        // Update bound properties
        SetCurrentValue(RealisedThumbnailsProperty, new Range(firstRealisedIndex + 1, lastRealisedIndex + 1));

        Range? currentVisibleThumbnails = null;
        if (firstVisibleIndex != -1 && lastVisibleIndex != -1) // No visible pages
        {
            currentVisibleThumbnails = new Range(firstVisibleIndex + 1, lastVisibleIndex + 2);
        }

        if (!Nullable.Equals(VisibleThumbnails, currentVisibleThumbnails))
        {
            SetCurrentValue(VisibleThumbnailsProperty, currentVisibleThumbnails);
            RefreshThumbnails?.Execute(null);
        }

        return true;
    }

    /// <summary>
    /// Starts at 0. Inclusive.
    /// </summary>
    private int GetMinPageIndex()
    {
        if (ItemsPanelRoot is VirtualizingStackPanel v)
        {
            return v.FirstRealizedIndex;
        }
        return 0;
    }

    /// <summary>
    /// Starts at 0. Exclusive.
    /// <para>-1 if not realised.</para>
    /// </summary>
    private int GetMaxPageIndex()
    {
        if (ItemsPanelRoot is VirtualizingStackPanel v)
        {
            if (v.LastRealizedIndex == -1)
            {
                return -1;
            }
            
            return Math.Min(ItemCount, v.LastRealizedIndex + 1);
        }

        return ItemCount;
    }
    

    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        base.PrepareContainerForItemOverride(container, item, index);

        if (container is not ListBoxItem)
        {
            return;
        }

        PostUpdateThumbnailsVisibility();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DataContextProperty)
        {
            ResetState();
            EnsureValidContainersVisibility();
        }
        else if (change.Property == IsVisibleProperty)
        {
            if (change is { OldValue: false, NewValue: true })
            {
                try
                {
                    _isScrollingToPage = true;
                    ScrollIntoView(SelectedIndex);
                }
                finally
                {
                    _isScrollingToPage = false;
                }

                EnsureValidContainersVisibility();
                PostUpdateThumbnailsVisibility();
            }
            else if (change is { OldValue: true, NewValue: false })
            {
                // Thumbnails control is hidden
                SetCurrentValue(RealisedThumbnailsProperty, null);
                SetCurrentValue(VisibleThumbnailsProperty, null);
                RefreshThumbnails?.Execute(null);
            }
        }
    }

    private void EnsureValidContainersVisibility()
    {
        // This is a hack to ensure only valid containers (realised) are visible
        // See https://github.com/CalyPdf/Caly/issues/11

        if (ItemsPanelRoot is null)
        {
            return;
        }

        var realised = GetRealizedContainers().OfType<ListBoxItem>();
        var visibleChildren = ItemsPanelRoot.Children.Where(c => c.IsVisible).OfType<ListBoxItem>();

        foreach (var child in visibleChildren.Except(realised))
        {
            child.SetCurrentValue(IsVisibleProperty, false);
        }
    }
    
    private void ResetState()
    {
        SetCurrentValue(RealisedThumbnailsProperty, null);
        SetCurrentValue(VisibleThumbnailsProperty, null);
        _isScrollingToPage = false;
        _isUpdateThumbnailsVisibilityScheduled = false;
    }
}
