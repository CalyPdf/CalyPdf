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
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Transformation;
using Caly.Core.Models;
using Caly.Core.Utilities;
using Caly.Pdf;
using Caly.Pdf.Models;
using System;
using System.Linq;
using System.Windows.Input;
using Avalonia.Threading;
using UglyToad.PdfPig.Actions;
using UglyToad.PdfPig.Core;

namespace Caly.Core.Controls;

/// <summary>
/// Control that displays the PDF document pages.
/// </summary>
[TemplatePart("PART_ScrollViewer", typeof(ScrollViewer))]
[TemplatePart("PART_LayoutTransformControl", typeof(LayoutTransformControl))]
public sealed class PageItemsControl : ItemsControl
{
    private const double _zoomFactor = 1.1;

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
    /// <c>true</c> if we are currently selecting text. <c>false</c> otherwise.
    /// </summary>
    private bool _isSelecting;

    /// <summary>
    /// <c>true</c> if we are selecting text though multiple click (full word selection).
    /// </summary>
    private bool _isMultipleClickSelection;

    private Point? _startPointerPressed;
    private Point? _currentPosition;
    private bool _isSettingPageVisibility;
    private bool _isZooming;
    private bool _pendingScrollToPage;
    private bool _isUpdatePagesVisibilityScheduled;

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
        _scrollChangedHandler = (_, _) => PostUpdatePagesVisibility();
        _sizeChangedHandler = (_, _) => PostUpdatePagesVisibility();

        // Use a Tunnel handler to ensure zoom checks run before bubble-phase handlers
        // and avoid unwanted event scrolls by 50px before we can reject them.
        // No need to RemoveHandler() as it is on 'this', so it's GC'd with the control.
        AddHandler(PointerWheelChangedEvent, OnPointerWheelChangedHandler, RoutingStrategies.Tunnel);

