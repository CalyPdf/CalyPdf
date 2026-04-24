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
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
using System;
using System.Buffers;

namespace Caly.Core.Controls.Rendering;

public partial class TiledPdfPageControl
{
    private sealed class TiledDrawOperation : ICustomDrawOperation
    {
        // Shared across all draw operations — these are only used on the render thread
        // which is single-threaded, so no synchronization needed.
        //
        // IsAntialias is deliberately false: with AA on, tile edges at fractional screen
        // pixel positions (after the zoom transform) get partial coverage which blends
        // with the canvas background, creating visible white seams between tiles.
        private static readonly SKPaint RenderPaint = new()
        {
            IsAntialias = false,
            IsDither = true
        };

        private TileDrawEntry[]? _tiles;
        private readonly int _tileCount;
        private readonly SKSamplingOptions _samplingOptions;
        private readonly SKRect _cullRect;

        public TiledDrawOperation(Rect bounds, SKRect cullRect, TileDrawEntry[] tiles, int tileCount, SKSamplingOptions samplingOptions)
        {
            Bounds = bounds;
            _cullRect = cullRect;
            _tiles = tiles;
            _tileCount = tileCount;
            _samplingOptions = samplingOptions;
        }

        public Rect Bounds { get; }

        public bool HitTest(Point p) => Bounds.Contains(p);

        public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);

        public override bool Equals(object? obj) => obj is ICustomDrawOperation cdo && Equals(cdo);

        public override int GetHashCode() => HashCode.Combine(Bounds, _tileCount);

        /// <summary>
        /// Executed on the render thread. Blits pre-rendered tile images.
        /// </summary>
        public void Render(ImmediateDrawingContext context)
        {
            Debug.ThrowOnUiThread();

            var tiles = _tiles;
            if (tiles is null)
            {
                return;
            }

            if (
#pragma warning disable CS8600
                !context.TryGetFeature(out ISkiaSharpApiLeaseFeature leaseFeature))
#pragma warning restore CS8600
            {
                return;
            }

            using ISkiaSharpApiLease lease = leaseFeature.Lease();

            var canvas = lease?.SkCanvas;
            if (canvas is null)
            {
                return;
            }

#if DEBUG
            using var backgroundPaint = new SKPaint();
            backgroundPaint.Style = SKPaintStyle.Fill;
            backgroundPaint.Color = SKColors.Aqua;
            canvas.DrawPaint(backgroundPaint);
#endif

            canvas.Save();
            canvas.ClipRect(_cullRect);

            for (int i = 0; i < _tileCount; ++i)
            {
                ref readonly var tile = ref tiles[i];
                if (tile is { CanRender: true, ImageRef.IsAlive: true } && !canvas.QuickReject(tile.DestRect))
                {
                    canvas.DrawImage(tile.ImageRef.Item, tile.SrcRect, tile.DestRect, _samplingOptions, RenderPaint);
                }
            }

            canvas.Restore();

#if DEBUG
            using var borderPaint = new SKPaint();
            borderPaint.Style = SKPaintStyle.Stroke;
            borderPaint.Color = SKColors.Red.WithAlpha(120);
            borderPaint.StrokeWidth = 5f;

            for (int i = 0; i < _tileCount; ++i)
            {
                ref readonly var tile = ref tiles[i];
                canvas.DrawRect(tile.DestRect, borderPaint);
            }

            borderPaint.Color = SKColors.DarkMagenta.WithAlpha(120);
            canvas.DrawRect(_cullRect, borderPaint);
#endif
        }

        public void Dispose()
        {
            var tiles = _tiles;
            if (tiles is null)
            {
                return;
            }

            _tiles = null;

            for (int i = 0; i < _tileCount; ++i)
            {
                tiles[i].Dispose();
                tiles[i] = default;
            }

            ArrayPool<TileDrawEntry>.Shared.Return(tiles, clearArray: false);
        }
    }
}
