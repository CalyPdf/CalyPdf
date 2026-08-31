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
using Avalonia.Media;
using Avalonia.Skia;
using Caly.Core.Services.Rendering;
using Caly.Core.Utilities;
using SkiaSharp;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Caly.Core.Controls.Rendering;

public partial class TiledPdfPageControl
{
    /// <summary>
    /// Sampling used while the user is actively scrolling or zooming. Nearest-neighbour
    /// skips the per-pixel filter math and makes fast motion cheaper; the visible loss
    /// in quality isn't noticeable while the scene is moving.
    /// </summary>
    private static readonly SKSamplingOptions InteractiveSamplingOptions = new(SKFilterMode.Nearest, SKMipmapMode.None);
    
    /// <summary>
    /// Sampling used when the scene is idle. Bilinear filtering for smooth tile scaling
    /// when the zoom ratio is not exactly 1:1 with the tile level resolution.
    /// </summary>
    private static readonly SKSamplingOptions IdleSamplingOptions = new(SKFilterMode.Linear, SKMipmapMode.Nearest);

    /// <summary>
    /// Reusable buffer for building tile draw entries in <see cref="Render"/>.
    /// Entries are transferred to an <see cref="ArrayPool{T}"/>-backed array for the draw operation.
    /// Only accessed on the UI thread.
    /// </summary>
    private readonly List<TileDrawEntry> _renderTileEntries = new();

    /// <summary>
    /// The tile level used on the previous render pass, used to detect zoom-level changes
    /// and trigger deferred eviction of stale tile levels from the cache.
    /// Only accessed on the UI thread in <see cref="Render"/>.
    /// Uses <see cref="int.MinValue"/> as the unset sentinel because tile levels can be
    /// negative (zoom-out), so any value a real level could take is ambiguous.
    /// </summary>
    private int _lastTileLevel = int.MinValue;

    /// <summary>
    /// Tile range produced by the last content render. Compared against the current
    /// tile range on <see cref="VisibleAreaProperty"/> changes so we only invalidate
    /// when the set of drawn tiles would actually differ — <see cref="VisibleAreaProperty"/>
    /// is deliberately left out of <see cref="AffectsRender"/> because sub-tile-boundary
    /// scrolls change the area without changing which tiles are drawn.
    /// <see cref="_lastRenderedTileRangeValid"/> is false when the last render took a
    /// base/empty path (no visible area, no picture, etc.), so transitions back into
    /// content rendering still invalidate.
    /// Only accessed on the UI thread.
    /// </summary>
    private TileRange _lastRenderedTileRange;

    private bool _lastRenderedTileRangeValid;

    /// <summary>
    /// When true, stale tile levels should be evicted once all visible tiles at the
    /// current level are cached. This defers eviction so that old-level tiles remain
    /// available as fallbacks while new-level tiles are being rendered.
    /// Only accessed on the UI thread in <see cref="Render"/>.
    /// </summary>
    private bool _staleLevelEvictionPending;

    private SKMatrix _ppiScaleMatrix = SKMatrix.Identity;

