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
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless.XUnit;
using Caly.Avalonia.Pdf.Rendering;
using Caly.Avalonia.Pdf.Rendering.Tiling;
using SkiaSharp;

namespace Caly.Avalonia.Pdf.Rendering.Tests;

public class TileRenderServiceTests
{
    private static IRef<SKPicture> MakePicture(int w = 600, int h = 800)
    {
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(SKRect.Create(w, h));
        using var paint = new SKPaint { Color = SKColors.Black };
        canvas.DrawRect(SKRect.Create(10, 10, 100, 100), paint);
        return PdfRef.Create(recorder.EndRecording());
    }

    [AvaloniaFact]
    public async Task RequestTiles_renders_into_cache_and_raises_TileReady()
    {
        await using var service = new TileRenderService(new TileRenderOptions());
        using var picture = MakePicture();

        var ready = new TaskCompletionSource();
        service.TileReady += _ => ready.TrySetResult();

        var pageDisplaySize = new Size(600, 800);
        var visible = new Rect(0, 0, 600, 800);
        var tiles = new[] { new TileCoord(0, 0) };

        service.RequestTiles(1, picture, tileLevel: 0, tiles, ppiScale: 1.0,
            in pageDisplaySize, in visible);

        var completed = await Task.WhenAny(ready.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(ready.Task, completed);
        Assert.True(service.Cache.Contains(new TileKey(1, 0, 0, 0)));
    }
}
