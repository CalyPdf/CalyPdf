using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Caly.Core.Controls.Rendering;
using Caly.Core.Services.Rendering;
using SkiaSharp;

namespace Caly.Tests;

/// <summary>
/// A page whose picture was evicted is drawn from its cached tiles while the picture is parsed
/// again. Headless tests draw nothing, so this watches the cache: drawing a tile makes it recent.
/// </summary>
public class TiledPdfPageControlNoPictureTests
{
    private const int TileSide = 8;
    private const long TileBytes = TileSide * TileSide * 4;

    private static TileImage CreateTile()
    {
        using var surface = SKSurface.Create(new SKImageInfo(TileSide, TileSide));
        surface.Canvas.Clear(SKColors.Red);
        return new TileImage(surface.Snapshot());
    }

    [AvaloniaFact]
    public async Task WithoutAPicture_DrawsTheCachedTilesOfItsPage()
    {
        // Room for two tiles. Page 1's tile is the oldest, so it is the one a third tile pushes
        // out, unless drawing it made it recent.
        var cache = new TileCache(maxMemoryBytes: 2 * TileBytes);
        var service = new TileRenderService(cache);
        await using var _ = service;
        cache.Add(new TileKey(1, 0, 0, 0), CreateTile());
        cache.Add(new TileKey(2, 0, 0, 0), CreateTile());

        var window = new Window { Width = 100, Height = 100 };
        window.Content = new TiledPdfPageControl
        {
            TileRenderService = service,
            PageNumber = 1,
            PpiScale = 1.0,
            ZoomLevel = 1.0,
            PageDisplaySize = new Size(100, 100),
            Width = 100,
            Height = 100,
            VisibleArea = new Rect(0, 0, 100, 100)
        };
        window.Show();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();

        cache.Add(new TileKey(3, 0, 0, 0), CreateTile());

        Assert.True(cache.Contains(new TileKey(1, 0, 0, 0)));
        Assert.False(cache.Contains(new TileKey(2, 0, 0, 0)));
    }
}
