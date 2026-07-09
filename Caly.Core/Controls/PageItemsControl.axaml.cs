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
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Caly.Core.Models;
using Caly.Core.Utilities;
using System;
using System.Windows.Input;
using Avalonia.Threading;

#if DEBUG
using System.Linq;
#endif

namespace Caly.Core.Controls;

/// <summary>
/// Control that displays the PDF document pages and owns the document state (pages, zoom, scroll, selection).
/// </summary>
[TemplatePart("PART_ScrollViewer", typeof(ScrollViewer))]
[TemplatePart("PART_LayoutTransformControl", typeof(LayoutTransformControl))]
public sealed class PageItemsControl : ItemsControl
{
    /// <summary>
    /// The default value for the <see cref="PageItemsControl.ItemsPanel"/> property.
    /// </summary>
    private static readonly FuncTemplate<Panel?> DefaultPanel = new(() => new VirtualizingStackPanel()
    {
        // On Windows desktop, 0 is enough
        // Need to test other platforms
        CacheLength = 0
    });

    /// <summary>
    /// Handles pointer input over the pages' interactive layers (text selection,
    /// annotations, links, hover feedback).
    /// </summary>
    private readonly TextSelectionInputHandler _textSelectionHandler;

    /// <summary>
    /// Shared visibility-tracking machinery (debounced updates, realized-range
    /// queries, container-visibility workaround).
    /// </summary>
    private readonly VirtualizedVisibilityTracker _visibilityTracker;

    /// <summary>
    /// Handles ctrl+wheel/pinch/external zoom and drag panning, owning the
    /// in-flight zoom/pan state.
    /// </summary>
    private readonly ZoomPanController _zoomPanController;

    private bool _isSettingPageVisibility;
    private bool _pendingScrollToPage;
    private bool _isApplyingPendingScroll;

    private readonly EventHandler<ScrollChangedEventArgs> _scrollChangedHandler;
    private readonly EventHandler<SizeChangedEventArgs> _sizeChangedHandler;

    /// <summary>
    /// Defines the <see cref="Scroll"/> property.
    /// </summary>
    public static readonly DirectProperty<PageItemsControl, ScrollViewer?> ScrollProperty =
        AvaloniaProperty.RegisterDirect<PageItemsControl, ScrollViewer?>(nameof(Scroll),
            o => o.Scroll);

    /// <summary>
    /// Defines the <see cref="LayoutTransform"/> property.
    /// </summary>
    public static readonly DirectProperty<PageItemsControl, LayoutTransformControl?> LayoutTransformControlProperty =
        AvaloniaProperty.RegisterDirect<PageItemsControl, LayoutTransformControl?>(nameof(LayoutTransform),
            o => o.LayoutTransform);

    /// <summary>
    /// Defines the <see cref="InteractiveActionOver"/> property. Starts at 1.
    /// </summary>
    public static readonly StyledProperty<string?> InteractiveActionOverProperty =
        AvaloniaProperty.Register<PageItemsControl, string?>(nameof(InteractiveActionOver),
            defaultBindingMode: BindingMode.OneWayToSource);

    /// <summary>
    /// Defines the <see cref="PageCount"/> property.
    /// </summary>
    public static readonly StyledProperty<int> PageCountProperty =
        AvaloniaProperty.Register<PageItemsControl, int>(nameof(PageCount));

