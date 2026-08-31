using System.Collections.Concurrent;
using Avalonia;
using Caly.Core.Services.Rendering;
using Caly.Core.Utilities;
using SkiaSharp;

namespace Caly.Tests;

/// <summary>
/// End-to-end coverage of the render pipeline: a recorded <see cref="SKPicture"/> goes through
/// <see cref="TileRenderService"/> and lands in the <see cref="TileCache"/> either as an image tile
/// or as a blank record. Exercises both routes to a blank result - a tile that falls outside the
/// picture's cull rect, and a tile that renders but comes out entirely white.
/// </summary>
public class TileRenderPipelineTests
{
    /// <summary>Page big enough for a 4 x 4 grid of 512px tiles at level 0.</summary>
    private static readonly Size PageSize = new(2000, 2000);

    private static readonly Rect VisibleArea = new(0, 0, 2000, 2000);

    private static IRef<SKPicture> RecordPicture(SKRect cullRect, Action<SKCanvas>? draw = null)
    {
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(cullRect);
        draw?.Invoke(canvas);
        return RefCountable.Create(recorder.EndRecording());
    }

    private static void DrawBlack(SKCanvas canvas, SKRect rect)
    {
        using var paint = new SKPaint { Color = SKColors.Black };
        canvas.DrawRect(rect, paint);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail($"Timed out waiting for {because}.");
    }

    [Fact]
    public async Task RenderedTileWithContent_IsCachedAsAnImageAndAnnounced()
    {
        var cache = new TileCache();
        await using var service = new TileRenderService(cache);

        var ready = new ConcurrentBag<TileKey>();
        service.TileReady += key => ready.Add(key);

        using var picture = RecordPicture(new SKRect(0, 0, 400, 400),
            canvas => DrawBlack(canvas, new SKRect(0, 0, 400, 400)));

        var key = new TileKey(1, 0, 0, 0);
        service.RequestTiles(1, picture, 0, [new TileCoord(0, 0)], 1.0, PageSize, VisibleArea);

        await WaitUntilAsync(() => cache.Contains(key), "the tile to be rendered");

        var result = cache.Lookup(key);
        Assert.Equal(TileCacheState.Cached, result.State);
        Assert.Equal(TileGrid.TilePixelSize, result.Image!.Item.Width);
        Assert.True(result.Image.Item.BytesSize > 0);
        result.Image.Dispose();

        // Only tiles with something to draw are announced for repaint.
        await WaitUntilAsync(() => ready.Contains(key), "the tile-ready notification");

        cache.Dispose();
    }

    [Fact]
    public async Task TileThatRendersEntirelyWhite_IsRecordedBlankWithNoImage()
    {
        var cache = new TileCache();
        await using var service = new TileRenderService(cache);

        var ready = new ConcurrentBag<TileKey>();
        service.TileReady += key => ready.Add(key);

        // The cull rect covers tile (0,0) so the tile is rendered, but nothing is drawn into it:
        // the surface stays the white it was cleared to.
        using var picture = RecordPicture(new SKRect(0, 0, 400, 400));

        var key = new TileKey(1, 0, 0, 0);
        service.RequestTiles(1, picture, 0, [new TileCoord(0, 0)], 1.0, PageSize, VisibleArea);

        await WaitUntilAsync(() => cache.Contains(key), "the tile to be rendered");

        var result = cache.Lookup(key);
        Assert.Equal(TileCacheState.Blank, result.State);
        Assert.Null(result.Image);

        // A blank tile has nothing to repaint, so it is not announced.
        Assert.DoesNotContain(key, ready);

        cache.Dispose();
    }

    [Fact]
    public async Task TileOutsideThePicturesCullRect_IsRecordedBlankWithoutRendering()
    {
        var cache = new TileCache();
        await using var service = new TileRenderService(cache);

        // Content confined to the top-left corner: tile (3,3) cannot intersect it.
        using var picture = RecordPicture(new SKRect(0, 0, 100, 100),
            canvas => DrawBlack(canvas, new SKRect(0, 0, 100, 100)));

        var key = new TileKey(1, 0, 3, 3);
        service.RequestTiles(1, picture, 0, [new TileCoord(3, 3)], 1.0, PageSize, VisibleArea);

        await WaitUntilAsync(() => cache.Contains(key), "the tile to be resolved");

        var result = cache.Lookup(key);
        Assert.Equal(TileCacheState.Blank, result.State);
        Assert.Null(result.Image);

        cache.Dispose();
    }

