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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Avalonia;
using Caly.Core.Utilities;
using SkiaSharp;

namespace Caly.Core.Services.Rendering;

/// <summary>
/// Background tile rendering service. One instance per document.
/// Renders SKPicture regions into tile-sized bitmaps on background threads.
/// </summary>
public sealed class TileRenderService : IAsyncDisposable
{
    private readonly struct TileRequest
    {
        public TileKey Key { get; }
        public IRef<SKPicture> Picture { get; }
        public double PpiScale { get; }
        public Size PageDisplaySize { get; }
        public CancellationToken Token { get; }

        /// <summary>
        /// Identifies this request among the ones that have held <see cref="Key"/> in
        /// <see cref="_inFlight"/>, so it only ever retires its own entry.
        /// </summary>
        public long RequestId { get; }

        /// <summary>
        /// Squared Euclidean distance (in tile units) from the tile's centre to the centre of the
        /// visible area at the time of the request. Primary sort key after <see cref="TileKey.PageNumber"/>:
        /// smaller = rendered sooner, so tiles fill outwards in rings from the middle of the viewport.
        /// </summary>
        public double DistSq { get; }

        /// <summary>
        /// Clockwise angle from the top (12 o'clock), in radians, in the range [0, 2π). Secondary
        /// tiebreaker for tiles that share a <see cref="DistSq"/>, so each ring fills clockwise from
        /// the top edge.
        /// </summary>
        public double AngleCw { get; }

        public TileRequest(in TileKey key, IRef<SKPicture> picture, double ppiScale, in Size pageDisplaySize,
            CancellationToken token, long requestId, double distSq, double angleCw)
        {
            Key = key;
            Picture = picture;
            PpiScale = ppiScale;
            PageDisplaySize = pageDisplaySize;
            Token = token;
            RequestId = requestId;
            DistSq = distSq;
            AngleCw = angleCw;
        }
    }

    private sealed class TileRequestComparer : IComparer<TileRequest>
    {
        public static readonly TileRequestComparer Instance = new();

        public int Compare(TileRequest x, TileRequest y)
        {
            // Order: page number first (so earlier pages finish before later ones), then distance
            // from the visible-area centre (rings outward), then clockwise angle from the top.
            // The net effect is that the middle of the viewport renders first and the ring around
            // it fills clockwise starting at 12 o'clock — the human eye is drawn to the centre, so
            // putting content there first makes scrolling feel more responsive than top-to-bottom.
            int cmp = x.Key.PageNumber.CompareTo(y.Key.PageNumber);
            if (cmp != 0)
            {
                return cmp;
            }

            cmp = x.DistSq.CompareTo(y.DistSq);
            if (cmp != 0)
            {
                return cmp;
            }

            return x.AngleCw.CompareTo(y.AngleCw);
        }
    }

    private readonly ChannelWriter<TileRequest> _requestWriter;
    private readonly ChannelReader<TileRequest> _requestReader;
    private readonly CancellationTokenSource _mainCts = new();
    private readonly CancellationToken _mainToken;
    private readonly Task _processingLoopTask;

    /// <summary>
    /// Tracks in-flight tile requests to avoid duplicate renders, mapping each key to the id of
    /// the request holding it.
    /// <para>
    /// An entry is a promise that a render for that key is on its way, so it only stands while
    /// that is still true: <see cref="CancelPage"/> and <see cref="InvalidatePage"/> retire the
    /// page's entries along with the requests they doom. Leaving them behind deduplicated the
    /// request that would have repaired the page against renders that were already going to be
    /// dropped, and the page stayed blank until a scroll asked for different tiles.
    /// </para>
    /// </summary>
    private readonly ConcurrentDictionary<TileKey, long> _inFlight = new();

    /// <summary>
    /// Source of the ids in <see cref="_inFlight"/>. Requests retire their entry by value, so a
    /// request dropped after its key has been re-taken cannot retire the new holder's entry.
    /// </summary>
    private long _nextRequestId;

    /// <summary>
    /// Per-page cancellation tokens for cancelling requests when pages scroll out of view.
    /// </summary>
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _pageTokens = new();

    /// <summary>
    /// Pool of reusable SKPaint instances to avoid allocating one per tile render.
    /// Bag size is bounded by the number of concurrent workers, since each rent is
    /// matched by a return on the same render call.
    /// </summary>
    private readonly ConcurrentBag<SKPaint> _paintPool = new();

    /// <summary>
    /// Fired when a tile has been rendered and is available in the cache.
    /// The handler receives the <see cref="TileKey"/> of the completed tile.
    /// This event may be raised from a background thread.
    /// </summary>
    public event Action<TileKey>? TileReady;