    /// <summary>
    /// This operation is executed on the UI thread.
    /// Draws cached tiles for visible area + margin. Tile requesting is handled
    /// separately in <see cref="RequestVisibleTiles"/>.
    /// </summary>
    public override void Render(DrawingContext context)
    {
        Debug.ThrowNotOnUiThread();

        var viewPort = new Rect(Bounds.Size);
        var visibleArea = VisibleArea;
        var picture = Picture;
        var service = TileRenderService;
        var pageDisplaySize = PageDisplaySize;

        if (viewPort.IsEmpty()
            || !visibleArea.HasValue || visibleArea.Value.IsEmpty()
            || picture?.IsAlive != true
            || service is null
            || pageDisplaySize.Width <= 0 || pageDisplaySize.Height <= 0)
        {
            _lastRenderedTileRangeValid = false;
            base.Render(context);
            return;
        }

        var cullRect = _ppiScaleMatrix.MapRect(picture.Item.CullRect);
        int tileLevel = TileGrid.ComputeTileLevel(ZoomLevel);
        int pageNumber = PageNumber;

        // Detect tile level changes and mark stale levels for deferred eviction.
        // We do NOT evict immediately — old-level tiles serve as fallbacks while
        // new-level tiles are being rendered in the background.
        if (_lastTileLevel != int.MinValue && _lastTileLevel != tileLevel)
        {
            _staleLevelEvictionPending = true;
        }

        _lastTileLevel = tileLevel;

        // Draw visible tiles + margin. The margin ensures tiles just outside the
        // viewport are pre-drawn, so the compositor can handle short scrolls without
        // triggering a new render pass. At high zoom levels this keeps per-frame work
        // bounded by viewport size rather than growing with the full tile grid.
        if (!GetTileRange(visibleArea.Value, in pageDisplaySize, tileLevel, RenderTileMargin,
                out int startCol, out int startRow, out int endCol, out int endRow))
        {
            _lastRenderedTileRangeValid = false;
            base.Render(context);
            return;
        }

        // Record the rendered range so subsequent VisibleArea changes can skip
        // invalidation when the tile range is unchanged.
        _lastRenderedTileRange = new TileRange(tileLevel, startCol, startRow, endCol, endRow);
        _lastRenderedTileRangeValid = true;

        bool allVisibleTilesCached = ComposeTileDrawEntries(service.Cache, pageNumber, tileLevel,
            startCol, startRow, endCol, endRow, in pageDisplaySize, _renderTileEntries);

        // Evict stale tile levels only after all visible+margin tiles at the current level
        // are cached, so old tiles remain available as fallbacks during the transition.
        // Eviction runs on a background thread to avoid lock acquisition and bitmap
        // disposal during the render pass.
        if (allVisibleTilesCached && _staleLevelEvictionPending)
        {
            _staleLevelEvictionPending = false;
            _ = Task.Run(() =>
            {
                try
                {
                    service.EvictStaleLevels(pageNumber, tileLevel);
                }
                catch (Exception e)
                {
                    System.Diagnostics.Debug.WriteLine(e);
                }
            });
        }

        // Transfer entries to an ArrayPool-backed array for the draw operation.
        // The draw operation takes ownership and returns the array to the pool on Dispose.
        int entryCount = _renderTileEntries.Count;
        var tileBuffer = ArrayPool<TileDrawEntry>.Shared.Rent(Math.Max(entryCount, 1));
        CollectionsMarshal.AsSpan(_renderTileEntries).CopyTo(tileBuffer);
        _renderTileEntries.Clear();

        SKSamplingOptions samplingOptions = _isInteracting
            ? InteractiveSamplingOptions
            : IdleSamplingOptions;
        context.Custom(new TiledDrawOperation(viewPort, cullRect, tileBuffer, entryCount, samplingOptions));
    }

