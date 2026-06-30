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

using Avalonia;
using Caly.Avalonia.Pdf.Rendering.Tiling;

namespace Caly.Avalonia.Pdf.Rendering.Tests;

public class TileGridTests
{
    [Theory]
    [InlineData(1.0, 0)]
    [InlineData(2.0, 1)]
    [InlineData(0.5, -1)]
    [InlineData(0.0, 0)]
    public void ComputeTileLevel_matches_ceil_log2(double zoom, int expected)
    {
        Assert.Equal(expected, TileGrid.ComputeTileLevel(zoom));
    }

    [Fact]
    public void ComputeTileLevel_clamps_to_min()
    {
        Assert.Equal(-4, TileGrid.ComputeTileLevel(0.001));
        Assert.Equal(-2, TileGrid.ComputeTileLevel(0.001, minTileLevel: -2));
    }

    [Fact]
    public void GetGridDimensions_respects_custom_tile_size()
    {
        var size = new Size(1024, 1024);
        // At level 0, 1024/512 = 2x2 tiles with default size.
        Assert.Equal(new PixelSize(2, 2), TileGrid.GetGridDimensions(in size, 0));
        // With 256-px tiles, 1024/256 = 4x4.
        Assert.Equal(new PixelSize(4, 4), TileGrid.GetGridDimensions(in size, 0, tilePixelSize: 256));
    }

    [Fact]
    public void GetTileDisplayRect_clamps_edge_tiles_to_page()
    {
        var size = new Size(600, 600);
        var rect = TileGrid.GetTileDisplayRect(1, 0, 0, in size); // second column at 512px → 512..600
        Assert.Equal(512, rect.Left, 3);
        Assert.Equal(88, rect.Width, 3);
    }
}
