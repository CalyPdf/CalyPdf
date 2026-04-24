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
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Caly.Core.Services.Rendering;
using Caly.Core.Utilities;
using SkiaSharp;

namespace Caly.Core.Controls.Rendering;

/// <summary>
/// Renders PDF pages using pre-rendered bitmap tiles for efficient zooming and scrolling.
/// </summary>
public sealed partial class TiledPdfPageControl : Control
{
    /// <summary>
    /// Tile column/row range at a given tile level. Used to compare the range
    /// produced by the current <see cref="VisibleArea"/> against the range the
    /// last render produced, so we can skip invalidation when they match.
    /// </summary>
    private readonly record struct TileRange(int TileLevel, int StartCol, int StartRow, int EndCol, int EndRow);

    /// <summary>
    /// A single tile entry for the draw operation, holding a cloned image reference,
    /// the source rect within the image, and its destination rect on the canvas.
    /// </summary>
    private readonly struct TileDrawEntry : IDisposable
    {
        public IRef<SKImage> ImageRef { get; }

        /// <summary>
        /// Source rectangle within the image. For exact-level tiles this is the full image.
        /// For lower-level fallback tiles this is a sub-region that covers the missing tile's area.
        /// </summary>
        public SKRect SrcRect { get; }

        public SKRect DestRect { get; }

        public bool CanRender { get; }

        public TileDrawEntry(IRef<SKImage> imageRef, SKRect srcRect, SKRect destRect)
        {
            ImageRef = imageRef;
            SrcRect = srcRect;
            DestRect = destRect;

            // BytesSize of 1 means it's empty
            CanRender = ImageRef is { IsAlive: true, Item.Info.BytesSize: > 1 };
        }

        public void Dispose() => ImageRef.Dispose();
    }
    
    public static readonly StyledProperty<double> PpiScaleProperty =
        AvaloniaProperty.Register<TiledPdfPageControl, double>(nameof(PpiScale));

    public static readonly StyledProperty<double> ZoomLevelProperty =
        AvaloniaProperty.Register<TiledPdfPageControl, double>(nameof(ZoomLevel), 1.0);

    public static readonly StyledProperty<IRef<SKPicture>?> PictureProperty =
        AvaloniaProperty.Register<TiledPdfPageControl, IRef<SKPicture>?>(nameof(Picture));

    public static readonly StyledProperty<Rect?> VisibleAreaProperty =
        AvaloniaProperty.Register<TiledPdfPageControl, Rect?>(nameof(VisibleArea));

    public static readonly StyledProperty<bool> IsPageVisibleProperty =
        AvaloniaProperty.Register<TiledPdfPageControl, bool>(nameof(IsPageVisible));

    public static readonly StyledProperty<int> PageNumberProperty =
        AvaloniaProperty.Register<TiledPdfPageControl, int>(nameof(PageNumber));

    public static readonly StyledProperty<Size> PageDisplaySizeProperty =
        AvaloniaProperty.Register<TiledPdfPageControl, Size>(nameof(PageDisplaySize));

    public static readonly StyledProperty<TileRenderService?> TileRenderServiceProperty =
        AvaloniaProperty.Register<TiledPdfPageControl, TileRenderService?>(nameof(TileRenderService));

    public double PpiScale
    {
        get => GetValue(PpiScaleProperty);
        set => SetValue(PpiScaleProperty, value);
    }

    public double ZoomLevel
    {
        get => GetValue(ZoomLevelProperty);
        set => SetValue(ZoomLevelProperty, value);
    }

    public IRef<SKPicture>? Picture
    {
        get => GetValue(PictureProperty);
        set => SetValue(PictureProperty, value);
    }

    public Rect? VisibleArea
    {
        get => GetValue(VisibleAreaProperty);
        set => SetValue(VisibleAreaProperty, value);
    }

    public bool IsPageVisible
    {
        get => GetValue(IsPageVisibleProperty);
        set => SetValue(IsPageVisibleProperty, value);
    }

    public int PageNumber
    {
        get => GetValue(PageNumberProperty);
        set => SetValue(PageNumberProperty, value);
    }

