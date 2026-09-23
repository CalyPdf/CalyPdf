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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Caly.Core.Utilities;

namespace Caly.Core.Controls;

/// <summary>
/// Handles zooming (ctrl+wheel, pinch, and external <see cref="PageItemsControl.ZoomLevel"/>
/// changes) and drag-panning on behalf of <see cref="PageItemsControl"/>, owning the
/// in-flight interaction state (<see cref="IsZooming"/>, <see cref="IsPinching"/>, the
/// pan position and the pinch zoom reference).
/// </summary>
internal sealed class ZoomPanController
{
    private const double ZoomFactor = 1.1;

    private readonly PageItemsControl _owner;

    private Point? _currentPosition;
    private double _pinchZoomReference = 1.0;

    public ZoomPanController(PageItemsControl owner)
    {
        ArgumentNullException.ThrowIfNull(owner, nameof(owner));
        _owner = owner;
    }

    /// <summary>
    /// <c>true</c> while a zoom transform change is in flight, including the layout
    /// pass it produces (cleared at Loaded priority, see <see cref="SetZoomFinished"/>).
    /// </summary>
    public bool IsZooming { get; private set; }

    public bool IsPinching { get; private set; }

    /// <summary>
    /// Clears the in-flight interaction state, e.g. when the owner's DataContext changes.
    /// </summary>
    public void Reset()
    {
        _currentPosition = null;
        IsZooming = false;
        IsPinching = false;
    }

    #region Zoom

    /// <summary>
    /// Applies a <see cref="PageItemsControl.ZoomLevel"/> change that did not originate
    /// from this controller's own zoom handlers (e.g. the view model's zoom in/out
    /// commands), zooming around the viewport centre. Changes produced by the
    /// wheel/pinch/programmatic handlers have already updated the layout transform, so
    /// the scale comparison below turns them into no-ops.
    /// <para>
    /// This is called from <see cref="PageItemsControl"/>'s <c>OnPropertyChanged</c>, which
    /// runs before bindings to <see cref="PageItemsControl.ZoomLevel"/> are notified, so the
    /// zoom-dependent page gap margin (see <see cref="Converters.ZoomToPageSpacingConverter"/>)
    /// still holds its old value. The zoom is therefore applied at
    /// <see cref="DispatcherPriority.Send"/>, once bindings are up to date but still ahead of
    /// the next layout pass.
    /// </para>
    /// </summary>
    public void HandleExternalZoomLevelChanged()
    {
        Dispatcher.UIThread.Post(ApplyExternalZoomLevel, DispatcherPriority.Send);
    }

    private void ApplyExternalZoomLevel()
    {
        var layoutTransform = _owner.LayoutTransform;
        if (layoutTransform is null || !layoutTransform.IsAttachedToVisualTree())
        {
            return;
        }

        double newZoom = _owner.ZoomLevel;
        double currentScale = layoutTransform.LayoutTransform?.Value.M11 ?? 1.0;
        if (Math.Abs(currentScale - newZoom) < 1e-9)
        {
            return; // Ignore as no change in zoom level
        }

        double dZoom = newZoom / currentScale;

        double w = 0, h = 0;
        if (!_owner.DesiredSize.IsEmpty())
        {
            _owner.DesiredSize.Deconstruct(out w, out h);
        }
        else if (!_owner.Bounds.Size.IsEmpty())
        {
            _owner.Bounds.Size.Deconstruct(out w, out h);
        }

        var pixelPoint = _owner.PointToScreen(new Point((int)(w / 2.0), (int)(h / 2.0)));
        var point = layoutTransform.PointToClient(pixelPoint);
        ZoomTo(dZoom, point);
    }

    /// <summary>
    /// Tunnel handler for ctrl+wheel zooming, subscribed on the owner.
    /// </summary>
    public void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
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
        if (_owner.LayoutTransform is null)
        {
            return;
        }

        if (IsZooming)
        {
            return;
        }

