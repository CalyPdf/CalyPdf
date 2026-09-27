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
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Caly.Core.Controls.Rendering;
using Caly.Core.Services.Rendering;
using Caly.Core.Utilities;
using SkiaSharp;

namespace Caly.Tests;

/// <summary>
/// The tiles a page needs on screen must always end up rendered, whatever the cache budget. A
/// large screen at high zoom can need more tile memory than <see cref="TileCache"/>'s budget, and
/// evicting a visible tile used to leave a permanent hole: tiles are only requested when the
/// visible tile range changes, so nothing ever asked for the evicted tile again.
/// </summary>
public class TiledPdfPageControlBudgetTests
{
    /// <summary>Bytes of one full <see cref="TileGrid.TilePixelSize"/> Bgra8888 tile.</summary>
    private const long FullTileBytes = (long)TileGrid.TilePixelSize * TileGrid.TilePixelSize * 4;

    private static IRef<SKPicture> CreatePicture(float size)
    {
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(new SKRect(0, 0, size, size));
        using (var paint = new SKPaint { Color = SKColors.Red })
        {
            canvas.DrawRect(new SKRect(0, 0, size, size), paint);
        }

        return RefCountable.Create(recorder.EndRecording());
    }

    [AvaloniaFact]
    public async Task VisibleTilesAllRender_WhenTheyNeedMoreMemoryThanTheCacheBudget()
    {
        // A 2x2 tile page, fully visible, with a budget that holds a single tile: the on-screen
        // working set is four times the budget.
        const int pageSize = TileGrid.TilePixelSize * 2;
        var service = new TileRenderService(new TileCache(maxMemoryBytes: FullTileBytes));
        await using var _ = service;

        using var picture = CreatePicture(pageSize);

        var window = new Window { Width = pageSize, Height = pageSize };
        var control = new TiledPdfPageControl
        {
            TileRenderService = service,
            PageNumber = 1,
            PpiScale = 1.0,
            ZoomLevel = 1.0,
            PageDisplaySize = new Size(pageSize, pageSize),
            Width = pageSize,
            Height = pageSize,
            VisibleArea = new Rect(0, 0, pageSize, pageSize),
            Picture = picture
        };
        window.Content = control;
        window.Show();

        var missing = new List<TileCoord>();
        for (int i = 0; i < 500; ++i)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();

            missing.Clear();
            service.Cache.FindMissing(1, 0, 0, 0, 1, 1, missing);
            if (missing.Count == 0)
            {
                break;
            }

            await Task.Delay(10);
        }

        Assert.True(missing.Count == 0,
            $"{missing.Count} visible tile(s) never rendered: {string.Join(", ", missing)}");
    }
}
