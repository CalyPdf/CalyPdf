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
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Caly.Core.Controls.Rendering;
using Caly.Core.Services.Rendering;
using Caly.Core.Utilities;
using SkiaSharp;

namespace Caly.Tests;

/// <summary>
/// A page control only asks for tiles from property changes, and it ignores those changes while
/// it is out of the visual tree. Closing a tab detaches and re-attaches the realised containers,
/// so the page's picture and visible area can both land in that window - after which nothing is
/// left to trigger a tile request and the page stays blank until the next zoom.
/// </summary>
public class TiledPdfPageControlAttachTests
{
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

    private static TiledPdfPageControl CreateControl(TileRenderService service)
    {
        return new TiledPdfPageControl
        {
            TileRenderService = service,
            PageNumber = 1,
            PpiScale = 1.0,
            ZoomLevel = 1.0,
            PageDisplaySize = new Size(1000, 1000),
            Width = 1000,
            Height = 1000
        };
    }

    /// <summary>
    /// Tile requests are queued from a thread pool work item, so the assertion has to wait for it.
    /// </summary>
    private static async Task<bool> WaitForTileRequest(IRef<SKPicture> picture)
    {
        for (int i = 0; i < 200; ++i)
        {
            Dispatcher.UIThread.RunJobs();
            if (picture.RefCount > 1)
            {
                return true;
            }

            await Task.Delay(10);
        }

        return false;
    }

    [AvaloniaFact]
    public async Task ReattachedControlRequestsTilesForAPictureThatArrivedWhileDetached()
    {
        // The processing loop is off, so requested tiles stay queued and keep their picture
        // clone alive - a reference count above our own is proof the request was made.
        var service = new TileRenderService(new TileCache(), startProcessingLoop: false);
        await using var _ = service;

        using var picture = CreatePicture();

        var parent = new Panel();
        var window = new Window { Content = parent };
        window.Show();

        var control = CreateControl(service);
        parent.Children.Add(control);
        Dispatcher.UIThread.RunJobs();

        // The container is recycled on a tab switch...
        parent.Children.Remove(control);
        Dispatcher.UIThread.RunJobs();

        // ...the new page's picture and visible area arrive while it is out of the tree...
        control.VisibleArea = new Rect(0, 0, 500, 500);
        control.Picture = picture;

        // ...and it comes back with both already set.
        parent.Children.Add(control);
        Dispatcher.UIThread.RunJobs();

        Assert.True(await WaitForTileRequest(picture),
            "Re-attached page control never requested its tiles, so the page renders blank.");
    }

    [AvaloniaFact]
    public async Task ReattachedControlRequestsTilesAgainAfterDetachCancelledThem()
    {
        var service = new TileRenderService(new TileCache(), startProcessingLoop: false);
        await using var _ = service;

        using var picture = CreatePicture();

        var parent = new Panel();
        var window = new Window { Content = parent };
        window.Show();

        var control = CreateControl(service);
        control.VisibleArea = new Rect(0, 0, 500, 500);
        control.Picture = picture;
        parent.Children.Add(control);
        Dispatcher.UIThread.RunJobs();

        Assert.True(await WaitForTileRequest(picture));

        // Detaching cancels the page's in-flight tile renders. Re-attaching with unchanged
        // properties raises no property change, so only the attach itself can re-request them.
        parent.Children.Remove(control);
        Dispatcher.UIThread.RunJobs();
        service.CancelPage(control.PageNumber);
        await WaitForRefCount(picture, 1);

        parent.Children.Add(control);
        Dispatcher.UIThread.RunJobs();

        Assert.True(await WaitForTileRequest(picture),
            "Re-attached page control never re-requested the tiles cancelled on detach.");
    }

    private static async Task WaitForRefCount(IRef<SKPicture> picture, int expected)
    {
        for (int i = 0; i < 200 && picture.RefCount != expected; ++i)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }
}