    [Fact]
    public async Task BlankTiles_AreNotRequestedOrRenderedAgain()
    {
        var cache = new TileCache();
        await using var service = new TileRenderService(cache);

        using var picture = RecordPicture(new SKRect(0, 0, 400, 400));

        var key = new TileKey(1, 0, 0, 0);
        service.RequestTiles(1, picture, 0, [new TileCoord(0, 0)], 1.0, PageSize, VisibleArea);
        await WaitUntilAsync(() => cache.Contains(key), "the first render to settle");

        // What the control asks for on the next frame: a blank tile is not missing work.
        var missing = new List<TileCoord>();
        cache.FindMissing(1, 0, 0, 0, 0, 0, missing);
        Assert.Empty(missing);

        // Even if it is requested again, the result stays blank rather than becoming an image.
        service.RequestTiles(1, picture, 0, [new TileCoord(0, 0)], 1.0, PageSize, VisibleArea);
        await Task.Delay(100);

        Assert.Equal(TileCacheState.Blank, cache.Lookup(key).State);

        cache.Dispose();
    }

    [Fact]
    public async Task MixedPage_SeparatesImageTilesFromBlankTilesInOneBatch()
    {
        var cache = new TileCache();
        await using var service = new TileRenderService(cache);

        // Content covers tile (0,0) only; (1,0) renders white and (3,3) is outside the cull rect.
        using var picture = RecordPicture(new SKRect(0, 0, 512, 512),
            canvas => DrawBlack(canvas, new SKRect(0, 0, 400, 400)));

        service.RequestTiles(1, picture, 0,
            [new TileCoord(0, 0), new TileCoord(1, 0), new TileCoord(3, 3)], 1.0, PageSize, VisibleArea);

        var keys = new[] { new TileKey(1, 0, 0, 0), new TileKey(1, 0, 1, 0), new TileKey(1, 0, 3, 3) };
        await WaitUntilAsync(() => keys.All(k => cache.Contains(k)), "the batch to finish");

        var results = new TileCacheResult[3];
        for (int i = 0; i < keys.Length; i++)
        {
            results[i] = cache.Lookup(keys[i]);
        }

        Assert.Equal(TileCacheState.Cached, results[0].State);
        Assert.Equal(TileCacheState.Blank, results[1].State);
        Assert.Equal(TileCacheState.Blank, results[2].State);

        // Only the tile with content holds an image; the blanks cost no reference at all.
        Assert.NotNull(results[0].Image);
        Assert.Null(results[1].Image);
        Assert.Null(results[2].Image);

        // The blank levels contribute nothing, so they must not be offered as fallback candidates.
        Assert.Equal<int[]>([0], cache.GetCachedLevelsAbove(1, baseLevel: -1));

        results[0].Image!.Dispose();
        cache.Dispose();
    }

    [Fact]
    public async Task InvalidatePage_ClearsBothImageAndBlankTilesSoThePageRendersAgain()
    {
        var cache = new TileCache();
        await using var service = new TileRenderService(cache);

        using var picture = RecordPicture(new SKRect(0, 0, 512, 512),
            canvas => DrawBlack(canvas, new SKRect(0, 0, 400, 400)));

        var imageKey = new TileKey(1, 0, 0, 0);
        var blankKey = new TileKey(1, 0, 1, 0);
        service.RequestTiles(1, picture, 0, [new TileCoord(0, 0), new TileCoord(1, 0)], 1.0, PageSize, VisibleArea);
        await WaitUntilAsync(() => cache.Contains(imageKey) && cache.Contains(blankKey), "the batch to finish");

        service.InvalidatePage(1);

        Assert.Equal(TileCacheState.Missing, cache.Lookup(imageKey).State);
        Assert.Equal(TileCacheState.Missing, cache.Lookup(blankKey).State);

        // Both tiles are work again after invalidation - the page content may have changed.
        var missing = new List<TileCoord>();
        cache.FindMissing(1, 0, 0, 0, 1, 0, missing);
        Assert.Equal([new TileCoord(0, 0), new TileCoord(1, 0)], missing);

        cache.Dispose();
    }
}