    /// <summary>
    /// Defines the <see cref="SelectedPageNumber"/> property. Starts at 1.
    /// </summary>
    public static readonly StyledProperty<int?> SelectedPageNumberProperty =
        AvaloniaProperty.Register<PageItemsControl, int?>(nameof(SelectedPageNumber), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Defines the <see cref="MinZoomLevel"/> property.
    /// </summary>
    public static readonly StyledProperty<double> MinZoomLevelProperty =
        AvaloniaProperty.Register<PageItemsControl, double>(nameof(MinZoomLevel));

    /// <summary>
    /// Defines the <see cref="MaxZoomLevel"/> property.
    /// </summary>
    public static readonly StyledProperty<double> MaxZoomLevelProperty =
        AvaloniaProperty.Register<PageItemsControl, double>(nameof(MaxZoomLevel), 1);

    /// <summary>
    /// Defines the <see cref="ZoomLevel"/> property.
    /// </summary>
    public static readonly StyledProperty<double> ZoomLevelProperty =
        AvaloniaProperty.Register<PageItemsControl, double>(nameof(ZoomLevel), 1, defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Defines the <see cref="ScrollOffset"/> property.
    /// </summary>
    public static readonly StyledProperty<Vector> ScrollOffsetProperty =
        AvaloniaProperty.Register<PageItemsControl, Vector>(nameof(ScrollOffset),
            defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Defines the <see cref="TextSelection"/> property.
    /// </summary>
    public static readonly StyledProperty<TextSelection?> TextSelectionProperty =
        AvaloniaProperty.Register<PageItemsControl, TextSelection?>(nameof(TextSelection));

    /// <summary>
    /// Defines the <see cref="RealisedPages"/> property. Starts at 1.
    /// </summary>
    public static readonly StyledProperty<Range?> RealisedPagesProperty =
        AvaloniaProperty.Register<PageItemsControl, Range?>(nameof(RealisedPages), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Defines the <see cref="VisiblePages"/> property. Starts at 1.
    /// </summary>
    public static readonly StyledProperty<Range?> VisiblePagesProperty =
        AvaloniaProperty.Register<PageItemsControl, Range?>(nameof(VisiblePages), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<ICommand?> RefreshPagesProperty =
        AvaloniaProperty.Register<PageItemsControl, ICommand?>(nameof(RefreshPages));

    public static readonly StyledProperty<ICommand?> ClearSelectionProperty =
        AvaloniaProperty.Register<PageItemsControl, ICommand?>(nameof(ClearSelection));

    static PageItemsControl()
    {
        ItemsPanelProperty.OverrideDefaultValue<PageItemsControl>(DefaultPanel);
        KeyboardNavigation.TabNavigationProperty.OverrideDefaultValue(typeof(PageItemsControl),
            KeyboardNavigationMode.Once);
    }

    public ICommand? RefreshPages
    {
        get => GetValue(RefreshPagesProperty);
        set => SetValue(RefreshPagesProperty, value);
    }

    public TextSelection? TextSelection
    {
        get => GetValue(TextSelectionProperty);
        set => SetValue(TextSelectionProperty, value);
    }

    public ICommand? ClearSelection
    {
        get => GetValue(ClearSelectionProperty);
        set => SetValue(ClearSelectionProperty, value);
    }

    public PageItemsControl()
    {
        _textSelectionHandler = new TextSelectionInputHandler(this);
        _visibilityTracker = new VirtualizedVisibilityTracker(this, () => UpdatePagesVisibility());
        _zoomPanController = new ZoomPanController(this);
        _scrollChangedHandler = (_, e) =>
        {
            AdjustXOffsetOnExtentChanged(e);
            _visibilityTracker.PostUpdateVisibility();
        };
        _sizeChangedHandler = (_, _) => _visibilityTracker.PostUpdateVisibility();

        // Use a Tunnel handler to ensure zoom checks run before bubble-phase handlers
        // and avoid unwanted event scrolls by 50px before we can reject them.
        // No need to RemoveHandler() as it is on 'this', so it's GC'd with the control.
        AddHandler(PointerWheelChangedEvent, _zoomPanController.OnPointerWheelChanged, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnKeyDownHandler, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyUpEvent, OnKeyUpHandler, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, OnInteractiveLayerPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnInteractiveLayerPointerReleased, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnInteractiveLayerPointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerWheelChangedEvent, OnInteractiveLayerPointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PageItem.BeforeRotationEvent, OnBeforePageRotation);

        ResetState();
    }

    private void OnInteractiveLayerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Ensure the control gets focus as it seats below the PageInteractiveLayerControl,
        // and hence can never directly get focus on click
        Focus(NavigationMethod.Pointer);
        
        if (e.Source is PageInteractiveLayerControl layer)
        {
            _textSelectionHandler.OnPointerPressed(layer, e);
        }
    }

    private void OnInteractiveLayerPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Source is PageInteractiveLayerControl layer)
        {
            _textSelectionHandler.OnPointerReleased(layer, e);
        }
    }

    private void OnInteractiveLayerPointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.Source is PageInteractiveLayerControl layer)
        {
            _textSelectionHandler.OnPointerMoved(layer, e);
        }
    }

    /// <summary>
    /// Gets the scroll information for the <see cref="ListBox"/>.
    /// </summary>
    public ScrollViewer? Scroll
    {
        get;
        private set => SetAndRaise(ScrollProperty, ref field, value);
    }

    /// <summary>
    /// Gets the scroll information for the <see cref="ListBox"/>.
    /// </summary>
    public LayoutTransformControl? LayoutTransform
    {
        get;
        private set => SetAndRaise(LayoutTransformControlProperty, ref field, value);
    }

    public string? InteractiveActionOver
    {
        get => GetValue(InteractiveActionOverProperty);
        set => SetValue(InteractiveActionOverProperty, value);
    }
    public int PageCount
    {
        get => GetValue(PageCountProperty);
        set => SetValue(PageCountProperty, value);
    }

    /// <summary>
    /// Starts at 1.
    /// </summary>
    public int? SelectedPageNumber
    {
        get => GetValue(SelectedPageNumberProperty);
        set => SetValue(SelectedPageNumberProperty, value);
    }

    public double MinZoomLevel
    {
        get => GetValue(MinZoomLevelProperty);
        set => SetValue(MinZoomLevelProperty, value);
    }

    public double MaxZoomLevel
    {
        get => GetValue(MaxZoomLevelProperty);
        set => SetValue(MaxZoomLevelProperty, value);
    }

