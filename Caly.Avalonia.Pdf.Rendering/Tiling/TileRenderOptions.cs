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

namespace Caly.Avalonia.Pdf.Rendering.Tiling;

/// <summary>Configuration for <see cref="TileRenderService"/> and the tile grid.</summary>
public sealed record TileRenderOptions
{
    /// <summary>Edge length in pixels of a single tile at its level's resolution.</summary>
    public int TilePixelSize { get; init; } = TileGrid.TilePixelSize;

    /// <summary>Maximum number of concurrent background tile renders.</summary>
    public int MaxDegreeOfParallelism { get; init; } = Math.Max(2, Environment.ProcessorCount / 2);

    /// <summary>Hard floor on the tile level (most zoomed-out coarse level).</summary>
    public int MinTileLevel { get; init; } = TileGrid.MinTileLevel;

    /// <summary>Tile cache memory budget in bytes.</summary>
    public long MaxCacheBytes { get; init; } = 256L * 1024 * 1024;
}
