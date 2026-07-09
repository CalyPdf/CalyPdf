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
}
