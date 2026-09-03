using Avalonia;
using Caly.Core.Services.Rendering;
using Caly.Core.Utilities;
using SkiaSharp;

namespace Caly.Tests;

public class TileRenderServiceTests
{
    [Fact]
    public async Task DisposeAsync_ReleasesQueuedTileRequests()
    {
        using var recorder = new SKPictureRecorder();
        recorder.BeginRecording(new SKRect(0, 0, 100, 100));
        var picture = recorder.EndRecording();

        var pictureRef = RefCountable.Create(picture);

        // Processing loop not started: the queued requests are never consumed,
        // exactly like requests still sitting in the channel when disposal wins
        // the race against the render workers.
        var service = new TileRenderService(new TileCache(), startProcessingLoop: false);

        service.RequestTiles(1, pictureRef, 0, [new TileCoord(0, 0), new TileCoord(1, 0)],
            1.0, new Size(1000, 1000), new Rect(0, 0, 500, 500));

        // Sanity: the per-tile clones are queued and hold references.
        Assert.True(pictureRef.RefCount > 1);

        await service.DisposeAsync();

        // Disposal must release the queued clones; only our own reference remains,
        // otherwise the native SKPictures stay alive until finalization.
        Assert.Equal(1, pictureRef.RefCount);

        pictureRef.Dispose();
    }
    private static IRef<SKPicture> CreatePicture()
    {
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(new SKRect(0, 0, 1000, 1000));
        using (var paint = new SKPaint { Color = SKColors.Red })
        {
            canvas.DrawRect(new SKRect(0, 0, 1000, 1000), paint);
        }

        return RefCountable.Create(recorder.EndRecording());
    }

    private static readonly TileCoord[] Tiles = [new TileCoord(0, 0), new TileCoord(1, 0)];

    private static void RequestTiles(TileRenderService service, IRef<SKPicture> picture) =>
        service.RequestTiles(1, picture, 0, Tiles, 1.0, new Size(1000, 1000), new Rect(0, 0, 500, 500));

    /// <summary>
    /// The in-flight set exists to stop the same tile being rendered twice, so it stands for a
    /// promise that a render is on its way. Cancelling the page breaks that promise - every
    /// queued request for it is dropped when a worker picks it up - so the entries have to go
    /// with it, or the next request for those exact tiles is deduplicated against renders that
    /// will never happen and the page stays blank until a scroll asks for different tiles.
    /// </summary>
    [Fact]
    public async Task CancelPage_LetsTheSameTilesBeRequestedAgain()
    {
        // Processing loop off: the first batch stays in the channel, which is the window the
        // cancellation lands in.
        var service = new TileRenderService(new TileCache(), startProcessingLoop: false);
        await using var _ = service;

        using var picture = CreatePicture();

        RequestTiles(service, picture);
        int queued = picture.RefCount;
        Assert.True(queued > 1, "the first batch should be queued");

        service.CancelPage(1);

        RequestTiles(service, picture);

        Assert.True(picture.RefCount > queued,
            "the tiles cancelled with the page must be requested again, not deduplicated away");
    }

    /// <summary>
    /// Same contract for <see cref="TileRenderService.InvalidatePage"/>, which additionally drops
    /// the page's cached tiles - so after it there is nothing to draw and nothing on its way.
    /// </summary>
    [Fact]
    public async Task InvalidatePage_LetsTheSameTilesBeRequestedAgain()
    {
        var service = new TileRenderService(new TileCache(), startProcessingLoop: false);
        await using var _ = service;

        using var picture = CreatePicture();

        RequestTiles(service, picture);
        int queued = picture.RefCount;
        Assert.True(queued > 1, "the first batch should be queued");

        service.InvalidatePage(1);

        RequestTiles(service, picture);

        Assert.True(picture.RefCount > queued,
            "the tiles invalidated with the page must be requested again, not deduplicated away");
    }

    /// <summary>
    /// Cancelling one page must not free another page's in-flight entries: those requests are
    /// still live, and dropping their entries would let the same tiles be queued twice.
    /// </summary>
    [Fact]
    public async Task CancelPage_LeavesOtherPagesInFlightEntriesAlone()
    {
        var service = new TileRenderService(new TileCache(), startProcessingLoop: false);
        await using var _ = service;

        using var picture = CreatePicture();

        service.RequestTiles(2, picture, 0, Tiles, 1.0, new Size(1000, 1000), new Rect(0, 0, 500, 500));
        int queued = picture.RefCount;

        service.CancelPage(1);

        service.RequestTiles(2, picture, 0, Tiles, 1.0, new Size(1000, 1000), new Rect(0, 0, 500, 500));

        Assert.Equal(queued, picture.RefCount);
    }
}
