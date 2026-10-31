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
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Caly.Core.Models;

namespace Caly.Core.Controls;

/*
 * Zoom fit modes. While ZoomMode is a fit mode, the control computes the zoom that fits the
 * reference page (ZoomFit), applies it through ZoomPanController and lands the view on the page
 * with GoToPage / ApplyScrollOffsets. A fit never sets ZoomMode: only a zoom picked by hand
 * (ZoomPanController, or the view model's commands) leaves a fit mode.
 *
 * Requests are coalesced into one fit, run from LayoutUpdated: after the layout pass, so the zoom
 * never changes inside measure or arrange, and before the frame is rendered, because Avalonia lays
 * out again before rendering when a LayoutUpdated handler invalidates the layout. The frame that
 * shows a resize or new pages therefore shows them fitted. A job at Loaded priority would run after
 * the render: that frame would show the old zoom (and, on a shrinking window, the horizontal bar).
 */
public sealed partial class PageItemsControl
{
    /// <summary>
    /// Defines the <see cref="ZoomMode"/> property.
    /// </summary>
    public static readonly StyledProperty<ZoomMode> ZoomModeProperty =
        AvaloniaProperty.Register<PageItemsControl, ZoomMode>(nameof(ZoomMode),
            defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Relative zoom change below which a fit that nobody asked for (see <see cref="RequestFit"/>)
    /// leaves the view alone.
    /// </summary>
    private const double RelativeZoomTolerance = 1e-3;

    /// <summary>
    /// A fit was requested and has not run yet (see <see cref="RequestFit"/>).
    /// </summary>
    private bool _isFitScheduled;

    /// <summary>
    /// A run of the requested fit is posted for when the zoom in flight has settled.
    /// </summary>
    private bool _isFitWaitingForZoom;

    /// <summary>
    /// The requested fit is for a new viewer size, which may zoom while a zoom is in flight (see
    /// <see cref="RunFit"/>).
    /// </summary>
    private bool _isFitForNewViewerSize;

    /// <summary>
    /// The pending fit was asked for by the user (a mode picked), as opposed to the viewer
    /// changing: it lands the view even when the zoom does not change.
    /// </summary>
    private bool _isExplicitFitPending;

    /// <summary>
    /// Continuous view: the page the fits are computed from. Starts at 1. Re-picked from
    /// <see cref="SelectedPageNumber"/> when <see cref="_fitRepickReference"/> is set, and kept
    /// while the user scrolls, so that scrolling onto a page of another size never re-zooms.
    /// </summary>
    private int _fitReferencePage = -1;

    private bool _fitRepickReference = true;

    /// <summary>
    /// The scroll viewer's vertical bar, looked up once per template.
    /// </summary>
    private ScrollBar? _verticalScrollBar;

    /// <summary>
    /// How the zoom is chosen. In a fit mode the control keeps the reference page fitted as the
    /// viewer changes size; a zoom picked by hand (wheel, pinch) switches back to
    /// <see cref="ZoomMode.Manual"/>.
    /// </summary>
    public ZoomMode ZoomMode
    {
        get => GetValue(ZoomModeProperty);
        set => SetValue(ZoomModeProperty, value);
    }

    private void OnZoomModeChanged(ZoomMode mode)
    {
        if (mode == ZoomMode.Manual)
        {
            _isExplicitFitPending = false;
            return;
        }

        // A mode arriving with a tab switch (the binding re-reads the newly shown document) is
        // the document's state, not the user's pick: it waits for the position restore and
        // keeps the restored position unless the window size now asks for another zoom.
        _fitRepickReference = true;
        RequestFit(isExplicit: !_pendingScrollToPage);
    }

    /// <summary>
    /// Pages arriving, a page's size arriving or its rotation, a display mode switch: the extent
    /// changes. A viewport-only change (a scroll bar showing or hiding) never re-fits, which is
    /// what keeps a fit from chasing its own scroll bars.
    /// </summary>
    private void OnScrollChangedForFit(ScrollChangedEventArgs e)
    {
        if (Math.Abs(e.ExtentDelta.X) > 0.01 || Math.Abs(e.ExtentDelta.Y) > 0.01)
        {
            RequestFit(isExplicit: false);
        }
    }

    /// <summary>
    /// The viewer's own size changed (window resize, side pane, tab torn off): re-fit, from the
    /// page now current.
    /// </summary>
    private void OnViewerSizeChangedForFit()
    {
        _fitRepickReference = true;
        RequestFit(isExplicit: false, forNewViewerSize: true);
    }

    /// <summary>
    /// A document's saved position was restored (tab switch, opening, display mode switch): re-fit
    /// from the restored page if the viewer now asks for another zoom.
    /// </summary>
    private void OnPositionRestoredForFit()
    {
        _fitRepickReference = true;
        RequestFit(isExplicit: false);
    }

    /// <summary>
    /// A page's content box became known (its picture was rendered) or changed with container
    /// recycling. Not filtered by page: the fit is recomputed from the reference page only, so a
    /// neighbour's box changes nothing.
    /// </summary>
    private void OnPageContentBoundsChanged(object? sender, RoutedEventArgs e)
    {
        if (ZoomFit.IsContentMode(ZoomMode))
        {
            RequestFit(isExplicit: false);
        }
    }

    private void ResetFitState()
    {
        _isExplicitFitPending = false;
        _fitRepickReference = true;
    }

    /// <summary>
    /// Single-page view: fits the page <see cref="GoToPage(int, double?, double?, PageOffsetSpace)"/>
    /// is about to land on, then replaces a <see cref="PageOffsetSpace.Display"/> landing with the
    /// mode's: fit content centres the content box, the width modes line the page (or its content
    /// box) up with the viewport's left edge. Targets in another space (a search hit, a bookmark)
    /// are the caller's and are kept.
    /// </summary>
    private void FitBeforeLanding(int pageNumber, ref double? yOffset, ref double? xOffset, ref PageOffsetSpace space)
    {
        if (ZoomMode == ZoomMode.Manual || LayoutTransform is null ||
            ContainerFromIndex(pageNumber - 1) is not PageItem page)
        {
            return;
        }

        if (TryGetFitInput(page, out var input) && ZoomFit.TryCompute(input, out double zoom))
        {
            double currentZoom = LayoutTransform.LayoutTransform?.Value.M11 ?? 1.0;
            if (Math.Abs(zoom - currentZoom) > currentZoom * RelativeZoomTolerance)
            {
                // May not apply while another zoom settles (key auto-repeat): the extent change of
                // the turn then re-fits once it has.
                _zoomPanController.TryZoomTo(zoom, keepTopInPlace: true);
            }
        }

        if (space != PageOffsetSpace.Display)
        {
            return;
        }

        Rect? content = page.ContentBounds is { Width: > 0, Height: > 0 } box ? box : null;
        switch (ZoomMode)
        {
            case ZoomMode.FitContent when content.HasValue:
            {
                Point centre = content.Value.Center;
                (yOffset, xOffset, space) = (centre.Y, centre.X, PageOffsetSpace.UnrotatedPage);
                break;
            }

            case ZoomMode.FitContentWidth when content.HasValue:
            {
                double appliedZoom = LayoutTransform.LayoutTransform?.Value.M11 ?? 1.0;
                Rect displayContent = ToDisplayRect(page.Rotation, GetAlignedBounds(page).Size, content.Value);
                xOffset ??= displayContent.Left - ZoomFit.FitMargin / appliedZoom;
                break;
            }

            default:
                xOffset ??= 0;
                break;
        }
    }

    /// <summary>
    /// Requests a fit, coalescing requests until it runs. It runs from the next
    /// <see cref="Layoutable.LayoutUpdated"/>: after the layout pass that brings the change, and
    /// before the frame showing it is rendered. The arrange invalidated here makes sure that a
    /// layout pass comes, as the request may come from outside layout, or from a LayoutUpdated
    /// handler run after this control's (the scroll viewer raises ScrollChanged from its own).
    /// </summary>
    /// <param name="isExplicit"><c>true</c> when the user picked the mode: the view then lands on
    /// the page even if the zoom does not change. Otherwise the view only moves when the zoom does.</param>
    /// <param name="forNewViewerSize"><c>true</c> when the viewer's size changed.</param>
    private void RequestFit(bool isExplicit, bool forNewViewerSize = false)
    {
        if (ZoomMode == ZoomMode.Manual)
        {
            return;
        }

        _isExplicitFitPending |= isExplicit;
        _isFitForNewViewerSize |= forNewViewerSize;

        if (_isFitScheduled)
        {
            return;
        }

        _isFitScheduled = true;
        InvalidateArrange();
    }

    private void OnLayoutUpdatedForFit(object? sender, EventArgs e) => RunFit(afterLayout: true);

    /// <summary>
    /// Runs the requested fit, if any.
    /// </summary>
    /// <param name="afterLayout"><c>true</c> when run from <see cref="Layoutable.LayoutUpdated"/>:
    /// the layout pass has laid out any zoom still in flight.</param>
    private void RunFit(bool afterLayout)
    {
        if (!_isFitScheduled)
        {
            return;
        }

        if (ZoomMode == ZoomMode.Manual)
        {
            _isFitScheduled = false;
            _isFitForNewViewerSize = false;
            _isExplicitFitPending = false;
            return;
        }

        // A zoom in flight settles at Loaded priority, after the frame: wait for it, once, as running
        // from layout passes, which the zoom itself brings, would come back here again and again. A
        // new viewer size zooms at once from a layout pass, so that a window resize step coming before
        // the last step's zoom has settled is fitted in its frame too: a zoom never changes the
        // viewer's size, so that cannot feed itself.
        if (_zoomPanController.IsZooming && !(afterLayout && _isFitForNewViewerSize))
        {
            if (!_isFitWaitingForZoom)
            {
                _isFitWaitingForZoom = true;
                Dispatcher.UIThread.Post(() =>
                {
                    _isFitWaitingForZoom = false;
                    RunFit(afterLayout: false);
                }, DispatcherPriority.Loaded);
            }

            return;
        }

        _isFitScheduled = false;
        _isFitForNewViewerSize = false;

        // Not ready yet: the next trigger (layout, restore, size change) runs it again.
        if (Scroll is null || LayoutTransform is null || _pendingScrollToPage || ItemCount == 0)
        {
            return;
        }

        int pageNumber = PickReferencePage();
        if (pageNumber < 1 || ContainerFromIndex(pageNumber - 1) is not PageItem page ||
            !TryGetFitInput(page, out var input) || !ZoomFit.TryCompute(input, out double zoom))
        {
            return;
        }

        double currentZoom = LayoutTransform.LayoutTransform?.Value.M11 ?? 1.0;
        bool zoomChanges = Math.Abs(zoom - currentZoom) > currentZoom * RelativeZoomTolerance;
        if (zoomChanges && !_zoomPanController.TryZoomTo(zoom, keepTopInPlace: true, afterLayout))
        {
            return;
        }

        bool isExplicit = _isExplicitFitPending;
        _isExplicitFitPending = false;

        if (isExplicit || zoomChanges)
        {
            PositionAfterFit(pageNumber, page, LayoutTransform.LayoutTransform?.Value.M11 ?? zoom);
        }
    }

    /// <summary>
    /// The page the fit is computed from. Starts at 1; -1 if none.
    /// </summary>
    private int PickReferencePage()
    {
        if (IsSinglePage)
        {
            int realized = _visibilityTracker.GetFirstRealizedIndex();
            return realized >= 0 ? realized + 1 : -1;
        }

        if (_fitRepickReference || _fitReferencePage < 1 || _fitReferencePage > ItemCount)
        {
            _fitReferencePage = SelectedPageNumber ?? -1;
            _fitRepickReference = false;
        }

        return _fitReferencePage;
    }

    /// <summary>
    /// Gathers what <see cref="ZoomFit.TryCompute"/> needs from the current layout.
    /// </summary>
    private bool TryGetFitInput(PageItem page, out ZoomFitInput input)
    {
        input = default;

        if (Scroll is null || LayoutTransform is null)
        {
            return false;
        }

        double zoom = LayoutTransform.LayoutTransform?.Value.M11 ?? 1.0;
        Rect bounds = GetAlignedBounds(page);
        if (zoom <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return false;
        }

        // The page gap is a bottom margin of gap / zoom (see ZoomToPageSpacingConverter).
        double unscaledGap = page.Margin.Bottom;
        int itemCount = IsSinglePage ? 1 : ItemCount; // Single-page view lays out one page and its gap

        input = new ZoomFitInput(
            Mode: ZoomMode,
            Viewport: Scroll.Bounds.Size.Deflate(Scroll.Padding),
            ScrollBarThickness: GetScrollBarThickness(),
            Page: bounds.Size,
            Content: page.ContentBounds is { } content ? ToDisplayRect(page.Rotation, bounds.Size, content) : null,
            DocumentHeight: Math.Max(0, Scroll.Extent.Height / zoom - itemCount * unscaledGap),
            GapCount: itemCount,
            Gap: unscaledGap * zoom,
            MinZoom: MinZoomLevel,
            MaxZoom: MaxZoomLevel);

        return true;
    }

    /// <summary>
    /// The room a shown scroll bar takes from the viewport. 0 when the bars overlay the content
    /// (<see cref="ScrollViewer.AllowAutoHide"/>). The bar's own width while it shows, else its
    /// minimum width, which the Fluent theme sets to its size even while it is hidden.
    /// </summary>
    private double GetScrollBarThickness()
    {
        if (Scroll is null || Scroll.AllowAutoHide)
        {
            return 0;
        }

        _verticalScrollBar ??= Scroll.GetTemplateDescendants()
            .OfType<ScrollBar>()
            .FirstOrDefault(bar => bar.Orientation == Orientation.Vertical);

        if (_verticalScrollBar is not { } scrollBar)
        {
            return 0;
        }

        return scrollBar.IsVisible && scrollBar.Bounds.Width > 0 ? scrollBar.Bounds.Width : scrollBar.MinWidth;
    }

    /// <summary>
    /// Lands the view on the fitted page: the page modes show the page from its top (fit page) or
    /// keep the reading position (fit width); the content modes put the content box in view.
    /// </summary>
    private void PositionAfterFit(int pageNumber, PageItem page, double zoom)
    {
        Rect? content = page.ContentBounds is { Width: > 0, Height: > 0 } box ? box : null;

        switch (ZoomMode)
        {
            case ZoomMode.FitContent when content.HasValue:
            {
                // The GoToWord path: centres a point of the unrotated page in the viewport.
                Point centre = content.Value.Center;
                GoToPage(pageNumber, centre.Y, centre.X, PageOffsetSpace.UnrotatedPage);
                break;
            }

            case ZoomMode.FitContentWidth when content.HasValue:
            {
                // Horizontal only, like fit width: GoToPage would first bring the whole page into
                // view, losing the reading position the zoom kept.
                Rect displayContent = ToDisplayRect(page.Rotation, GetAlignedBounds(page).Size, content.Value);
                AlignWithFitMargin(pageNumber, page, displayContent.Left, zoom);
                break;
            }

            case ZoomMode.FitPage:
            case ZoomMode.FitContent:
                GoToPage(pageNumber, 0);
                AlignWithFitMargin(pageNumber, page, 0, zoom);
                break;

            default:
                // Fit width (or fit content width without a content box): horizontal only.
                AlignWithFitMargin(pageNumber, page, 0, zoom);
                break;
        }
    }

    /// <summary>
    /// Scrolls horizontally so that <paramref name="left"/>, a position on the page as displayed
    /// (unscaled), sits <see cref="ZoomFit.FitMargin"/> from the viewport's left edge, as far as
    /// the scroll range allows. A page narrower than the viewport cannot scroll: the scroll viewer
    /// centres it, which splits the margin between its sides.
    /// </summary>
    private void AlignWithFitMargin(int pageNumber, PageItem page, double left, double zoom)
    {
        // Relative to the document: an offset relative to the page stops at the page's left edge,
        // and the margin left of the page shows when a wider page makes the document scroll.
        ApplyScrollOffsets(pageNumber, null, GetAlignedBounds(page).Left + left - ZoomFit.FitMargin / zoom,
            PageOffsetSpace.Display);
    }
}