    public Size PageDisplaySize
    {
        get => GetValue(PageDisplaySizeProperty);
        set => SetValue(PageDisplaySizeProperty, value);
    }

    public TileRenderService? TileRenderService
    {
        get => GetValue(TileRenderServiceProperty);
        set => SetValue(TileRenderServiceProperty, value);
    }

    private int _invalidateScheduled;

    /// <summary>
    /// Cached page number for thread-safe access from the <see cref="OnTileReady"/> callback,
    /// which fires on a background thread and cannot read styled properties.
    /// </summary>
    private volatile int _cachedPageNumber;
    
    /// <summary>
    /// Number of extra tile rows/columns rendered beyond the visible area, so
    /// short scrolls don't immediately reveal unrendered regions.
    /// </summary>
    private const int RenderTileMargin = 1;
    
    /// <summary>
    /// How long after the last zoom/scroll event before the control switches back to
    /// <see cref="IdleSamplingOptions"/>. Short enough to feel instantaneous once motion
    /// stops; long enough to ride out per-frame bursts during continuous scrolling.
    /// </summary>
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// True while the control is in an "interactive" state — zoom or visible-area
    /// changes have fired recently and we're still within <see cref="SettleDelay"/> of
    /// the last one. Drives the sampling choice in <see cref="Render"/>.
    /// Only accessed on the UI thread.
    /// </summary>
    private bool _isInteracting;

    /// <summary>
    /// Dispatcher timer that flips <see cref="_isInteracting"/> back to false and triggers
    /// a repaint at <see cref="IdleSamplingOptions"/> quality. Lazily created on first
    /// interaction so pages that never scroll/zoom don't pay for the timer.
    /// </summary>
    private DispatcherTimer? _settleTimer;
    