        try
        {
            IsZooming = true;
            double dZoom = Math.Round(Math.Pow(ZoomFactor, e.Delta.Y), 4); // If IsScrollInertiaEnabled = false, Y is only 1 or -1
            ZoomToInternal(dZoom, e.GetPosition(_owner.LayoutTransform));
        }
        finally
        {
            SetZoomFinished();
        }
    }

    private void ZoomTo(double dZoom, Point point)
    {
        if (_owner.LayoutTransform is null || _owner.Scroll is null)
        {
            return;
        }

        if (IsZooming)
        {
            return;
        }

        try
        {
            IsZooming = true;
            ZoomToInternal(dZoom, point);
        }
        finally
        {
            SetZoomFinished();
        }
    }

    private void SetZoomFinished()
    {
        // ZoomToInternal positions the offset around the zoom origin itself, so
        // suppress the auto-anchor in AdjustXOffsetOnExtentChanged for the layout/
        // scroll events this transform change is about to produce. Setting to 'false'
        // is posted at Loaded priority so it runs after the layout pass that
        // updates Scroll.Extent.
        //_isZooming = false;
        Dispatcher.UIThread.Post(() =>
        {
            IsZooming = false;
        }, DispatcherPriority.Loaded);
    }

    private void ZoomToInternal(double dZoom, Point point)
    {
        var layoutTransform = _owner.LayoutTransform;
        var scroll = _owner.Scroll;
        if (layoutTransform is null || scroll is null)
        {
            return;
        }

        double oldZoom = layoutTransform.LayoutTransform?.Value.M11 ?? 1.0;
        double newZoom = oldZoom * dZoom;

        if (newZoom < _owner.MinZoomLevel)
        {
            if (oldZoom.Equals(_owner.MinZoomLevel))
            {
                return;
            }

            newZoom = _owner.MinZoomLevel;
            dZoom = newZoom / oldZoom;
        }
        else if (newZoom > _owner.MaxZoomLevel)
        {
            if (oldZoom.Equals(_owner.MaxZoomLevel))
            {
                return;
            }

            newZoom = _owner.MaxZoomLevel;
            dZoom = newZoom / oldZoom;
        }

        // Capture the zoom origin relative to the page under it before the layout changes.
        var anchor = GetPageAnchor(layoutTransform, point);
        double viewportY = point.Y - scroll.Offset.Y;

        var builder = TransformOperations.CreateBuilder(1);
        builder.AppendScale(newZoom, newZoom);
        layoutTransform.LayoutTransform = builder.Build();

        // Publish the new zoom level now, so zoom-dependent page layout (the page gap
        // margin, see ZoomToPageSpacingConverter) changes in the same layout pass as
        // the transform. A no-op when the change came from ZoomLevel itself.
        double appliedZoom = layoutTransform.LayoutTransform?.Value.M11 ?? newZoom;
        if (Math.Abs(_owner.ZoomLevel - appliedZoom) > 1e-9)
        {
            _owner.SetCurrentValue(PageItemsControl.ZoomLevelProperty, appliedZoom);
        }

        var offset = scroll.Offset - GetOffset(dZoom, point.X, point.Y);

        // Lay out now so the scroll extent is up to date before setting the offset
        // (which is coerced against it), and so the anchor page has its new position.
        // Only the scroll viewer is measured and arranged, rather than a full UpdateLayout():
        // that would also update the panel's effective viewport with the old offset at the
        // new scale which, deep in a document, is many pages away from the zoom origin. The
        // panel would then recycle the visible pages' containers onto those pages and back
        // again once the offset is set (flicker). Here the panel keeps its pre-zoom viewport,
        // which covers the pages around the zoom origin.
        // The chain from the panel up is invalidated explicitly: outside a layout pass, only
        // the pages (margin) and the layout transform control are invalid, so measuring the
        // scroll viewer with an unchanged constraint would otherwise be a no-op.
        for (var visual = _owner.ItemsPanelRoot as Visual; visual is not null && visual != scroll; visual = visual.GetVisualParent())
        {
            (visual as Layoutable)?.InvalidateMeasure();
        }

        scroll.InvalidateMeasure();
        scroll.Measure(LayoutInformation.GetPreviousMeasureConstraint(scroll) ?? scroll.Bounds.Size);
        scroll.Arrange(LayoutInformation.GetPreviousArrangeBounds(scroll) ?? scroll.Bounds);

        // The page gap changes every page's size in panel space, which makes the
        // VirtualizingStackPanel re-estimate page positions (as index * average size).
        // Content positions therefore do not simply scale with the zoom, so re-anchor
        // vertically on the page that was under the zoom origin.
        if (anchor.HasValue && GetAnchorContentY(layoutTransform, anchor.Value) is { } anchorY)
        {
            offset = offset.WithY(anchorY - viewportY);
        }

        scroll.SetCurrentValue(ScrollViewer.OffsetProperty, offset);
    }

    /// <summary>
    /// Finds the realized page under <paramref name="point"/> (in <paramref name="layoutTransform"/>
    /// coordinates) and returns its index and the unscaled Y of the point within that page.
    /// The gap below a page counts as part of that page, and the horizontal position is
    /// ignored, so zooming with the pointer beside or between pages still anchors.
    /// </summary>
    private (int Index, double YInPage)? GetPageAnchor(LayoutTransformControl layoutTransform, Point point)
    {
        if (_owner.Presenter is not { } presenter ||
            layoutTransform.TranslatePoint(point, presenter) is not { } presenterPoint ||
            _owner.GetRealizedPageItemAtY(presenterPoint.Y, out int index) is not { } pageItem)
        {
            return null;
        }

        return (index, presenterPoint.Y - pageItem.Bounds.Top);
    }

    /// <summary>
    /// Gets the current Y, in <paramref name="layoutTransform"/> coordinates, of the anchor
    /// captured by <see cref="GetPageAnchor"/>, realizing the anchor page if the re-layout
    /// recycled it.
    /// </summary>
    private double? GetAnchorContentY(LayoutTransformControl layoutTransform, (int Index, double YInPage) anchor)
    {
        var container = _owner.ContainerFromIndex(anchor.Index);
        if (container is null)
        {
            _owner.ScrollIntoView(anchor.Index);
            _owner.UpdateLayout();
            container = _owner.ContainerFromIndex(anchor.Index);
        }

        return container?.TranslatePoint(new Point(0, anchor.YInPage), layoutTransform)?.Y;
    }

    private static Vector GetOffset(double scale, double x, double y)
    {
        double s = 1 - scale;
        return new Vector(x * s, y * s);
    }

    #endregion

    #region Mobile handling

    public void OnHolding(object? sender, HoldingRoutedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"Holding {e.HoldingState}: {e.Position.X}, {e.Position.Y}");
    }

    public void OnPinchEnded(object? sender, PinchEndedEventArgs e)
    {
        _pinchZoomReference = _owner.ZoomLevel;
        IsPinching = false;
    }

    public void OnPinchChanged(object? sender, PinchEventArgs e)
    {
        if (!IsPinching)
        {
            // Capture the zoom level at the start of each new pinch gesture so the
            // first event doesn't compute dZoom against a stale reference of 1.0.
            _pinchZoomReference = _owner.ZoomLevel;
            IsPinching = true;
        }

        if (e.Scale != 0)
        {
            ZoomTo(e);
            e.Handled = true;
        }
    }

    private void ZoomTo(PinchEventArgs e)
    {
        if (_owner.LayoutTransform is null)
        {
            return;
        }

        if (IsZooming)
        {
            return;
        }

        try
        {
            IsZooming = true;

            // Pinch zoom always starts with a scale of 1, then increase/decrease until PinchEnded
            double dZoom = (e.Scale * _pinchZoomReference) / _owner.ZoomLevel;

            // TODO - Origin still not correct
            var point = _owner.LayoutTransform.PointToClient(new PixelPoint((int)e.ScaleOrigin.X, (int)e.ScaleOrigin.Y));
            ZoomToInternal(dZoom, point);
        }
        finally
        {
            SetZoomFinished();
        }
    }

    #endregion

    #region Pan

    public void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.IsPanning())
        {
            return;
        }

        var point = e.GetCurrentPoint(_owner);
        _currentPosition = point.Position;
        e.Handled = true;
    }

    public void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!e.IsPanningOrZooming())
        {
            return;
        }

        if (e.IsPanning())
        {
            _owner.SetPanCursor();
            PanTo(e);
        }

        e.Handled = true;
    }

    public void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        ResetPan();
    }

    public void ResetPan()
    {
        _currentPosition = null;
        _owner.SetDefaultCursor();
    }

    private void PanTo(PointerEventArgs e)
    {
        var scroll = _owner.Scroll;
        if (scroll is null)
        {
            return;
        }

        var point = e.GetCurrentPoint(_owner);

        if (!_currentPosition.HasValue)
        {
            _currentPosition = point.Position;
            return;
        }

        var delta = point.Position - _currentPosition;

        var offset = scroll.Offset - delta.Value;
        scroll.SetCurrentValue(ScrollViewer.OffsetProperty, offset);
        _currentPosition = point.Position;
    }

    #endregion
}