    /// <summary>
    /// Gets the tile cache used by this service.
    /// </summary>
    public TileCache Cache { get; }

    public TileRenderService() : this(new TileCache())
    {
    }

    public TileRenderService(TileCache cache) : this(cache, startProcessingLoop: true)
    {
    }

    /// <summary>
    /// Used by tests: with <paramref name="startProcessingLoop"/> set to <c>false</c>,
    /// queued requests are never consumed, deterministically reproducing disposal
    /// racing ahead of the render workers.
    /// </summary>
    internal TileRenderService(TileCache cache, bool startProcessingLoop)
    {
        _mainToken = _mainCts.Token;
        Cache = cache;

        var channel = Channel.CreateUnboundedPrioritized(new UnboundedPrioritizedChannelOptions<TileRequest>()
        {
            Comparer = TileRequestComparer.Instance,
            SingleWriter = false,
            SingleReader = false
        });

        _requestWriter = channel.Writer;
        _requestReader = channel.Reader;

        _processingLoopTask = startProcessingLoop ? Task.Run(ProcessingLoop) : Task.CompletedTask;
    }

    private async Task ProcessingLoop()
    {
        int maxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2);
        System.Diagnostics.Debug.WriteLine($"TileRenderService: MaxDegreeOfParallelism = {maxDegreeOfParallelism}");
        var options = new ParallelOptions()
        {
            MaxDegreeOfParallelism = maxDegreeOfParallelism,
            CancellationToken = _mainToken
        };