        ResetState();
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
            if (pageItem.Rotation == 0)
            {
                yOffset = pageItem.Bounds.Height - yOffset;
            }
            else
            {
                // Page wa rotated by user.
                // Pdf coordinates are not relevant anymore, we just scroll to the top of the page.
                yOffset = 0;
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
        // Skip while a tab-switch restoration is pending or while we're mid-zoom,
        // either state would otherwise overwrite the saved values with the
        // in-flight scroll position from the transition.
        if (_pendingScrollToPage || _isZooming || _isSettingPageVisibility)
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
            Math.Max(0, (Scroll.Offset.Y / scale) - pageItem.Bounds.Top)));
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

    private void PageItem_Unloaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not PageItem pageItem)
        {
            return;
        }

        pageItem.Loaded -= PageItem_Loaded;
        pageItem.Unloaded -= PageItem_Unloaded;

        if (pageItem.InteractiveLayer is null)
        {
            return;
        }

        pageItem.InteractiveLayer.PointerMoved -= InteractiveLayerPointerMoved;
        pageItem.InteractiveLayer.PointerWheelChanged -= InteractiveLayerPointerMoved;
        pageItem.InteractiveLayer.PointerExited -= InteractiveLayerPointerExited;
        pageItem.InteractiveLayer.PointerReleased -= InteractiveLayerPointerReleased;
        pageItem.InteractiveLayer.PointerPressed -= InteractiveLayerPointerPressed;
        pageItem.BeforeRotation -= OnBeforePageRotation;
    }

    private void PageItem_Loaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not PageItem pageItem)
        {
            return;
        }

        pageItem.Loaded -= PageItem_Loaded;

        if (pageItem.InteractiveLayer is null)
        {
            return;
        }

        // Make sure we unsubscribe first
        pageItem.InteractiveLayer.PointerMoved -= InteractiveLayerPointerMoved;
        pageItem.InteractiveLayer.PointerWheelChanged -= InteractiveLayerPointerMoved;
        pageItem.InteractiveLayer.PointerExited -= InteractiveLayerPointerExited;
        pageItem.InteractiveLayer.PointerReleased -= InteractiveLayerPointerReleased;
        pageItem.InteractiveLayer.PointerPressed -= InteractiveLayerPointerPressed;
        pageItem.BeforeRotation -= OnBeforePageRotation;

        // Then subscribe to events
        pageItem.InteractiveLayer.PointerMoved += InteractiveLayerPointerMoved;
        pageItem.InteractiveLayer.PointerWheelChanged += InteractiveLayerPointerMoved;
        pageItem.InteractiveLayer.PointerExited += InteractiveLayerPointerExited;
        pageItem.InteractiveLayer.PointerReleased += InteractiveLayerPointerReleased;
        pageItem.InteractiveLayer.PointerPressed += InteractiveLayerPointerPressed;
        pageItem.BeforeRotation += OnBeforePageRotation;
    }

    private void OnBeforePageRotation(object? sender, EventArgs e)
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

    private void InteractiveLayerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Debug.ThrowNotOnUiThread();

        if (TextSelection is null || sender is not PageInteractiveLayerControl control || control.PdfTextLayer is null)
        {
            return;
        }

        if (e.IsPanningOrZooming())
        {
            // Panning pages is not handled here
            control.HideAnnotation();
            return;
        }

        bool clearSelection = false;

        _isMultipleClickSelection = e.ClickCount > 1;

        var pointerPoint = e.GetCurrentPoint(control);
        var point = pointerPoint.Position;

        if (pointerPoint.Properties.IsLeftButtonPressed)
        {
            _startPointerPressed = point;

            // Text selection
            PdfWord? word = control.PdfTextLayer.FindWordOver(point.X, point.Y);

            if (word is not null && TextSelection.IsWordSelected(control.PageNumber!.Value, word))
            {
                clearSelection = e.ClickCount == 1; // Clear selection if single click
                if (e.ClickCount >= 2)
                {
                    HandleMultipleClick(control, e, word);
                }
            }
            else if (word is not null && e.ClickCount == 2)
            {
                // TODO - do better multiple click selection
                HandleMultipleClick(control, e, word);
            }
            else
            {
                clearSelection = true;
            }
        }
        else if (pointerPoint.Properties.IsRightButtonPressed)
        {
            // Always hide annotation on right-click to not conflict with context flyout. This works
            // on Windows but would need to be tested on other platforms
            control.HideAnnotation();
        }

        if (clearSelection)
        {
            ClearSelection?.Execute(null);
        }

        e.Handled = true;
        e.PreventGestureRecognition();
    }

    private void HandleMultipleClick(PageInteractiveLayerControl control, PointerPressedEventArgs e, PdfWord word)
    {
        if (TextSelection is null || control.PdfTextLayer is null)
        {
            return;
        }

        PdfWord? startWord;
        PdfWord? endWord;

        switch (e.ClickCount)
        {
            case 2:
                {
                    // Select whole word
                    startWord = word;
                    endWord = word;
                    break;
                }
            case 3:
                {
                    // Select whole line
                    var block = control.PdfTextLayer.TextBlocks![word.TextBlockIndex];
                    var line = block.TextLines![word.TextLineIndex - block.TextLines[0].IndexInPage];

                    startWord = line.Words[0];
                    endWord = line.Words[^1];
                    break;
                }
            case 4:
                {
                    // Select whole paragraph
                    var block = control.PdfTextLayer.TextBlocks![word.TextBlockIndex];

                    startWord = block.TextLines![0].Words![0];
                    endWord = block.TextLines![^1].Words![^1];
                    break;
                }
            default:
                System.Diagnostics.Debug.WriteLine($"HandleMultipleClick: Not handled, got {e.ClickCount} click(s).");
                return;
        }

        ClearSelection?.Execute(null);

        int pageNumber = control.PageNumber!.Value;
        TextSelection.Start(pageNumber, startWord);
        TextSelection.Extend(pageNumber, endWord);

        System.Diagnostics.Debug.WriteLine($"HandleMultipleClick: {startWord} -> {endWord}.");
    }

    private void InteractiveLayerPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        Debug.ThrowNotOnUiThread();

        if (sender is not PageInteractiveLayerControl control || control.PdfTextLayer is null)
        {
            return;
        }

        if (e.IsPanningOrZooming())
        {
            // Panning pages is not handled here
            return;
        }

        _startPointerPressed = null;

        var pointerPoint = e.GetCurrentPoint(control);

        bool ignore = _isSelecting || _isMultipleClickSelection;
        if (!ignore && pointerPoint.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonReleased)
        {
            ClearSelection?.Execute(null);

            var point = pointerPoint.Position;

            // Annotation
            PdfAnnotation? annotation = control.PdfTextLayer.FindAnnotationOver(point.X, point.Y);

            if (annotation?.Action is not null)
            {
                switch (annotation.Action.Type)
                {
                    case ActionType.URI:
                        string? uri = ((UriAction)annotation.Action)?.Uri;
                        if (!string.IsNullOrEmpty(uri))
                        {
                            CalyExtensions.OpenUriAsync(uri);
                            return;
                        }
                        break;

                    case ActionType.GoTo:
                    case ActionType.GoToE:
                    case ActionType.GoToR:
                        var goToAction = (AbstractGoToAction)annotation.Action;
                        var dest = goToAction?.Destination;
                        if (dest is not null)
                        {
                            // Ignore destination types for the moment
                            if (dest.Coordinates.Top.HasValue)
                            {
                                double scaledTop = dest.Coordinates.Top.Value * annotation.PpiScale;
                                GoToPage(dest.PageNumber, scaledTop, true);
                            }
                            else
                            {
                                GoToPage(dest.PageNumber, 0); // Top of page
                            }
                            return;
                        }
                        else
                        {
                            // Log error
                        }
                        break;
                }
            }

            // Words
            PdfWord? word = control.PdfTextLayer.FindWordOver(point.X, point.Y);
            if (word is not null && control.PdfTextLayer.GetLine(word) is { IsInteractive: true } line)
            {
                /*
                 * TODO - Use TopLevel.GetTopLevel(source)?.Launcher
                 *  if (e.Source is Control source && TopLevel.GetTopLevel(source)?.Launcher is {}
                 *  launcher && word is not null && control.PdfTextLayer.GetLine(word) is { IsInteractive: true } line)
                 *  ...
                 *  launcher.LaunchUriAsync(new Uri(match.ToString()))
                 */

                if (!string.IsNullOrEmpty(line.InteractiveLink))
                {
                    CalyExtensions.OpenUriAsync(line.InteractiveLink);
                }
            }
        }

        _isSelecting = false;

        e.Handled = true;
        e.PreventGestureRecognition();
    }

    private void InteractiveLayerPointerExited(object? sender, PointerEventArgs e)
    {
        Debug.ThrowNotOnUiThread();

        if (sender is not PageInteractiveLayerControl interactiveLayer)
        {
            return;
        }

        interactiveLayer.SetDefaultCursor();
        interactiveLayer.HideAnnotation();
        SetCurrentValue(InteractiveActionOverProperty, null);
    }

    private void InteractiveLayerPointerMoved(object? sender, PointerEventArgs e)
    {
        Debug.ThrowNotOnUiThread();

        // Needs to be on UI thread to access
        if (sender is not PageInteractiveLayerControl control || control.PdfTextLayer is null)
        {
            return;
        }

        if (e.IsPanningOrZooming())
        {
            // Panning pages is not handled here
            return;
        }

        var pointerPoint = e.GetCurrentPoint(control);
        var loc = pointerPoint.Position;

        if (e is PointerWheelEventArgs we)
        {
            // TODO - Looks like there's a bug in Avalonia (TBC) where the position of the pointer
            // is 1 step behind the actual position.
            // We need to add back this step (1 scroll step is 50, see link below)
            // https://github.com/AvaloniaUI/Avalonia/blob/dadc9ab69284bb228ad460f36d5442b4eee4a82a/src/Avalonia.Controls/Presenters/ScrollContentPresenter.cs#L684

            var adjPoint = new Point(50, 50);
            var matrix = control.GetLayoutTransformMatrix();

            if (!matrix.IsIdentity && matrix.TryInvert(out var inverted))
            {
                adjPoint = inverted.Transform(adjPoint);
            }

            double x = Math.Max(loc.X - we.Delta.X * adjPoint.X, 0);
            double y = Math.Max(loc.Y - we.Delta.Y * adjPoint.Y, 0);

            loc = new Point(x, y);

            // TODO - We have an issue when scrolling and changing page here, similar the TrySwitchCapture
            // not sure how we should address it
        }

        if (pointerPoint.Properties.IsLeftButtonPressed && _startPointerPressed.HasValue && _startPointerPressed.Value.Euclidean(loc) > 1.0)
        {
            // Text selection
            HandleMouseMoveSelection(control, e, loc);
        }
        else
        {
            HandleMouseMoveOver(control, pointerPoint.Properties, loc);
        }
    }

    private void HandleMouseMoveSelection(PageInteractiveLayerControl control, PointerEventArgs e, Point loc)
    {
        if (_isMultipleClickSelection || TextSelection is null)
        {
            return;
        }

        if (!control.Bounds.Contains(loc))
        {
            TrySwitchCapture(e);
            return;
        }

        // Get the line under the cursor or nearest from the top
        PdfTextLine? lineBox = control.PdfTextLayer!.FindLineOver(loc.X, loc.Y);

        PdfWord? word = null;
        if (TextSelection.HasStarted && lineBox is null)
        {
            // Try to find the closest line as we are already selecting something
            word = FindNearestWordWhileSelecting(loc, control.PdfTextLayer);
        }

        if (lineBox is null && word is null)
        {
            return;
        }

        if (lineBox is not null && word is null)
        {
            // Get the word under the cursor
            word = lineBox.FindWordOver(loc.X, loc.Y);

            // If no word found under the cursor use the last or the first word in the line
            if (word is null)
            {
                word = lineBox.FindNearestWord(loc.X, loc.Y);
            }
        }

        if (word is null)
        {
            return;
        }

        // If there is matching word
        bool allowPartialSelect = !_isMultipleClickSelection;

        Point? partialSelectLoc = allowPartialSelect ? loc : null;
        if (!TextSelection.HasStarted)
        {
            TextSelection.Start(control.PageNumber!.Value, word, partialSelectLoc);
        }

        // Always set the focus word
        TextSelection.Extend(control.PageNumber!.Value, word, partialSelectLoc);

        control.SetIbeamCursor();

        _isSelecting = TextSelection.IsSelecting;
    }

    /// <summary>
    /// Handle mouse hover over words, links or others
    /// </summary>
    private void HandleMouseMoveOver(PageInteractiveLayerControl control, PointerPointProperties properties, Point loc)
    {
        PdfAnnotation? annotation = control.PdfTextLayer!.FindAnnotationOver(loc.X, loc.Y);

        if (annotation is not null)
        {
            if (!string.IsNullOrEmpty(annotation.Content) && !properties.IsRightButtonPressed)
            {
                // We do not show annotation when right-clicking
                // to not conflict with context flyout. This works
                // on Windows but would need to be tested on other platforms
                control.ShowAnnotation(annotation);
            }

            if (annotation.IsInteractive)
            {
                control.SetHandCursor();
                if (annotation.Action is UriAction uriAction)
                {
                    SetCurrentValue(InteractiveActionOverProperty, $"Open '{uriAction.Uri}'");
                }

                return;
            }
        }
        else
        {
            control.HideAnnotation();
        }

        PdfWord? word = control.PdfTextLayer!.FindWordOver(loc.X, loc.Y);
        if (word is not null)
        {
            //if (control.PdfTextLayer.GetLine(word)?.IsInteractive == true)
            if (control.PdfTextLayer.GetLine(word) is { IsInteractive: true } line)
            {
                control.SetHandCursor();
                SetCurrentValue(InteractiveActionOverProperty, $"Open '{line.InteractiveLink}'");
            }
            else
            {
                control.SetIbeamCursor();
                SetCurrentValue(InteractiveActionOverProperty, null);
            }
        }
        else
        {
            control.SetDefaultCursor();
            SetCurrentValue(InteractiveActionOverProperty, null);
        }
    }

    private static PdfWord? FindNearestWordWhileSelecting(Point loc, PdfTextLayer textLayer)
    {
        if (textLayer.TextBlocks is null || textLayer.TextBlocks.Count == 0)
        {
            return null;
        }

        // Try finding the closest line as we are already selecting something

        // TODO - To finish, improve performance
        var point = new PdfPoint(loc.X, loc.Y);

        double dist = double.MaxValue;
        double projectionOnLine = 0;
        PdfTextLine? l = null;

        foreach (var block in textLayer.TextBlocks)
        {
            foreach (var line in block.TextLines)
            {
                PdfPoint? projection = PdfPointExtensions.ProjectPointOnLine(in point,
                    line.BoundingBox.BottomLeft,
                    line.BoundingBox.BottomRight,
                    out double s);

                if (!projection.HasValue || s < 0)
                {
                    // If s < 0, the cursor is before the line (to the left), we ignore
                    continue;
                }

                // If s > 1, the cursor is after the line (to the right), we measure distance from bottom right corner
                PdfPoint referencePoint = s > 1 ? line.BoundingBox.BottomRight : projection.Value;

                double localDist = SquaredWeightedEuclidean(in point, in referencePoint, wY: 4); // Make y direction farther

                // TODO - Prevent selection line 'below' cursor

                if (localDist < dist)
                {
                    dist = localDist;
                    l = line;
                    projectionOnLine = s;
                }
            }
        }

        if (l is null)
        {
            return null;
        }

        if (projectionOnLine >= 1)
        {
            // Cursor after line, return last word
            return l.Words[^1];
        }

        // TODO - to improve, we already know where on the line is the point thanks to 'projectionOnLine'
        return l.FindNearestWord(loc.X, loc.Y);

        static double SquaredWeightedEuclidean(in PdfPoint point1, in PdfPoint point2, double wX = 1.0, double wY = 1.0)
        {
            double dx = point1.X - point2.X;
            double dy = point1.Y - point2.Y;
            return wX * dx * dx + wY * dy * dy;
        }
    }

    /// <summary>
    /// Switch pointer capture to the page under the cursor if we are selecting text and the cursor is outside the current page.
    /// </summary>
    private void TrySwitchCapture(PointerEventArgs e)
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

            return Math.Min(PageCount, v.LastRealizedIndex + 1);
        }

        return PageCount;
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

        int minPageIndex = GetMinPageIndex();
        int maxPageIndex = GetMaxPageIndex(); // Exclusive

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
        Scroll.AddHandler(KeyDownEvent, OnKeyDownHandler);
        Scroll.AddHandler(KeyUpEvent, OnKeyUpHandler);
        Scroll.Focus(); // Make sure the Scroll has focus

        LayoutTransform = e.NameScope.FindFromNameScope<LayoutTransformControl>("PART_LayoutTransformControl");
        LayoutTransform.AddHandler(PointerPressedEvent, OnPointerPressed);
        LayoutTransform.AddHandler(PointerMovedEvent, OnPointerMoved);
        LayoutTransform.AddHandler(PointerReleasedEvent, OnPointerReleased);

        if (CalyExtensions.IsMobilePlatform())
        {
            LayoutTransform.GestureRecognizers.Add(new PinchGestureRecognizer());
            LayoutTransform.AddHandler(PinchEvent, _onPinchChangedHandler);
            LayoutTransform.AddHandler(PinchEndedEvent, _onPinchChangedHandler);
            LayoutTransform.AddHandler(HoldingEvent, _onPinchChangedHandler);
        }
    }

    protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromLogicalTree(e);

        if (Scroll is not null)
        {
            Scroll.RemoveHandler(ScrollViewer.ScrollChangedEvent, _scrollChangedHandler);
            Scroll.RemoveHandler(SizeChangedEvent, _sizeChangedHandler);
            Scroll.RemoveHandler(KeyDownEvent, OnKeyDownHandler);
            Scroll.RemoveHandler(KeyUpEvent, OnKeyUpHandler);
        }

        if (LayoutTransform is not null)
        {
            LayoutTransform.RemoveHandler(PointerPressedEvent, OnPointerPressed);
            LayoutTransform.RemoveHandler(PointerMovedEvent, OnPointerMoved);
            LayoutTransform.RemoveHandler(PointerReleasedEvent, OnPointerReleased);

            if (CalyExtensions.IsMobilePlatform())
            {
                LayoutTransform.RemoveHandler(PinchEvent, _onPinchChangedHandler);
                LayoutTransform.RemoveHandler(PinchEndedEvent, _onPinchChangedHandler);
                LayoutTransform.RemoveHandler(HoldingEvent, _onPinchChangedHandler);
            }
        }
    }

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

        if (GetMaxPageIndex() > 0)
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

                    ScrollIntoView(SelectedPageNumber.Value - 1);
                    ApplyScrollOffsets(SelectedPageNumber.Value, savedOffset.Y, offsetPdfCoord: false, savedOffset.X);
                    _pendingScrollToPage = false;
                    return; // Wait for the scroll to trigger another layout update.
                }
                _pendingScrollToPage = false;
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
            EnsureValidContainersVisibility();
            ItemsPanelRoot?.LayoutUpdated -= ItemsPanelRoot_LayoutUpdated;
            ItemsPanelRoot?.LayoutUpdated += ItemsPanelRoot_LayoutUpdated;
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

        var realised = GetRealizedContainers().OfType<PageItem>();
        var visibleChildren = ItemsPanelRoot.Children.Where(c => c.IsVisible).OfType<PageItem>();

        foreach (var child in visibleChildren.Except(realised))
        {
            child.SetCurrentValue(IsVisibleProperty, false);
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
        PostUpdatePagesVisibility();
    }

    private bool HasRealisedItems()
    {
        if (ItemsPanelRoot is VirtualizingStackPanel vsp)
        {
            return vsp.FirstRealizedIndex != -1 && vsp.LastRealizedIndex != -1;
        }

        return false;
    }

    private void PostUpdatePagesVisibility()
    {
        if (_isUpdatePagesVisibilityScheduled)
        {
            return;
        }

        _isUpdatePagesVisibilityScheduled = true;
        Dispatcher.UIThread.Post(() =>
        {
            _isUpdatePagesVisibilityScheduled = false;
            UpdatePagesVisibility();
        }, DispatcherPriority.Loaded);
    }

    private bool UpdatePagesVisibility()
    {
        // Exit early if the view is unstable (e.g., user interacting, or a tab-switch
        // restoration is still in flight)
        if (_isSettingPageVisibility || _isZooming || _pendingScrollToPage)
        {
            return false;
        }

        if (LayoutTransform is null || Scroll is null ||
            Scroll.Viewport.IsEmpty() || ItemsView.Count == 0 || !HasRealisedItems())
        {
            return false;
        }

        Debug.AssertIsNullOrScale(LayoutTransform.LayoutTransform?.Value);

        // Compute viewport in document coordinates
        double invScale = 1.0 / (LayoutTransform.LayoutTransform?.Value.M11 ?? 1.0);
        Rect viewport = Scroll.GetViewportRect().TransformToAABB(Matrix.CreateScale(invScale, invScale));

        int firstRealisedIndex = GetMinPageIndex();
        int lastRealisedIndex = GetMaxPageIndex();

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
        SetCurrentValue(RealisedPagesProperty, new Range(firstRealisedIndex + 1, lastRealisedIndex + 2));

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
        if (Scroll is not null)
        {
            // We re-subscribe to key down events, even if no
            // unsubscribe happened.
            Scroll.RemoveHandler(KeyDownEvent, OnKeyDownHandler);
            Scroll.AddHandler(KeyDownEvent, OnKeyDownHandler);
        }

        if (e.IsPanningOrZooming())
        {
            ResetPanTo();
        }
    }

    private void OnKeyDownHandler(object? sender, KeyEventArgs e)
    {
        if (e.IsPanningOrZooming())
        {
            // We stop listening to key down events when panning / zooming.
            // Keeping the 'ctrl' key down involves continuously firing 
            // key down events.
            Scroll?.RemoveHandler(KeyDownEvent, OnKeyDownHandler);
            ResetPanTo();
        }

        switch (e.Key)
        {
            case Key.Right:
                Scroll!.PageDown();
                break;
            case Key.Down:
                Scroll!.LineDown();
                break;
            case Key.Left:
                Scroll!.PageUp();
                break;
            case Key.Up:
                Scroll!.LineUp();
                break;
        }
    }

    #region Mobile handling

    private void _onHoldingChangedHandler(object? sender, HoldingRoutedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"Holding {e.HoldingState}: {e.Position.X}, {e.Position.Y}");
    }

    private double _pinchZoomReference = 1.0;
    private bool _isPinching;

    private void _onPinchEndedHandler(object? sender, PinchEndedEventArgs e)
    {
        _pinchZoomReference = ZoomLevel;
        _isPinching = false;
    }

    private void _onPinchChangedHandler(object? sender, PinchEventArgs e)
    {
        if (!_isPinching)
        {
            // Capture the zoom level at the start of each new pinch gesture so the
            // first event doesn't compute dZoom against a stale reference of 1.0.
            _pinchZoomReference = ZoomLevel;
            _isPinching = true;
        }

        if (e.Scale != 0)
        {
            ZoomTo(e);
            e.Handled = true;
        }
    }

    private void ZoomTo(PinchEventArgs e)
    {
        if (LayoutTransform is null)
        {
            return;
        }

        if (_isZooming)
        {
            return;
        }

        try
        {
            _isZooming = true;

            // Pinch zoom always starts with a scale of 1, then increase/decrease until PinchEnded
            double dZoom = (e.Scale * _pinchZoomReference) / ZoomLevel;

            // TODO - Origin still not correct
            var point = LayoutTransform.PointToClient(new PixelPoint((int)e.ScaleOrigin.X, (int)e.ScaleOrigin.Y));
            ZoomToInternal(dZoom, point);
            SetCurrentValue(ZoomLevelProperty, LayoutTransform.LayoutTransform?.Value.M11);
        }
        finally
        {
            _isZooming = false;
        }
    }
    #endregion

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.IsPanning())
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        _currentPosition = point.Position;
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!e.IsPanningOrZooming())
        {
            return;
        }

        if (e.IsPanning())
        {
            SetPanCursor();
            PanTo(e);
        }

        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        ResetPanTo();
    }

    private void PanTo(PointerEventArgs e)
    {
        if (Scroll is null)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);

        if (!_currentPosition.HasValue)
        {
            _currentPosition = point.Position;
            return;
        }

        var delta = point.Position - _currentPosition;

        var offset = Scroll.Offset - delta.Value;
        Scroll.SetCurrentValue(ScrollViewer.OffsetProperty, offset);
        _currentPosition = point.Position;
    }

    private void ResetPanTo()
    {
        _currentPosition = null;
        SetDefaultCursor();
    }

    private void OnPointerWheelChangedHandler(object? sender, PointerWheelEventArgs e)
    {
        var hotkeys = Application.Current!.PlatformSettings?.HotkeyConfiguration;
        var ctrl = hotkeys is not null && e.KeyModifiers.HasFlag(hotkeys.CommandModifiers);

        if (ctrl && e.Delta.Y != 0)
        {
            ZoomTo(e);
            e.Handled = true;
            e.PreventGestureRecognition();
        }
    }

    private void ZoomTo(PointerWheelEventArgs e)
    {
        if (LayoutTransform is null)
        {
            return;
        }

        if (_isZooming)
        {
            return;
        }

        try
        {
            _isZooming = true;
            double dZoom = Math.Round(Math.Pow(_zoomFactor, e.Delta.Y), 4); // If IsScrollInertiaEnabled = false, Y is only 1 or -1
            ZoomToInternal(dZoom, e.GetPosition(LayoutTransform));
            SetCurrentValue(ZoomLevelProperty, LayoutTransform.LayoutTransform?.Value.M11);
        }
        finally
        {
            _isZooming = false;
        }
    }

    internal void ZoomTo(double dZoom, Point point)
    {
        if (LayoutTransform is null || Scroll is null)
        {
            return;
        }

        if (_isZooming)
        {
            return;
        }

        try
        {
            _isZooming = true;
            ZoomToInternal(dZoom, point);
        }
        finally
        {
            _isZooming = false;
        }
    }

    private void ZoomToInternal(double dZoom, Point point)
    {
        if (LayoutTransform is null || Scroll is null)
        {
            return;
        }

        double oldZoom = LayoutTransform.LayoutTransform?.Value.M11 ?? 1.0;
        double newZoom = oldZoom * dZoom;

        if (newZoom < MinZoomLevel)
        {
            if (oldZoom.Equals(MinZoomLevel))
            {
                return;
            }

            newZoom = MinZoomLevel;
            dZoom = newZoom / oldZoom;
        }
        else if (newZoom > MaxZoomLevel)
        {
            if (oldZoom.Equals(MaxZoomLevel))
            {
                return;
            }

            newZoom = MaxZoomLevel;
            dZoom = newZoom / oldZoom;
        }

        var builder = TransformOperations.CreateBuilder(1);
        builder.AppendScale(newZoom, newZoom);
        LayoutTransform.LayoutTransform = builder.Build();

        var offset = Scroll.Offset - GetOffset(dZoom, point.X, point.Y);
        if (newZoom > oldZoom)
        {
            // When zooming-in, we need to re-arrange the scroll viewer
            Scroll.Measure(Size.Infinity);
            Scroll.Arrange(new Rect(Scroll.DesiredSize));
        }

        Scroll.SetCurrentValue(ScrollViewer.OffsetProperty, offset);
    }

    private static Vector GetOffset(double scale, double x, double y)
    {
        double s = 1 - scale;
        return new Vector(x * s, y * s);
    }

    private void ResetState()
    {
        SetCurrentValue(VisiblePagesProperty, null);
        _currentPosition = null;
        _isSelecting = false;
        _isMultipleClickSelection = false;
        _startPointerPressed = null;
        _isSettingPageVisibility = false;
        _isZooming = false;
        _pendingScrollToPage = false;
        _isPinching = false;
        _isUpdatePagesVisibilityScheduled = false;
    }
}