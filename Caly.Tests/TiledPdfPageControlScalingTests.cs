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

using System.Collections.Concurrent;
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
/// A screen at 150% shows 1.5 device pixels per display unit at 100% zoom. Tiles picked from the
/// zoom alone (level 0, one pixel per unit) were stretched 1.5x and came out blurred, while a pinch
/// landing a hair above 100% (level 1) was sharp.
/// </summary>
public class TiledPdfPageControlScalingTests
{
    [AvaloniaFact]
    public async Task TilesFollowTheScreenScaling()
    {
        var service = new TileRenderService(new TileCache());
        await using var _ = service;

        var levels = new ConcurrentQueue<int>();
        service.TileReady += key => levels.Enqueue(key.TileLevel);

        using var picture = CreatePicture();
        var control = new TiledPdfPageControl
        {
            TileRenderService = service,
            PageNumber = 1,
            PpiScale = 1.0,
            ZoomLevel = 1.0,
            PageDisplaySize = new Size(1000, 1000),
            VisibleArea = new Rect(0, 0, 500, 500),
            Picture = picture,
            Width = 1000,
            Height = 1000
        };

        var window = new Window { Content = control };
        window.Show();

        Assert.True(await WaitForLevel(levels, 0), "No level 0 tiles at 100% screen scaling.");

        // The window moves to a 150% screen: nothing else about the page changes.
        window.SetRenderScaling(1.5);

        Assert.True(await WaitForLevel(levels, 1),
            "At 100% zoom on a 150% screen the tiles must be level 1, or they are stretched and blurred.");
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

    /// <summary>
    /// Tiles render on background threads, so the assertion has to wait for them.
    /// </summary>
    private static async Task<bool> WaitForLevel(ConcurrentQueue<int> levels, int level)
    {
        for (int i = 0; i < 200; ++i)
        {
            Dispatcher.UIThread.RunJobs();
            if (levels.Contains(level))
            {
                return true;
            }

            await Task.Delay(10);
        }

        return false;
    }
}