        try
        {
            await Parallel.ForEachAsync(_requestReader.ReadAllAsync(_mainToken), options, (request, ct) =>
            {
                try
                {
                    if (request.Token.IsCancellationRequested)
                    {
                        return ValueTask.CompletedTask;
                    }

                    RenderTile(in request);
                }
                catch (OperationCanceledException) { }
                catch (Exception e) { Debug.WriteExceptionToFile(e); }
                finally
                {
                    RetireInFlight(request.Key, request.RequestId);
                    request.Picture.Dispose();
                }

                return ValueTask.CompletedTask;
            });
        }
        catch (OperationCanceledException)
        {
            // Service is shutting down
        }
    }

    private void RenderTile(in TileRequest request)
    {
        Debug.ThrowOnUiThread();

        // Skip if already in cache (another request may have rendered it)
        if (Cache.Contains(request.Key))
        {
            return;
        }

        request.Token.ThrowIfCancellationRequested();

        // Compute actual tile dimensions (edge tiles may be smaller)
        double tileScale = TileGrid.GetTileLevelScale(request.Key.TileLevel);
        int pagePixelWidth = (int)Math.Ceiling(request.PageDisplaySize.Width * tileScale);
        int pagePixelHeight = (int)Math.Ceiling(request.PageDisplaySize.Height * tileScale);

        int tileWidth = Math.Min(TileGrid.TilePixelSize, pagePixelWidth - request.Key.Column * TileGrid.TilePixelSize);
        int tileHeight = Math.Min(TileGrid.TilePixelSize, pagePixelHeight - request.Key.Row * TileGrid.TilePixelSize);

        if (tileWidth <= 0 || tileHeight <= 0)
        {
            return;
        }

        var imageInfo = new SKImageInfo(tileWidth, tileHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        var matrix = TileGrid.CreateRenderMatrix(request.Key.Column, request.Key.Row, request.PpiScale, request.Key.TileLevel);

        if (matrix.MapRect(request.Picture.Item.CullRect).IntersectsWith(imageInfo.Rect))
        {
            var surface = SKSurface.Create(imageInfo);
            if (surface is null)
            {
                return;
            }

            try
            {
                var canvas = surface.Canvas;

                request.Token.ThrowIfCancellationRequested();

                canvas.SetMatrix(in matrix);
                canvas.Clear(SKColors.White);

                var paint = RentPaint();
                try
                {
                    canvas.DrawPicture(request.Picture.Item, paint);
                }
                finally
                {
                    ReturnPaint(paint);
                }

                request.Token.ThrowIfCancellationRequested();

                SKPixmap pixmap = new SKPixmap();
                TileImage? image = null;
                bool shouldRender = true;

                try
                {
                    var hasPixmap = surface.PeekPixels(pixmap);
#if DEBUG
                    if (!hasPixmap)
                    {
                        // Probably because the surface is rendered on the GPU instead of the CPU.
                        // Copying the pixels to a SKBitmap should be the fallback solution.
                        System.Diagnostics.Debug.WriteLine("Cannot get pixmap from surface.");
                    }
#endif

                    if (hasPixmap && pixmap.GetPixelSpan().IndexOfAnyExcept(byte.MaxValue) == -1)
                    {
                        // It's empty (all pixels are white). Snapshotting it would cache a tile of
                        // white pixels; recording the key alone is enough to stop it being
                        // re-rendered and tells the render pass there is nothing to draw.
                        shouldRender = false;
                    }
                    else
                    {
                        image = new TileImage(surface.Snapshot());
                    }
                }
                finally
                {
                    pixmap.Dispose();
                }

                if (image is not null)
                {
                    Cache.Add(request.Key, image);
                }
                else
                {
                    Cache.AddBlank(request.Key);
                }

                if (shouldRender)
                {
                    TileReady?.Invoke(request.Key);
                }
            }
            finally
            {
                surface.Dispose();
            }
        }
        else
        {
            request.Token.ThrowIfCancellationRequested();

            // There is nothing to render. The SKPicture's CullRect is a narrow rect of the area
            // that contains elements to render (SKPicture is recorded with the RTree optimisation).
            Cache.AddBlank(request.Key);
        }
    }

    private SKPaint RentPaint()
    {
        return _paintPool.TryTake(out var paint)
            ? paint
            : new SKPaint { IsAntialias = false, IsDither = true };
    }

    private void ReturnPaint(SKPaint paint)
    {
        _paintPool.Add(paint);
    }

    /// <summary>
    /// Returns the per-page cancellation token, creating one if needed. The retry loop
    /// handles a race against <see cref="CancelPage"/>: between GetOrAdd returning a
    /// stale entry and the Token read, CancelPage may have disposed it.
    /// </summary>
    private CancellationToken GetPageCancellationToken(int pageNumber)
    {
        while (true)
        {
            var cts = _pageTokens.GetOrAdd(pageNumber, static (_, root) =>
                CancellationTokenSource.CreateLinkedTokenSource(root), _mainToken);
            try
            {
                return cts.Token;
            }
            catch (ObjectDisposedException)
            {
                _pageTokens.TryRemove(KeyValuePair.Create(pageNumber, cts));
            }
        }
    }

    /// <summary>
    /// Requests tiles for a page. Missing tiles are queued for background rendering, prioritised so
    /// that tiles near the centre of <paramref name="visibleArea"/> render first and each ring
    /// around the centre fills clockwise from the top.
    /// The caller provides a cloned picture reference per tile.
    /// </summary>
    /// <param name="pageNumber">The page number.</param>
    /// <param name="picture">A cloned reference to the page's SKPicture. The caller retains ownership of this reference;
    /// the service will clone it internally for each tile request.</param>
    /// <param name="tileLevel">The tile level to render at.</param>
    /// <param name="tiles">Span of (column, row) tile coordinates to render.</param>
    /// <param name="ppiScale">The PPI scale factor.</param>
    /// <param name="pageDisplaySize">The page display size (in display coordinates).</param>
    /// <param name="visibleArea">Visible area in page display coordinates. Used to compute the
    /// centre from which the render priority radiates outward.</param>
    public void RequestTiles(int pageNumber, IRef<SKPicture> picture, int tileLevel, ReadOnlySpan<TileCoord> tiles,
        double ppiScale, in Size pageDisplaySize, in Rect visibleArea)
    {
        if (_mainToken.IsCancellationRequested)
        {
            return;
        }

        // Get or create per-page cancellation token
        var pageToken = GetPageCancellationToken(pageNumber);

        // Clone the caller's picture once up front. Holding this local ref guarantees
        // the underlying SKPicture stays alive for the duration of this method, so the
        // per-tile clones below cannot race with disposal and fail partway through the
        // batch (which previously caused remaining tiles to be silently skipped and
        // never rendered).
        IRef<SKPicture> batchPicture;
        try
        {
            if (!picture.IsAlive)
            {
                return;
            }

            batchPicture = picture.Clone();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        // Centre of the visible area, expressed in tile-coordinates at the current level. Each
        // tile is then scored relative to this centre; the priority queue uses that score so the
        // worker threads pull the most centrally-located tiles first.
        double tileDisplaySize = TileGrid.TilePixelSize / TileGrid.GetTileLevelScale(tileLevel);
        double centreColTile = (visibleArea.Left + visibleArea.Right) * 0.5 / tileDisplaySize;
        double centreRowTile = (visibleArea.Top + visibleArea.Bottom) * 0.5 / tileDisplaySize;

        try
        {
            foreach (ref readonly var tile in tiles)
            {
                var key = new TileKey(pageNumber, tileLevel, tile.Column, tile.Row);

                // _inFlight is sufficient as the dedup guard here: the caller already filtered
                // cached tiles via TileCache.FindMissing, and the render worker re-checks the
                // cache before allocating a surface. Re-acquiring the cache lock per tile just
                // to repeat the Contains check serialized batches with concurrent Cache.Add
                // operations for no benefit.
                long requestId = Interlocked.Increment(ref _nextRequestId);
                if (!_inFlight.TryAdd(key, requestId))
                {
                    continue;
                }

                // Use the tile centre (+0.5) rather than the top-left corner so tiles symmetric
                // around the focal point get identical DistSq values and fall into the same ring.
                double dCol = tile.Column + 0.5 - centreColTile;
                double dRow = tile.Row + 0.5 - centreRowTile;
                double distSq = dCol * dCol + dRow * dRow;

                // Clockwise angle from 12 o'clock in screen coordinates (row increases downward).
                // Atan2(dCol, -dRow) yields 0 at the top, π/2 at the right, π at the bottom, and
                // -π/2 at the left; the normalisation pulls the left half into [π, 2π) so a simple
                // ascending sort walks the ring clockwise from the top.
                double angleCw = Math.Atan2(dCol, -dRow);
                if (angleCw < 0)
                {
                    angleCw += 2 * Math.PI;
                }

                // Safe: batchPicture is held alive for the entire loop, so Clone cannot
                // fail with ObjectDisposedException here.
                var pictureClone = batchPicture.Clone();

                var request = new TileRequest(in key, pictureClone, ppiScale, in pageDisplaySize, pageToken,
                    requestId, distSq, angleCw);
                if (!_requestWriter.TryWrite(request))
                {
                    pictureClone.Dispose();
                    RetireInFlight(key, requestId);
                }
            }
        }
        finally
        {
            batchPicture.Dispose();
        }
    }

    /// <summary>
    /// Retires an <see cref="_inFlight"/> entry, but only while it is still the one
    /// <paramref name="requestId"/> put there. A request whose key has since been re-taken by a
    /// newer one must not retire that newer entry, or the tile it promises could be queued twice.
    /// </summary>
    private void RetireInFlight(in TileKey key, long requestId)
    {
        _inFlight.TryRemove(KeyValuePair.Create(key, requestId));
    }

    /// <summary>
    /// Retires every <see cref="_inFlight"/> entry for a page whose queued requests have just
    /// been cancelled. They are certain to be dropped as the workers reach them, so the keys
    /// have to be free for whatever asks for those tiles next.
    /// </summary>
    private void RetirePageInFlight(int pageNumber)
    {
        foreach (var pair in _inFlight)
        {
            if (pair.Key.PageNumber == pageNumber)
            {
                _inFlight.TryRemove(pair);
            }
        }
    }

    /// <summary>
    /// Cancels pending tile requests for a page and removes its tiles from the cache.
    /// </summary>
    public void InvalidatePage(int pageNumber)
    {
        if (_pageTokens.TryRemove(pageNumber, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }

        RetirePageInFlight(pageNumber);

        Cache.InvalidatePage(pageNumber);
    }

    /// <summary>
    /// Evicts cached tiles for a page whose tile level differs from <paramref name="keepLevel"/>.
    /// Call this when the zoom level changes to free memory occupied by stale tile levels.
    /// </summary>
    public void EvictStaleLevels(int pageNumber, int keepLevel)
    {
        Cache.EvictPageLevelsExcept(pageNumber, keepLevel);
    }

    /// <summary>
    /// Cancels pending tile requests for a page without removing cached tiles.
    /// Call this when a page scrolls out of the visible area.
    /// </summary>
    public void CancelPage(int pageNumber)
    {
        if (!_pageTokens.TryRemove(pageNumber, out var cts))
        {
            return;
        }

        cts.Cancel();
        cts.Dispose();

        RetirePageInFlight(pageNumber);
    }

    public async ValueTask DisposeAsync()
    {
        await _mainCts.CancelAsync();

        // No new requests can be queued once the writer is completed.
        _requestWriter.TryComplete();

        try
        {
            await _processingLoopTask;
        }
        catch
        {
            // No op
        }

        // Release the picture clones of requests still queued when the processing
        // loop stopped.
        while (_requestReader.TryRead(out var request))
        {
            request.Picture.Dispose();
        }

        foreach (var kvp in _pageTokens)
        {
            kvp.Value.Dispose();
        }

        _pageTokens.Clear();
        _inFlight.Clear();

        while (_paintPool.TryTake(out var paint))
        {
            paint.Dispose();
        }

        Cache.Dispose();
        _mainCts.Dispose();
    }
}
