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
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using Caly.Core.Services.Rendering;
using System;

namespace Caly.Core.Controls.Rendering;

/// <summary>
/// A 1 x 1 control that is always in the main view and draws nothing. When <see cref="GpuCachePurge"/> is
/// requested it schedules a frame, and on the render thread of that frame empties the GPU resource cache
/// of unused textures. It exists because after the last document closes no tile control is left to draw.
/// </summary>
public sealed class GpuCachePurgeHost : Control
{
    private static readonly Rect OnePixel = new(0, 0, 1, 1);

    public GpuCachePurgeHost()
    {
        IsHitTestVisible = false;
        Focusable = false;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        GpuCachePurge.PurgeRequested += OnPurgeRequested;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        GpuCachePurge.PurgeRequested -= OnPurgeRequested;
        base.OnDetachedFromVisualTree(e);
    }

    // May be raised by the collection timer's thread.
    private void OnPurgeRequested() => Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Background);

    public override void Render(DrawingContext context)
    {
        context.Custom(new PurgeDrawOperation());
    }

    private sealed class PurgeDrawOperation : ICustomDrawOperation
    {
        public Rect Bounds => OnePixel;

        public bool HitTest(Point p) => false;

        public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);

        public void Dispose()
        {
        }

        /// <summary>
        /// Executed on the render thread, where the <c>GRContext</c> is current. A software renderer has no
        /// <c>GRContext</c>: the request is still consumed, there is nothing to purge.
        /// </summary>
        public void Render(ImmediateDrawingContext context)
        {
            if (
#pragma warning disable CS8600
                !context.TryGetFeature(out ISkiaSharpApiLeaseFeature leaseFeature))
#pragma warning restore CS8600
            {
                return;
            }

            using ISkiaSharpApiLease lease = leaseFeature.Lease();
            var grContext = lease?.GrContext;

            if (GpuCachePurge.TryConsume())
            {
                // Unlocked resources only: textures a frame in flight is drawing are untouched, and the
                // textures of tiles still cached are uploaded again on their next draw.
                grContext?.PurgeUnlockedResources(false);
            }

            if (MemoryProbe.IsEnabled)
            {
                MemoryProbe.RecordGpuCache(grContext);
            }
        }
    }
}
