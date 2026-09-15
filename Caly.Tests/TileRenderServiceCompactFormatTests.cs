using System.Collections.Concurrent;
using Avalonia;
using Caly.Core.Models;
using Caly.Core.Services.Rendering;
using Caly.Core.Utilities;
using SkiaSharp;

namespace Caly.Tests;

/// <summary>
/// <see cref="CalySettings.UseCompactTileFormat"/>'s effect on the actual rasterised tile: pixel format,
/// resulting memory, and that the white-tile blank check still holds under the new format (a fresh
/// colour type is the kind of change that silently breaks a byte-pattern check like that one).
/// </summary>
public class TileRenderServiceCompactFormatTests
{
    private static readonly Size PageSize = new(TileGrid.TilePixelSize, TileGrid.TilePixelSize);

    private static readonly Rect VisibleArea = new(0, 0, TileGrid.TilePixelSize, TileGrid.TilePixelSize);

    private static IRef<SKPicture> RecordPicture(SKRect cullRect, Action<SKCanvas>? draw = null)
    {
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(cullRect);
        draw?.Invoke(canvas);
        return RefCountable.Create(recorder.EndRecording());
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

    private static async Task<TileImage> RenderOneTileAsync(TileRenderService service, TileCache cache)
    {
        var ready = new ConcurrentBag<TileKey>();
        service.TileReady += key => ready.Add(key);

        var fullTile = new SKRect(0, 0, TileGrid.TilePixelSize, TileGrid.TilePixelSize);
        using var picture = RecordPicture(fullTile, canvas =>
        {
            using var paint = new SKPaint { Color = SKColors.Black };
            canvas.DrawRect(fullTile, paint);
        });

        var key = new TileKey(1, 0, 0, 0);
        service.RequestTiles(1, picture, 0, [new TileCoord(0, 0)], 1.0, PageSize, VisibleArea);

        await WaitUntilAsync(() => cache.Contains(key), "the tile to be rendered");

        var result = cache.Lookup(key);
        Assert.Equal(TileCacheState.Cached, result.State);
        return result.Image!.Item;
    }

    [Fact]
    public async Task DefaultFormat_IsBgra8888_FourBytesPerPixel()
    {
        var cache = new TileCache();
        await using var service = new TileRenderService(cache, startProcessingLoop: true);

        var image = await RenderOneTileAsync(service, cache);
        try
        {
            Assert.Equal(SKColorType.Bgra8888, image.Image.ColorType);
            Assert.Equal(4L * TileGrid.TilePixelSize * TileGrid.TilePixelSize, image.BytesSize);
        }
        finally
        {
            image.Dispose();
            cache.Dispose();
        }
    }

    [Fact]
    public async Task CompactFormat_IsRgb565_HalvesBytesPerPixel()
    {
        var cache = new TileCache();
        await using var service = new TileRenderService(cache, startProcessingLoop: true, useCompactTileFormat: true);

        var image = await RenderOneTileAsync(service, cache);
        try
        {
            Assert.Equal(SKColorType.Rgb565, image.Image.ColorType);
            Assert.Equal(2L * TileGrid.TilePixelSize * TileGrid.TilePixelSize, image.BytesSize);
        }
        finally
        {
            image.Dispose();
            cache.Dispose();
        }
    }

    /// <summary>
    /// The blank check in <see cref="TileRenderService"/> scans the raw pixel bytes for an all-0xFF
    /// pattern. White happens to be the max-value bit pattern in both formats (0xFFFFFFFF in Bgra8888,
    /// 0xFFFF in Rgb565), so the check should still catch an all-white tile under the compact format -
    /// this pins that down rather than relying on it staying true by coincidence.
    /// </summary>
    [Fact]
    public async Task CompactFormat_WhiteTile_IsStillRecordedBlank()
    {
        var cache = new TileCache();
        await using var service = new TileRenderService(cache, startProcessingLoop: true, useCompactTileFormat: true);

        var fullTile = new SKRect(0, 0, TileGrid.TilePixelSize, TileGrid.TilePixelSize);
        // Cull rect covers the tile, but nothing is drawn into it, so it renders to plain white.
        using var picture = RecordPicture(fullTile);

        var key = new TileKey(1, 0, 0, 0);
        service.RequestTiles(1, picture, 0, [new TileCoord(0, 0)], 1.0, PageSize, VisibleArea);

        await WaitUntilAsync(() => cache.Contains(key) || cache.Lookup(key).State == TileCacheState.Blank,
            "the tile to be recorded");

        var result = cache.Lookup(key);
        Assert.Equal(TileCacheState.Blank, result.State);
        Assert.Null(result.Image);

        cache.Dispose();
    }
}