    public double ZoomLevel
    {
        get => GetValue(ZoomLevelProperty);
        set => SetValue(ZoomLevelProperty, value);
    }

    /// <summary>
    /// Scroll offset to persist across tab switches. Y is relative to
    /// <see cref="SelectedPageNumber"/>'s top; both components are in unscaled
    /// document coordinates.
    /// </summary>
    public Vector ScrollOffset
    {
        get => GetValue(ScrollOffsetProperty);
        set => SetValue(ScrollOffsetProperty, value);
    }

    /// <summary>
    /// Starts at 1.
    /// </summary>
    public Range? RealisedPages
    {
        get => GetValue(RealisedPagesProperty);
        set => SetValue(RealisedPagesProperty, value);
    }

    /// <summary>
    /// Starts at 1.
    /// </summary>
    public Range? VisiblePages
    {
        get => GetValue(VisiblePagesProperty);
        set => SetValue(VisiblePagesProperty, value);
    }

    /// <summary>
    /// Get the page control for the page number.
    /// </summary>
    /// <param name="pageNumber">The page number. Starts at 1.</param>
    /// <returns>The page control, or <c>null</c> if not found.</returns>
    public PageItem? GetPageItem(int pageNumber)
    {
        System.Diagnostics.Debug.WriteLine($"GetPageItem {pageNumber}.");
        if (ContainerFromIndex(pageNumber - 1) is PageItem presenter)
        {
            return presenter;
        }

        return null;
    }

    /// <summary>
    /// Scrolls to the page number, attempting to focus on the word.
    /// </summary>
    /// <param name="pageNumber">The page number.<para>Starts at 1.</para></param>
    /// <param name="wordIndex">The word index to focus on, if possible.</param>
    public void GoToWord(int pageNumber, int wordIndex)
    {
        double yOffset = 0; // Top of page

        var textLayer = GetPageItem(pageNumber)?.InteractiveLayer?.PdfTextLayer;
        if (textLayer is not null)
        {
            var word = textLayer[wordIndex];
            // NB: We are NOT in pdf coordinates, words y-axis is already inverted.
            yOffset = word.BoundingBox.Bottom;
        }

        // We don't attempt to get the text layer if it's not available
        GoToPage(pageNumber, yOffset);
    }

    /// <summary>
    /// Scrolls to the page number, optionally scrolling to a specific Y position within the page.
    /// </summary>
    /// <param name="pageNumber">The page number.<para>Starts at 1.</para></param>
    /// <param name="yOffset">Optional Y offset within the page.</param>
    /// <param name="offsetPdfCoord"><c>true</c> if the offset is in PDF coordinates (bottom = 0, increasing upward).
    /// <para><c>false</c> if the offset is in Avalonia coordinates (top = 0, increasing downward, unscaled pixels).</para>
    /// Default is <c>false</c>.
    /// </param>
    public void GoToPage(int pageNumber, double? yOffset = null, bool offsetPdfCoord = false)
    {
        if (_isSettingPageVisibility || pageNumber <= 0 || pageNumber > PageCount || ItemsView.Count == 0)
        {
            return;
        }

        ScrollIntoView(pageNumber - 1);
        if (yOffset.HasValue)
        {
            ApplyYOffset(pageNumber, yOffset.Value, offsetPdfCoord);
        }
    }

    private void ApplyYOffset(int pageNumber, double yOffset, bool offsetPdfCoord)
    {
        ApplyScrollOffsets(pageNumber, yOffset, offsetPdfCoord, xOffsetUnscaled: null);
    }

    /// <summary>
    /// Sets the scroll position to the given page, with an optional Y offset inside the page
    /// and an optional horizontal offset.
    /// </summary>
    /// <param name="pageNumber">The page number. Starts at 1.</param>
    /// <param name="yOffset">Y offset within the page.</param>
    /// <param name="offsetPdfCoord"><c>true</c> if <paramref name="yOffset"/> is in PDF coordinates
    /// (bottom = 0, increasing upward); <c>false</c> for Avalonia coordinates (top = 0, increasing downward, unscaled).</param>
    /// <param name="xOffsetUnscaled">Horizontal scroll offset in unscaled document coordinates,
    /// or <c>null</c> to keep the current horizontal offset.</param>
    private void ApplyScrollOffsets(int pageNumber, double yOffset, bool offsetPdfCoord, double? xOffsetUnscaled)
    {
        if (Scroll is null || LayoutTransform is null)
        {
            return;
        }

        if (ContainerFromIndex(pageNumber - 1) is not PageItem pageItem)
        {
            return;
        }

        if (yOffset > pageItem.Bounds.Height)
        {
            yOffset = pageItem.Bounds.Height; // Max offset is page height
        }

        if (offsetPdfCoord)
        {
            switch (pageItem.Rotation)
            {
                case 0:
                    // Upright: distance from the top edge.
                    yOffset = pageItem.Bounds.Height - yOffset;
                    break;
                case 180:
                    // The PDF bottom is now at the page top, so the offset is already the distance from the top.
                    break;
                default:
                    // 90 / 270: the offset maps to the horizontal axis, which we cannot honour. Scroll to the top.
                    yOffset = 0;
                    break;
            }
        }

        double scale = LayoutTransform.LayoutTransform?.Value.M11 ?? 1.0;
        double newOffsetY = (pageItem.Bounds.Top + yOffset) * scale;
        double newOffsetX = xOffsetUnscaled.HasValue
            ? Math.Max(0, xOffsetUnscaled.Value * scale)
            : Scroll.Offset.X;
        Scroll.SetCurrentValue(ScrollViewer.OffsetProperty, new Vector(newOffsetX, newOffsetY));
    }