    /// <summary>
    /// Builds the list of tiles to draw for the given tile range, in draw order.
    /// <para>
    /// Each tile in the range resolves to one of three outcomes: an exact-level cached image is
    /// drawn whole; a tile known to be blank contributes nothing; and a missing tile falls back to
    /// a coarser tile if one covers it, or otherwise to whatever finer-level tiles are cached.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Split out of <see cref="Render"/> so the composition can be exercised directly against a
    /// <see cref="TileCache"/>, without a drawing context.
    /// </remarks>
    /// <param name="entries">Cleared, then filled with the entries to draw. Each entry owns an
    /// image reference and must be disposed by the caller.</param>
    /// <returns>
    /// <see langword="true"/> when every tile in the range was resolved at the exact level (drawn
    /// or known blank), meaning no fallback was needed and stale levels are safe to evict.
    /// </returns>
    internal static bool ComposeTileDrawEntries(TileCache cache, int pageNumber, int tileLevel,
        int startCol, int startRow, int endCol, int endRow, in Size pageDisplaySize,
        List<TileDrawEntry> entries)
    {
        int rangeCols = endCol - startCol + 1;
        int rangeRows = endRow - startRow + 1;
        int tileCount = rangeCols * rangeRows;

        System.Diagnostics.Debug.Assert(tileCount >= 0);

        entries.Clear();
        entries.EnsureCapacity(tileCount);

        bool allVisibleTilesCached = true;

        // Batch the exact-level lookups under a single cache lock acquisition instead of N
        // locked TryGet calls. With a background renderer concurrently adding tiles and a
        // separate prefetch thread calling FindMissing, per-tile locking here was the main
        // source of frame-time jitter during fast scrolling.
        var exactLevelResults = ArrayPool<TileCacheResult>.Shared.Rent(tileCount);
        try
        {
            cache.TryGetRange(pageNumber, tileLevel,
                startCol, startRow, endCol, endRow,
                exactLevelResults.AsSpan(0, tileCount));

            // Query cached higher levels once per render. Passed into the finer-level fallback
            // search so it iterates only levels that actually have tiles — this avoids scanning
            // large empty tile grids at finer levels (4x per level) while still finding fallbacks
            // when the user zooms out past many levels (otherwise tiles from deeply zoomed-in
            // views would be skipped, leaving the page blank until exact-level tiles render).
            int[]? higherCachedLevels = null;
            bool higherCachedLevelsFetched = false;

            for (int r = 0; r < rangeRows; r++)
            {
                int row = startRow + r;
                int flatRowIndex = r * rangeCols;

                for (int c = 0; c < rangeCols; c++)
                {
                    int col = startCol + c;
                    var result = exactLevelResults[flatRowIndex + c];

                    if (result.Image is { } imageRef)
                    {
                        // Exact-level tile available — use full image as source.
                        var destRect = TileGrid.GetTileDisplayRect(col, row, tileLevel, in pageDisplaySize).ToSKRect();
                        var srcRect = new SKRect(0, 0, imageRef.Item.Width, imageRef.Item.Height);
                        entries.Add(new TileDrawEntry(imageRef, srcRect, destRect));
                    }
                    else if (result.State == TileCacheState.Blank)
                    {
                        // Rendered and empty: nothing to draw, and no fallback can add pixels to
                        // an area already known to be blank. It counts as cached, so it must not
                        // hold back stale-level eviction either.
                    }
                    else
                    {
                        allVisibleTilesCached = false;

                        // Try coarser (lower-level) fallback first — single upscaled tile.
                        var fallbackEntry = TryGetFallbackTile(cache, pageNumber, tileLevel, col, row, in pageDisplaySize, out bool coveredByBlankTile);
                        if (fallbackEntry.HasValue)
                        {
                            entries.Add(fallbackEntry.Value);
                        }
                        else if (!coveredByBlankTile)
                        {
                            // Only ask for the finer-level set when we actually need it. In the
                            // common case where every exact-level tile hits or a coarser fallback
                            // is available, we skip this locked snapshot entirely.
                            if (!higherCachedLevelsFetched)
                            {
                                higherCachedLevels = cache.GetCachedLevelsAbove(pageNumber, tileLevel);
                                higherCachedLevelsFetched = true;
                            }

                            // Try finer (higher-level) fallback — multiple cached tiles may cover this area.
                            // This handles zoom-out: old higher-resolution tiles fill the gap until
                            // coarser tiles are rendered.
                            AddHigherLevelFallbackTiles(cache, pageNumber, tileLevel, col, row, in pageDisplaySize, entries, higherCachedLevels);
                        }
                    }
                }
            }
        }
        finally
        {
            // Clear references before returning to the pool — the entries we handed to
            // the list hold the clones we still need; any untransferred slots are null.
            Array.Clear(exactLevelResults, 0, tileCount);
            ArrayPool<TileCacheResult>.Shared.Return(exactLevelResults, clearArray: false);
        }

        return allVisibleTilesCached;
    }