    static TiledPdfPageControl()
    {
        ClipToBoundsProperty.OverrideDefaultValue<TiledPdfPageControl>(true);
        IsHitTestVisibleProperty.OverrideDefaultValue<TiledPdfPageControl>(false);

        AffectsRender<TiledPdfPageControl>(PictureProperty, IsPageVisibleProperty, ZoomLevelProperty);
        AffectsMeasure<TiledPdfPageControl>(PictureProperty);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == PageNumberProperty)
        {
            _cachedPageNumber = change.GetNewValue<int>();
        }
        else if (change.Property == TileRenderServiceProperty)
        {
            if (change.OldValue is TileRenderService oldService)
            {
                oldService.TileReady -= OnTileReady;
            }

            if (change.NewValue is TileRenderService newService)
            {
                newService.TileReady -= OnTileReady;
                newService.TileReady += OnTileReady;
                // Service just became available — request tiles if VisibleArea is already set.
                RequestVisibleTiles();
            }
        }
        else if (change.Property == VisibleAreaProperty)
        {
            if (!this.IsAttachedToVisualTree())
            {
                return;
            }

            // VisibleArea is not in AffectsRender: small scrolls shift the area
            // without changing which tiles are drawn, so we'd redraw for nothing.
            // Only prefetch + invalidate when the computed tile range actually differs.
            MarkInteracting();
            HandleVisibleAreaChanged();
        }
        else if (change.Property == ZoomLevelProperty)
        {
            if (!this.IsAttachedToVisualTree())
            {
                return;
            }

            // AffectsRender handles the redraw; we only need to queue missing tiles.
            MarkInteracting();
            RequestVisibleTiles();
        }
        else if (change.Property == PictureProperty)
        {
            // Invalidate the cached range so HandleVisibleAreaChanged can't skip repaints
            // by matching against the previous picture's range.
            _lastRenderedTileRangeValid = false;

            if (!this.IsAttachedToVisualTree())
            {
                return;
            }

            RequestVisibleTiles();
        }
    }

    /// <summary>
    /// Snapshots what is needed to compute and queue missing tiles, then hands the work off to the
    /// thread pool. Runs on the UI thread, so the only per-call work here must be a few value-type
    /// reads and one <see cref="IRef{T}.Clone"/>. The cache lookup and channel writes happen on the
    /// background thread — doing them inline on the UI thread makes scrolling stutter because each
    /// scroll pixel bursts many locked cache operations and prioritized channel inserts.
    /// </summary>
    private void RequestVisibleTiles()
    {
        if (!VisibleArea.HasValue || VisibleArea.Value.IsEmpty())
        {
            return;
        }

        var service = TileRenderService;
        var pageDisplaySize = PageDisplaySize;
        if (service is null || pageDisplaySize.Width <= 0 || pageDisplaySize.Height <= 0)
        {
            return;
        }

        var picture = Picture;
        if (picture?.IsAlive != true)
        {
            return;
        }

        IRef<SKPicture> pictureClone;
        try
        {
            pictureClone = picture.Clone();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        var workItem = new PrefetchWorkItem(
            service,
            PageNumber,
            TileGrid.ComputeTileLevel(ZoomLevel),
            VisibleArea.Value,
            pageDisplaySize,
            PpiScale,
            pictureClone);

        ThreadPool.UnsafeQueueUserWorkItem(workItem, preferLocal: false);
    }

    /// <summary>
    /// Expands the visible area into a tile range, asks the cache which tiles are missing, and
    /// submits a batch request to the render service. Runs on a thread pool thread so the UI
    /// thread is never blocked on cache locks or channel inserts.
    /// </summary>
    private sealed class PrefetchWorkItem : IThreadPoolWorkItem
    {
        private readonly TileRenderService _service;
        private readonly int _pageNumber;
        private readonly int _tileLevel;
        private readonly Rect _visibleArea;
        private readonly Size _pageDisplaySize;
        private readonly double _ppiScale;
        private readonly IRef<SKPicture> _picture;

        public PrefetchWorkItem(TileRenderService service, int pageNumber, int tileLevel,
            Rect visibleArea, Size pageDisplaySize, double ppiScale, IRef<SKPicture> picture)
        {
            _service = service;
            _pageNumber = pageNumber;
            _tileLevel = tileLevel;
            _visibleArea = visibleArea;
            _pageDisplaySize = pageDisplaySize;
            _ppiScale = ppiScale;
            _picture = picture;
        }

        public void Execute()
        {
            try
            {
                GetTileRange(_visibleArea, _pageDisplaySize, _tileLevel, RenderTileMargin,
                    out int startCol, out int startRow, out int endCol, out int endRow);

                var missing = new List<TileCoord>();
                _service.Cache.FindMissing(_pageNumber, _tileLevel, startCol, startRow, endCol, endRow, missing);

                if (missing.Count > 0)
                {
                    _service.RequestTiles(_pageNumber, _picture, _tileLevel,
                        CollectionsMarshal.AsSpan(missing), _ppiScale, _pageDisplaySize, _visibleArea);
                }
            }
            catch (Exception e)
            {
                Debug.WriteExceptionToFile(e);
            }
            finally
            {
                _picture.Dispose();
            }
        }
    }

    /// <summary>
    /// Handles a change to <see cref="VisibleAreaProperty"/>. Since the property is not
    /// in <see cref="AffectsRender"/>, this computes the current tile range once and:
    /// <list type="bullet">
    ///   <item>skips both prefetch and invalidate when the range matches the last render
    ///     (sub-tile-boundary scroll — no visible change),</item>
    ///   <item>otherwise queues a prefetch for missing tiles and invalidates the visual.</item>
    /// </list>
    /// Sharing the computed range between the skip check and the subsequent prefetch
    /// avoids queueing a thread-pool work item that would just recompute the same range
    /// and exit after finding no missing tiles.
    /// </summary>
    private void HandleVisibleAreaChanged()
    {
        Debug.ThrowNotOnUiThread();

        var visibleArea = VisibleArea;
        var pageDisplaySize = PageDisplaySize;

        if (!visibleArea.HasValue || visibleArea.Value.IsEmpty()
            || pageDisplaySize.Width <= 0 || pageDisplaySize.Height <= 0)
        {
            // Render() will take the base/empty path. Only invalidate if the previous
            // render drew content — otherwise the scene is already in the right state.
            if (_lastRenderedTileRangeValid)
            {
                InvalidateVisual();
            }
            return;
        }

        int tileLevel = TileGrid.ComputeTileLevel(ZoomLevel);
        var range = GetTileRange(visibleArea.Value, in pageDisplaySize, tileLevel, RenderTileMargin);

        if (_lastRenderedTileRangeValid && _lastRenderedTileRange == range)
        {
            // Same tile range as the last render — no new tiles to request and
            // the existing scene is still correct. Skip both prefetch and repaint.
            return;
        }

        RequestVisibleTiles();
        InvalidateVisual();
    }

    /// <summary>
    /// Computes the tile column/row range for the given visible area, expanded by a margin
    /// and clamped to the grid dimensions.
    /// </summary>
    private static void GetTileRange(in Rect visibleArea, in Size pageDisplaySize, int tileLevel, int margin,
        out int startCol, out int startRow, out int endCol, out int endRow)
    {
        var gridDims = TileGrid.GetGridDimensions(in pageDisplaySize, tileLevel);
        double tileDisplaySize = TileGrid.TilePixelSize / TileGrid.GetTileLevelScale(tileLevel);

        startCol = Math.Max(0, (int)(visibleArea.Left / tileDisplaySize) - margin);
        startRow = Math.Max(0, (int)(visibleArea.Top / tileDisplaySize) - margin);
        endCol = Math.Min(gridDims.Width - 1, (int)Math.Ceiling(visibleArea.Right / tileDisplaySize) - 1 + margin);
        endRow = Math.Min(gridDims.Height - 1, (int)Math.Ceiling(visibleArea.Bottom / tileDisplaySize) - 1 + margin);
    }

    /// <summary>
    /// Computes the tile column/row range for the given visible area, expanded by a margin
    /// and clamped to the grid dimensions.
    /// </summary>
    private static TileRange GetTileRange(in Rect visibleArea, in Size pageDisplaySize, int tileLevel, int margin)
    {
        GetTileRange(in visibleArea, in pageDisplaySize, tileLevel, margin,
            out int startCol, out int startRow, out int endCol, out int endRow);
        return new TileRange(tileLevel, startCol, startRow, endCol, endRow);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (TileRenderService is not null)
        {
            TileRenderService.TileReady -= OnTileReady;
            TileRenderService.TileReady += OnTileReady;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (TileRenderService is not null)
        {
            TileRenderService.TileReady -= OnTileReady;
            TileRenderService.CancelPage(PageNumber);
        }

        _settleTimer?.Stop();
        _isInteracting = false;
    }

    /// <summary>
    /// Flags the control as actively interacting (zoom/scroll in flight) and (re)starts the
    /// settle timer. While the flag is set, <see cref="Render"/> uses
    /// <see cref="InteractiveSamplingOptions"/>; when the timer fires without another
    /// interaction resetting it, <see cref="OnSettleTick"/> flips back and triggers a
    /// high-quality repaint.
    /// </summary>
    private void MarkInteracting()
    {
        _isInteracting = true;
        var timer = _settleTimer ??= CreateSettleTimer();
        timer.Stop();
        timer.Start();
    }

    private DispatcherTimer CreateSettleTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = SettleDelay };
        timer.Tick += OnSettleTick;
        return timer;
    }

    private void OnSettleTick(object? sender, EventArgs e)
    {
        _settleTimer!.Stop();
        if (_isInteracting)
        {
            _isInteracting = false;
            InvalidateVisual();
        }
    }

    private void OnTileReady(TileKey key)
    {
        if (key.PageNumber != _cachedPageNumber)
        {
            return;
        }

        // Coalesce invalidation requests to avoid flooding the UI thread
        if (Interlocked.Exchange(ref _invalidateScheduled, 1) == 0)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Interlocked.Exchange(ref _invalidateScheduled, 0);
                InvalidateVisual();
            }, DispatcherPriority.Render);
        }
    }
}