    /// <summary>
    /// Gets the Y distance from the viewport top to the top of the currently selected page,
    /// in unscaled display coordinates (page top = 0, increasing downward).
    /// Returns <c>null</c> if the selected page is not realized or scroll state is unavailable.
    /// </summary>
    internal double? GetCurrentPageRelativeYOffset(int? pageNumber)
    {
        if (!pageNumber.HasValue || Scroll is null || LayoutTransform is null || !SelectedPageNumber.HasValue)
        {
            return null;
        }

        if (ContainerFromIndex(pageNumber.Value - 1) is not PageItem pageItem)
        {
            return null;
        }

        double scale = LayoutTransform.LayoutTransform?.Value.M11 ?? 1.0;
        double relativeOffset = (Scroll.Offset.Y / scale) - pageItem.Bounds.Top;
        return Math.Max(0, relativeOffset);
    }

    /// <summary>
    /// Persists the current scroll position to the <see cref="ScrollOffset"/> property
    /// in unscaled coordinates so the values remain valid across zoom changes.
    /// <para>
    /// Call this only after <see cref="SelectedPageNumber"/> has been brought in sync with
    /// the current viewport (i.e. from the end of <see cref="UpdatePagesVisibility"/>),
    /// because the saved Y is relative to that page.
    /// </para>
    /// </summary>
    private void SaveScrollState()
    {
        // Skip while a tab-switch restoration is pending or setting visibility,
        // either state would otherwise overwrite the saved values with the
        // in-flight scroll position from the transition.
        if (_pendingScrollToPage || _isSettingPageVisibility)
        {
            return;
        }

        if (Scroll is null || LayoutTransform is null || !SelectedPageNumber.HasValue)
        {
            return;
        }

        if (ContainerFromIndex(SelectedPageNumber.Value - 1) is not PageItem pageItem)
        {
            return;
        }

        double scale = LayoutTransform.LayoutTransform?.Value.M11 ?? 1.0;
        SetCurrentValue(ScrollOffsetProperty, new Vector(
            Scroll.Offset.X / scale,
            Scroll.Offset.Y / scale - pageItem.Bounds.Top));
    }

    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        base.PrepareContainerForItemOverride(container, item, index);
        if (container is not PageItem pageItem)
        {
            return;
        }

        pageItem.Loaded += PageItem_Loaded;
        pageItem.Unloaded += PageItem_Unloaded;