    /// <summary>
    /// Searches cached lower tile levels for a coarser tile that covers the area of a missing tile.
    /// Returns a <see cref="TileDrawEntry"/> with the appropriate sub-region of the fallback image,
    /// or null if no fallback is available or the covering tile is blank.
    /// </summary>
    /// <param name="cache">The tile cache to search.</param>
    /// <param name="pageNumber">The page number.</param>
    /// <param name="tileLevel">The tile level of the missing tile.</param>
    /// <param name="col">Column of the missing tile.</param>
    /// <param name="row">Row of the missing tile.</param>
    /// <param name="pageDisplaySize">The page display size.</param>
    /// <param name="isBlank">
    /// Set to <c>true</c> when a cached tile covering this area was found but is blank. The
    /// caller must then skip the finer-level fallback search: there is nothing to draw here.
    /// </param>
    /// <returns>A fallback tile entry with upscaled source rect, or null.</returns>
    internal static TileDrawEntry? TryGetFallbackTile(TileCache cache, int pageNumber, int tileLevel, int col, int row, in Size pageDisplaySize, out bool isBlank)
    {
        isBlank = false;

        // Search lower levels (coarser tiles) for a cached tile that covers this area.
        // At fallback level fl (where fl < tileLevel), the covering tile is at
        // (col >> d, row >> d) where d = tileLevel - fl. Walks down to
        // TileGrid.MinTileLevel so that zoom-out fallbacks from a cached
        // negative-level tile are still found when the current level is also negative.
        for (int fl = tileLevel - 1; fl >= TileGrid.MinTileLevel; fl--)
        {
            int levelDiff = tileLevel - fl;
            int divisor = 1 << levelDiff;
            int fallbackCol = col >> levelDiff;
            int fallbackRow = row >> levelDiff;

            var fallbackKey = new TileKey(pageNumber, fl, fallbackCol, fallbackRow);
            var fallback = cache.Lookup(fallbackKey);

            if (fallback.State == TileCacheState.Blank)
            {
                // The covering tile is rendered and blank. The missing tile's area is a sub-region
                // of it, so there is nothing to draw and no coarser or finer tile can add pixels.
                isBlank = true;
                return null;
            }

            if (fallback.Image is not { } fallbackRef)
            {
                continue;
            }

            // Compute the sub-region within the fallback image that corresponds
            // to the missing tile's display area.
            //
            // The fallback image is TilePixelSize x TilePixelSize (or smaller for edge tiles).
            // Each current-level tile maps to a (TilePixelSize / divisor) pixel-wide strip
            // within the fallback image.
            float subPixelSize = (float)TileGrid.TilePixelSize / divisor;
            int subCol = col - (fallbackCol << levelDiff);
            int subRow = row - (fallbackRow << levelDiff);

            float srcX = subCol * subPixelSize;
            float srcY = subRow * subPixelSize;

            // Clamp to actual image dimensions for edge tiles
            float srcRight = Math.Min(srcX + subPixelSize, fallbackRef.Item.Width);
            float srcBottom = Math.Min(srcY + subPixelSize, fallbackRef.Item.Height);

            if (srcRight <= srcX || srcBottom <= srcY)
            {
                fallbackRef.Dispose();
                continue;
            }

            var srcRect = new SKRect(srcX, srcY, srcRight, srcBottom);

            // The destination is the display area of the missing tile
            var displayRect = TileGrid.GetTileDisplayRect(col, row, tileLevel, in pageDisplaySize);

            return new TileDrawEntry(fallbackRef, srcRect, displayRect.ToSKRect());
        }

        return null;
    }

    /// <summary>
    /// Searches cached higher tile levels (finer resolution) for tiles that overlap the
    /// display area of a missing tile. This handles zoom-out: the previously rendered
    /// higher-resolution tiles are drawn at their original display positions, covering
    /// parts of the missing coarser tile until it is rendered.
    /// </summary>
    /// <param name="higherCachedLevels">The set of tile levels above <paramref name="tileLevel"/>
    /// that have any cached tiles for this page, sorted ascending (closest level first).
    /// Queried once per render to skip empty levels without iterating empty regions.</param>
    internal static void AddHigherLevelFallbackTiles(TileCache cache, int pageNumber, int tileLevel, int col, int row,
        in Size pageDisplaySize, List<TileDrawEntry> entries, int[]? higherCachedLevels)
    {
        if (higherCachedLevels is null)
        {
            return;
        }

        foreach (var fl in higherCachedLevels)
        {
            int levelDiff = fl - tileLevel;
            int multiplier = 1 << levelDiff;

            // At finer level fl, the area of tile (col, row) at tileLevel is covered
            // by a multiplier x multiplier block of tiles starting at (col * multiplier, row * multiplier).
            int startCol = col * multiplier;
            int startRow = row * multiplier;
            int endCol = startCol + multiplier;
            int endRow = startRow + multiplier;

            // Clamp to grid bounds at the finer level
            var dims = TileGrid.GetGridDimensions(in pageDisplaySize, fl);
            endCol = Math.Min(endCol, dims.Width);
            endRow = Math.Min(endRow, dims.Height);

            bool foundAny = false;
            for (int r = startRow; r < endRow; ++r)
            {
                for (int c = startCol; c < endCol; ++c)
                {
                    var finerKey = new TileKey(pageNumber, fl, c, r);

                    // A blank tile yields no image, so it can neither occupy a draw slot nor set
                    // foundAny - it must not stop the search at a level that contributes nothing.
                    if (cache.Lookup(finerKey).Image is { } imageRef)
                    {
                        // Each finer tile maps to its own (smaller) display (dest) rect 
                        // draw the full image at that position.
                        var destRect = TileGrid.GetTileDisplayRect(c, r, fl, in pageDisplaySize).ToSKRect();
                        var srcRect = new SKRect(0, 0, imageRef.Item.Width, imageRef.Item.Height);
                        entries.Add(new TileDrawEntry(imageRef, srcRect, destRect));
                        foundAny = true;
                    }
                }
            }

            // Use the closest higher level that has any cached tiles
            if (foundAny)
            {
                break;
            }
        }
    }
}