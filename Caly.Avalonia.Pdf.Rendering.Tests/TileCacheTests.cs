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

using System.Collections.Generic;
using Caly.Avalonia.Pdf.Rendering.Tiling;
using SkiaSharp;

namespace Caly.Avalonia.Pdf.Rendering.Tests;

public class TileCacheTests
{
    private static SKImage MakeImage(int w = 8, int h = 8)
    {
        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.Red);
        return surface.Snapshot();
    }

    [Fact]
    public void Add_then_TryGet_returns_clone_and_Contains_true()
    {
        using var cache = new TileCache();
        var key = new TileKey(1, 0, 0, 0);
        cache.Add(in key, MakeImage());

        Assert.True(cache.Contains(in key));
        Assert.True(cache.TryGet(in key, out var img));
        using (img) { Assert.NotNull(img); Assert.True(img!.IsAlive); }
    }

    [Fact]
    public void FindMissing_reports_uncached_coordinates()
    {
        using var cache = new TileCache();
        var present = new TileKey(2, 0, 0, 0);
        cache.Add(in present, MakeImage());

        var missing = new List<TileCoord>();
        cache.FindMissing(2, 0, 0, 0, 1, 0, missing);

        Assert.Contains(new TileCoord(1, 0), missing);
        Assert.DoesNotContain(new TileCoord(0, 0), missing);
    }

    [Fact]
    public void InvalidatePage_removes_all_tiles_for_page()
    {
        using var cache = new TileCache();
        var key = new TileKey(3, 0, 0, 0);
        cache.Add(in key, MakeImage());
        cache.InvalidatePage(3);
        Assert.False(cache.Contains(in key));
    }
}