        pageItem.SetCurrentValue(PageItem.VisibleAreaProperty, null);
    }

    /*
     * Container event wiring. The Loaded/Unloaded subscriptions live for the whole
     * container lifetime (in PrepareContainerForItemOverride), and every
     * Loaded/Unloaded cycle re-wires the per-container PointerExited handler.
     * This keeps the wiring alive when the control is detached and reattached without
     * its containers being re-prepared (e.g. a Tabalonia tab torn off into another window);
     * the virtualizing panel keeps its realized containers across that cycle.
     */

    private void PageItem_Unloaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not PageItem { InteractiveLayer: not null } pageItem)
        {
            return;
        }

        pageItem.InteractiveLayer.PointerExited -= _textSelectionHandler.OnPointerExited;
    }

    private void PageItem_Loaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not PageItem { InteractiveLayer: not null } pageItem)
        {
            return;
        }

        // Make sure we unsubscribe first (a recycled container that stayed loaded
        // keeps its subscription from its previous item)
        pageItem.InteractiveLayer.PointerExited -= _textSelectionHandler.OnPointerExited;
        pageItem.InteractiveLayer.PointerExited += _textSelectionHandler.OnPointerExited;
    }

    private void OnBeforePageRotation(object? sender, RoutedEventArgs e)
    {
        int? savedPage = VisiblePages?.Start.GetOffset(PageCount);
        double? savedOffset = GetCurrentPageRelativeYOffset(savedPage);

        if (!savedPage.HasValue || !savedOffset.HasValue)
        {
            return;
        }

        int pageNumber = savedPage.Value;
        double yOffset = savedOffset.Value;

        // After the rotation action and the resulting layout pass, restore the scroll
        // position relative to the page that was in view before the rotation.
        Dispatcher.UIThread.Post(() =>
        {
            GoToPage(pageNumber, yOffset);
        }, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Switch pointer capture to the page under the cursor if we are selecting text and the cursor is outside the current page.
    /// </summary>
    internal void TrySwitchCapture(PointerEventArgs e)
    {
        PageItem? endPage = GetPageItemOver(e);
        if (endPage?.InteractiveLayer is null)
        {
            // Cursor is not over any page, do nothing or
            // Template not yet applied on the target page — do nothing.
            return;
        }

        e.Pointer.Capture(endPage.InteractiveLayer); // Switch capture to new page
    }

    protected override void ClearContainerForItemOverride(Control container)
    {
        base.ClearContainerForItemOverride(container);

        if (container is not PageItem pageItem)
        {
            return;
        }

        pageItem.Loaded -= PageItem_Loaded;
        pageItem.Unloaded -= PageItem_Unloaded;

        // PointerExited is deliberately left subscribed: a container recycled while
        // it stays loaded gets no new Loaded event, so removing the (item-agnostic)
        // handler here would leave the recycled container without one.

        pageItem.SetCurrentValue(PageItem.VisibleAreaProperty, null);
    }

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey)
    {
        return new PageItem();
    }

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
    {
        return NeedsContainer<PageItem>(item, out recycleKey);
    }

    public PageItem? GetPageItemOver(PointerEventArgs e)
    {
        if (Presenter is null)
        {
            // Should never happen
            return null;
        }

        Point point = e.GetPosition(Presenter);

        // Quick reject
        if (!Presenter.Bounds.Contains(point))
        {
            return null;
        }

        int minPageIndex = _visibilityTracker.GetFirstRealizedIndex();
        int maxPageIndex = _visibilityTracker.GetLastRealizedIndex();

        if (minPageIndex == -1 || maxPageIndex == -1)
        {
            return null;
        }

        int startIndex = SelectedPageNumber.HasValue ? SelectedPageNumber.Value - 1 : 0; // Switch from one-indexed to zero-indexed

        bool isAfterSelectedPage = false;

        // Check selected current page
        if (ContainerFromIndex(startIndex) is PageItem presenter)
        {
            if (presenter.Bounds.Contains(point))
            {
                return presenter;
            }

            isAfterSelectedPage = point.Y > presenter.Bounds.Bottom;
        }

        if (isAfterSelectedPage)
        {
            // Start with checking forward
            for (int p = startIndex + 1; p < maxPageIndex; ++p)
            {
                if (ContainerFromIndex(p) is not PageItem cp)
                {
                    continue;
                }

                if (cp.Bounds.Contains(point))
                {
                    return cp;
                }

                if (point.Y < cp.Bounds.Top)
                {
                    return null;
                }
            }
        }
        else
        {
            // Continue with checking backward
            for (int p = startIndex - 1; p >= minPageIndex; --p)
            {
                if (ContainerFromIndex(p) is not PageItem cp)
                {
                    continue;
                }

                if (cp.Bounds.Contains(point))
                {
                    return cp;
                }

                if (point.Y > cp.Bounds.Bottom)
                {
                    return null;
                }
            }
        }

        return null;
    }

    internal void SetPanCursor()
    {
        Debug.ThrowNotOnUiThread();
        Cursor = App.PanCursor;
    }

    internal void SetDefaultCursor()
    {
        Debug.ThrowNotOnUiThread();
        Cursor = App.DefaultCursor;
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        Scroll = e.NameScope.FindFromNameScope<ScrollViewer>("PART_ScrollViewer");
        Scroll.AddHandler(ScrollViewer.ScrollChangedEvent, _scrollChangedHandler);
        Scroll.AddHandler(SizeChangedEvent, _sizeChangedHandler, RoutingStrategies.Direct);
        Scroll.Focus(); // Make sure the Scroll has focus

        LayoutTransform = e.NameScope.FindFromNameScope<LayoutTransformControl>("PART_LayoutTransformControl");
        LayoutTransform.AddHandler(PointerPressedEvent, _zoomPanController.OnPointerPressed);
        LayoutTransform.AddHandler(PointerMovedEvent, _zoomPanController.OnPointerMoved);
        LayoutTransform.AddHandler(PointerReleasedEvent, _zoomPanController.OnPointerReleased);

        if (CalyExtensions.IsMobilePlatform())
        {
            LayoutTransform.GestureRecognizers.Add(new PinchGestureRecognizer());
            LayoutTransform.AddHandler(PinchEvent, _zoomPanController.OnPinchChanged);
            LayoutTransform.AddHandler(PinchEndedEvent, _zoomPanController.OnPinchEnded);
            LayoutTransform.AddHandler(HoldingEvent, _zoomPanController.OnHolding);
        }
    }

    // NB: the handlers added to the template parts in OnApplyTemplate are intentionally
    // never removed. They live on this control's own template subtree (no leak beyond
    // the control's lifetime), and removing them on detach would leave the control dead
    // after a detach/reattach cycle because OnApplyTemplate does not run again.

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        ItemsPanelRoot!.DataContextChanged += ItemsPanelRoot_DataContextChanged;
        ItemsPanelRoot.LayoutUpdated += ItemsPanelRoot_LayoutUpdated;
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        ItemsPanelRoot!.DataContextChanged -= ItemsPanelRoot_DataContextChanged;
        ItemsPanelRoot.LayoutUpdated -= ItemsPanelRoot_LayoutUpdated;
    }

    private void ItemsPanelRoot_LayoutUpdated(object? sender, EventArgs e)
    {
        // When ItemsPanelRoot is first loaded, there is a chance that a container
        // (i.e. the second page) is realised after the last SetPagesVisibility()
        // call. When this happens the page will not be rendered because it
        // is seen as 'not visible'.
        // To prevent that we listen to the first layout updates and check visibility.

        // ScrollIntoView runs synchronous layout passes that re-raise LayoutUpdated,
        // which would re-enter this handler and recurse until the stack overflows.
        if (_isApplyingPendingScroll)
        {
            return;
        }

        if (_visibilityTracker.GetLastRealizedIndex() > 0)
        {
            if (_pendingScrollToPage)
            {
                // After a DataContext change (tab/document switch), items are now realized.
                // Scroll to the correct page before running auto-selection to prevent
                // UpdatePagesVisibility from selecting the wrong page based on a stale viewport.
                if (SelectedPageNumber.HasValue && SelectedPageNumber.Value > 0 && SelectedPageNumber.Value <= PageCount)
                {
                    // Snapshot the saved scroll state BEFORE any scroll operation. The
                    // ScrollChanged event fires synchronously from ScrollIntoView, and the
                    // SaveScrollStateToDataContext handler would otherwise overwrite the
                    // saved value with the in-flight scroll position. The two-way binding
                    // has already pulled the new document's saved value into this property
                    // at the DataContext change.
                    Vector savedOffset = ScrollOffset;

                    _isApplyingPendingScroll = true;
                    try
                    {
                        ScrollIntoView(SelectedPageNumber.Value - 1); // Can cause stack overflow without _isApplyingPendingScroll
                        ApplyScrollOffsets(SelectedPageNumber.Value, savedOffset.Y, offsetPdfCoord: false, savedOffset.X);
                    }
                    finally
                    {
                        _pendingScrollToPage = false;
                        _isApplyingPendingScroll = false;
                    }
                }
                else
                {
                    _pendingScrollToPage = false;
                }
            }

            if (UpdatePagesVisibility())
            {
                // We have enough containers realised, we can stop listening to layout updates.
                ItemsPanelRoot!.LayoutUpdated -= ItemsPanelRoot_LayoutUpdated;
            }
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DataContextProperty)
        {
            ResetState();
            _pendingScrollToPage = true;
            Scroll?.Focus();
            _visibilityTracker.EnsureValidContainersVisibility();
            ItemsPanelRoot?.LayoutUpdated -= ItemsPanelRoot_LayoutUpdated;
            ItemsPanelRoot?.LayoutUpdated += ItemsPanelRoot_LayoutUpdated;
        }
        else if (change.Property == ZoomLevelProperty)
        {
            _zoomPanController.HandleExternalZoomLevelChanged(change);
        }
    }

    private void ItemsPanelRoot_DataContextChanged(object? sender, EventArgs e)
    {
        LayoutUpdated += OnLayoutUpdatedOnce;
    }

    private void OnLayoutUpdatedOnce(object? sender, EventArgs e)
    {
        LayoutUpdated -= OnLayoutUpdatedOnce;

        // Ensure the pages visibility is set when OnApplyTemplate()
        // is not called, i.e. when a new document is opened but the
        // page has exactly the same dimension of the visible page
        _visibilityTracker.PostUpdateVisibility();
    }

    private bool _suppressScrollAdjustment;

    private void AdjustXOffsetOnExtentChanged(ScrollChangedEventArgs e)
    {
        if (Scroll is null || _suppressScrollAdjustment || _zoomPanController.IsZooming ||
            _zoomPanController.IsPinching || _pendingScrollToPage)
        {
            return;
        }

        // Ignore ordinary user scrolling; only react to geometry changes.
        bool extentChanged = Math.Abs(e.ExtentDelta.X) > 0.01;
        bool viewportChanged = Math.Abs(e.ViewportDelta.X) > 0.01;
        if (!extentChanged && !viewportChanged)
        {
            return;
        }

        double newExtent = Scroll.Extent.Width;
        double newViewport = Scroll.Viewport.Width;
        double newOffsetX = Scroll.Offset.X;

        double oldExtent = newExtent - e.ExtentDelta.X;
        double oldViewport = newViewport - e.ViewportDelta.X;
        double oldOffsetX = newOffsetX - e.OffsetDelta.X;

        if (oldExtent < 1.0 || newViewport < 1.0)
        {
            return;
        }

        double delta = newExtent - oldExtent;
        if (Math.Abs(delta) < 0.01)
        {
            return;
        }

        // Keep the content visually anchored during width changes.
        double prevContentX = oldExtent > oldViewport
            ? -oldOffsetX
            : (oldViewport - oldExtent) / 2.0;

        double targetContentX = prevContentX - delta / 2.0;

        double targetOffsetX = newExtent > newViewport
            ? -targetContentX
            : 0.0;

        targetOffsetX = Math.Clamp(targetOffsetX, 0.0, Math.Max(0.0, newExtent - newViewport));

        if (Math.Abs(targetOffsetX - newOffsetX) < 0.01)
        {
            return;
        }

        _suppressScrollAdjustment = true;
        try
        {
            Scroll.SetCurrentValue(ScrollViewer.OffsetProperty, Scroll.Offset.WithX(targetOffsetX));
        }
        finally
        {
            _suppressScrollAdjustment = false;
        }
    }

    private bool UpdatePagesVisibility()
    {
        // Exit early if the view is unstable (e.g., user interacting, or a tab-switch
        // restoration is still in flight)
        if (_isSettingPageVisibility || _pendingScrollToPage)
        {
            return false;
        }

        if (LayoutTransform is null || Scroll is null ||
            Scroll.Viewport.IsEmpty() || ItemsView.Count == 0 || !_visibilityTracker.HasRealizedItems())
        {
            return false;
        }

        Debug.AssertIsNullOrScale(LayoutTransform.LayoutTransform?.Value);

        // Compute viewport in document coordinates
        double invScale = 1.0 / (LayoutTransform.LayoutTransform?.Value.M11 ?? 1.0);
        Rect viewport = Scroll.GetViewportRect().TransformToAABB(Matrix.CreateScale(invScale, invScale));

        int firstRealisedIndex = _visibilityTracker.GetFirstRealizedIndex();
        int lastRealisedIndex = _visibilityTracker.GetLastRealizedIndex();

        if (firstRealisedIndex == -1 || lastRealisedIndex == -1)
        {
            SetCurrentValue(RealisedPagesProperty, null);
            if (VisiblePages.HasValue)
            {
                SetCurrentValue(VisiblePagesProperty, null);
                RefreshPages?.Execute(null);
            }

            return true;
        }

        int startIndex = (SelectedPageNumber ?? 1) - 1;

        // Adjust start if previous visible range is outdated
        if (VisiblePages is { } prev && (prev.Start.Value < firstRealisedIndex + 1 || prev.End.Value > lastRealisedIndex + 1))
        {
            // Previous visible pages are out of the realised pages.
            // The previous visible pages were marked as not visible,
            // on container clearing.
            // Start from first realised page.
            startIndex = firstRealisedIndex;
        }

        bool needMoreChecks = true;
        bool wasVisible = false;
        double maxOverlap = double.MinValue;
        int mostVisibleIndex = -1;

        bool CheckPage(int index, out bool visible)
        {
            visible = false;
            if (ContainerFromIndex(index) is not PageItem page)
            {
                return !wasVisible; // Skip unrealised pages but stop after last visible one.
            }

            if (!needMoreChecks || page.Bounds.IsEmpty())
            {
                page.SetCurrentValue(PageItem.VisibleAreaProperty, null);
                return wasVisible;
            }

            var bounds = GetAlignedBounds(page);
            if (!OverlapsHeight(viewport.Top, viewport.Bottom, bounds.Top, bounds.Bottom))
            {
                page.SetCurrentValue(PageItem.VisibleAreaProperty, null);
                needMoreChecks = !wasVisible;
                return true;
            }

            var intersect = bounds.Intersect(viewport);
            double overlapArea = intersect.Height * intersect.Width;
            if (overlapArea <= 0)
            {
                page.SetCurrentValue(PageItem.VisibleAreaProperty, null);
                needMoreChecks = !wasVisible;
                return true;
            }

            if (overlapArea > maxOverlap)
            {
                maxOverlap = overlapArea;
                mostVisibleIndex = index;
            }

            visible = true;
            page.SetCurrentValue(PageItem.VisibleAreaProperty, ComputeVisibleArea(page, intersect));
            return true;
        }

        // Check visibility starting from current selection, then forward and backward.
        CheckPage(startIndex, out bool selectedVisible);

        int firstVisibleIndex = selectedVisible ? startIndex : -1;
        int lastVisibleIndex = selectedVisible ? startIndex : -1;

        wasVisible = selectedVisible;
        for (int i = startIndex + 1; i < lastRealisedIndex && CheckPage(i, out bool visible); ++i)
        {
            if (visible)
            {
                lastVisibleIndex = i;
                if (!wasVisible)
                {
                    firstVisibleIndex = i;
                }
            }

            wasVisible = visible;
        }

        wasVisible = false;
        needMoreChecks = true;
        for (int i = startIndex - 1; i >= firstRealisedIndex && CheckPage(i, out bool visible); --i)
        {
            if (visible)
            {
                firstVisibleIndex = i;
                if (lastVisibleIndex == -1)
                {
                    lastVisibleIndex = i;
                }
            }

            wasVisible = visible;
        }

        // Update bound properties
        SetCurrentValue(RealisedPagesProperty, VirtualizedVisibilityTracker.GetPageRange(firstRealisedIndex, lastRealisedIndex));

        Range? currentVisiblePages = null;
        if (firstVisibleIndex != -1 && lastVisibleIndex != -1) // No visible pages
        {
            currentVisiblePages = new Range(firstVisibleIndex + 1, lastVisibleIndex + 2);
        }

        if (!VisiblePages.HasValue || !VisiblePages.Value.Equals(currentVisiblePages))
        {
            SetCurrentValue(VisiblePagesProperty, currentVisiblePages);
            RefreshPages?.Execute(null);
        }

        // Auto-select the page with the largest overlap
        if (mostVisibleIndex >= 0 && SelectedPageNumber != mostVisibleIndex + 1)
        {
            _isSettingPageVisibility = true;
            try
            {
                SetCurrentValue(SelectedPageNumberProperty, mostVisibleIndex + 1);
            }
            finally
            {
                _isSettingPageVisibility = false;
            }
        }

#if DEBUG
        if (VisiblePages.HasValue)
        {
            foreach (var item in Items.OfType<ViewModels.PageViewModel>())
            {
                if (item.PageNumber >= VisiblePages.Value.Start.Value && item.PageNumber < VisiblePages.Value.End.Value)
                {
                    System.Diagnostics.Debug.Assert(item.IsPageVisible);
                }
                else
                {
                    System.Diagnostics.Debug.Assert(!item.IsPageVisible);
                }
            }
        }
#endif


        SaveScrollState();

        return true;
    }

    private static Rect ComputeVisibleArea(PageItem page, Rect visible)
    {
        visible = visible.Translate(new Vector(-page.Bounds.Left, -page.Bounds.Top));
        return page.Rotation switch
        {
            90 => new Rect(visible.Y, page.Bounds.Width - visible.Right, visible.Height, visible.Width),
            180 => new Rect(page.Bounds.Width - visible.Right, page.Bounds.Height - visible.Bottom, visible.Width, visible.Height),
            270 => new Rect(page.Bounds.Height - visible.Bottom, visible.X, visible.Height, visible.Width),
            _ => visible
        };
    }

    private static Rect GetAlignedBounds(PageItem page)
    {
        var bounds = page.Bounds;
        if (bounds.Height == 0) return bounds;

        double expectedWidth = page.Width;
        if (Math.Abs(bounds.Width - expectedWidth) > double.Epsilon)
        {
            double offset = (bounds.Width - expectedWidth) / 2.0;
            bounds = new Rect(bounds.X + offset, bounds.Y, expectedWidth, bounds.Height);
        }
        return bounds;
    }

    /// <summary>
    /// Works for vertical scrolling.
    /// </summary>
    private static bool OverlapsHeight(double top1, double bottom1, double top2, double bottom2)
    {
        return !(top1 > bottom2 || bottom1 < top2);
    }

    private void OnKeyUpHandler(object? sender, KeyEventArgs e)
    {
        if (e.IsPanningOrZooming())
        {
            _zoomPanController.ResetPan();
        }
    }

    private void OnKeyDownHandler(object? sender, KeyEventArgs e)
    {
        if (Scroll is null)
        {
            return;
        }

        if (e.IsPanningOrZooming())
        {
            _zoomPanController.ResetPan();
            return;
        }

        switch (e.Key)
        {
            case Key.Home:
            {
                Scroll.ScrollToHome();
                e.Handled = true;
                break;
            }
            case Key.End:
            {
                Scroll.ScrollToEnd();
                e.Handled = true;
                break;
            }
            case Key.PageUp:
            {
                int? pageNumber = SelectedPageNumber;
                if (pageNumber.HasValue)
                {
                    GoToPage(pageNumber.Value - 1, 0);
                    e.Handled = true;
                }

                break;
            }
            case Key.PageDown:
            {
                int? pageNumber = SelectedPageNumber;
                if (pageNumber.HasValue)
                {
                    GoToPage(pageNumber.Value + 1, 0);
                    e.Handled = true;
                }

                break;
            }
            case Key.Right:
            {
                Scroll.PageDown();
                e.Handled = true;
                break;
            }
            case Key.Down:
            {
                Scroll.LineDown();
                e.Handled = true;
                break;
            }
            case Key.Left:
            {
                Scroll.PageUp();
                e.Handled = true;
                break;
            }
            case Key.Up:
            {
                Scroll.LineUp();
                e.Handled = true;
                break;
            }
        }
    }

    private void ResetState()
    {
        SetCurrentValue(VisiblePagesProperty, null);
        _textSelectionHandler.Reset();
        _zoomPanController.Reset();
        _isSettingPageVisibility = false;
        _pendingScrollToPage = false;
        _isApplyingPendingScroll = false;
        _visibilityTracker.Reset();
        _suppressScrollAdjustment = false;
    }
}