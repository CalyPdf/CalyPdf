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
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Metadata;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Caly.Core.Utilities;
using SkiaSharp;

namespace Caly.Core.Controls.Rendering;

    /*
     * We render the SKPicture directly on the Avalonia Skia canvas, i.e. we don't rasterize it to a bitmap first.
     *
     * This is different from other PDF Viewers where the PDF page is rendered to a bitmap first and then the bitmap
     * is drawn on the canvas, taking in account the visible area and the scale.
     *
     * By doing so, we avoid complex logic to know "when" to rasterize the PDF page to a bitmap and at which scale.
     *
     * The downside is that the SKPicture rendering is done on the Render thread, which can lead to lags in the app,
     * depending on the complexity of the PDF page.
     *
     * However, since SKPicture is a vector representation, it scales well and looks sharp at any zoom level.
     *
     * -> One question is whether the SKPicture rendering can take in account the visible area to optimize
     * the rendering, i.e. we would clip the picture to the visible area. The benefit of doing so in unclear.
     * One drawback of clipping the SKPicture is that a SkiaDrawOperation object needs to be created on each render,
     * i.e. every time the visible area changes.
     * Not doing so allows to reuse the same SkiaDrawOperation object as long as the Picture property does not change.
     */

    /// <summary>
    /// Skia Pdf page control.
    /// </summary>
    public sealed class SkiaPdfPageControl : Control
    {
        private sealed class SkiaDrawOperation : ICustomDrawOperation
        {
            private readonly IRef<SKPicture>? _picture;
            private readonly SKPaint _paint;
            private readonly double _ppiScale;

            private readonly Lock _lock = new Lock();

            public SkiaDrawOperation(Rect bounds, double ppiScale, IRef<SKPicture>? picture)
            {
                _picture = picture;
                _ppiScale = ppiScale;
                Bounds = bounds;

                _paint = new SKPaint()
                {
                    IsDither = false,
                    IsAntialias = false
                };
            }

            public void Dispose()
            {
                lock (_lock)
                {
                    _picture?.Dispose();
                    _paint.Dispose();
                }
            }

            public Rect Bounds { get; }

            public bool HitTest(Point p) => Bounds.Contains(p);

            public bool Equals(ICustomDrawOperation? other)
            {
                return other is SkiaDrawOperation op &&
                       op.Bounds == Bounds &&
                       op._picture?.Item?.UniqueId == _picture?.Item?.UniqueId;
            }

            public override bool Equals(object? obj)
            {
                return obj is ICustomDrawOperation cdo && Equals(cdo);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(Bounds, _picture?.Item?.UniqueId);
            }

            /// <summary>
            /// This operation is executed on Render thread.
            /// </summary>
            public void Render(ImmediateDrawingContext context)
            {
                Debug.ThrowOnUiThread();

                lock (_lock)
                {
                    if (_picture?.IsAlive != true || _picture.Item.Handle == IntPtr.Zero ||
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
                        !context.TryGetFeature(out ISkiaSharpApiLeaseFeature leaseFeature))
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.
                    {
                        return;
                    }

                    using (ISkiaSharpApiLease lease = leaseFeature.Lease())
                    {
                        var canvas = lease?.SkCanvas;
                        if (canvas is null)
                        {
                            return;
                        }

                        canvas.Save();
                        // The canvas could be clipped here: canvas.ClipRect(_visibleArea);

                        var scale = SKMatrix.CreateScale((float)_ppiScale, (float)_ppiScale);
                        canvas.DrawPicture(_picture.Item, in scale, _paint);

#if DEBUG
                        using (var skFont = SKTypeface.Default.ToFont((float)Bounds.Height / 4f, 1f))
                        using (var paint = new SKPaint())
                        {
                            paint.Style = SKPaintStyle.Fill;
                            paint.Color = SKColors.Blue.WithAlpha(100);
                            canvas.DrawText(_picture.Item.UniqueId.ToString(),
                                (float)Bounds.Width / 4f,
                                (float)Bounds.Height / 2f,
                                skFont, paint);
                        }
#endif
                        canvas.Restore();
                    }
                }
            }
        }

        /// <summary>
        /// Defines the <see cref="PpiScale"/> property.
        /// </summary>
        public static readonly StyledProperty<double> PpiScaleProperty =
            AvaloniaProperty.Register<SkiaPdfPageControl, double>(nameof(PpiScale));

        /// <summary>
        /// Defines the <see cref="Picture"/> property.
        /// </summary>
        public static readonly StyledProperty<IRef<SKPicture>?> PictureProperty =
            AvaloniaProperty.Register<SkiaPdfPageControl, IRef<SKPicture>?>(nameof(Picture));

        /// <summary>
        /// Defines the <see cref="VisibleArea"/> property.
        /// </summary>
        public static readonly StyledProperty<Rect?> VisibleAreaProperty =
            AvaloniaProperty.Register<SkiaPdfPageControl, Rect?>(nameof(VisibleArea));

        /// <summary>
        /// Defines the <see cref="IsPageVisible"/> property.
        /// </summary>
        public static readonly StyledProperty<bool> IsPageVisibleProperty =
            AvaloniaProperty.Register<SkiaPdfPageControl, bool>(nameof(IsPageVisible));

        /// <summary>
        /// Gets or sets the <see cref="SKPicture"/> picture.
        /// </summary>
        [Content]
        public IRef<SKPicture>? Picture
        {
            get => GetValue(PictureProperty);
            set => SetValue(PictureProperty, value);
        }

        public Rect? VisibleArea
        {
            get => GetValue(VisibleAreaProperty);
            set => SetValue(VisibleAreaProperty, value);
        }

        public bool IsPageVisible
        {
            get => GetValue(IsPageVisibleProperty);
            set => SetValue(IsPageVisibleProperty, value);
        }
        
        public double PpiScale
        {
            get => GetValue(PpiScaleProperty);
            set => SetValue(PpiScaleProperty, value);
        }
        
        static SkiaPdfPageControl()
        {
            ClipToBoundsProperty.OverrideDefaultValue<SkiaPdfPageControl>(true);

            AffectsRender<SkiaPdfPageControl>(PictureProperty, IsPageVisibleProperty);
            AffectsMeasure<SkiaPdfPageControl>(PictureProperty);
        }

        /// <summary>
        /// This operation is executed on UI thread.
        /// </summary>
        public override void Render(DrawingContext context)
        {
            Debug.ThrowNotOnUiThread();

            var viewPort = new Rect(Bounds.Size);

            if (viewPort.IsEmpty())
            {
                base.Render(context);
                return;
            }

            if (!VisibleArea.HasValue || VisibleArea.Value.IsEmpty())
            {
                base.Render(context);
                return;
            }

            var picture = Picture?.Clone();
            if (picture?.IsAlive != true || picture.Item.CullRect.IsEmpty)
            {
                base.Render(context);
                return;
            }

            context.Custom(new SkiaDrawOperation(viewPort, PpiScale, picture));
        }
    }
