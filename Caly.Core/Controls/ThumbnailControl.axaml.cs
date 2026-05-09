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
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Caly.Core.Controls;

/// <summary>
/// Control that represents a single page thumbnail in a PDF document, with its visible area rectangle.
/// </summary>
public sealed class ThumbnailControl : TemplatedControl
{
    /*
     * See PDF Reference 1.7 - C.2 Architectural limits
     * Thumbnail images should be no larger than 106 by 106 samples, and should be created at one-eighth scale for 8.5-by-11-inch and A4-size pages.
     */

    private const double _borderThickness = 2;

#if DEBUG
    private static readonly IImmutableSolidColorBrush BackgroundBrush = Brushes.HotPink;
#else
    private static readonly IImmutableSolidColorBrush BackgroundBrush = Brushes.White;
#endif
    
    private static readonly Color AreaColor = Colors.DodgerBlue;
    private static readonly IImmutableBrush AreaBrush = new SolidColorBrush(AreaColor).ToImmutable();
    private static readonly IImmutableBrush AreaTransparentBrush = new SolidColorBrush(AreaColor, 0.3).ToImmutable();
    private static readonly ImmutablePen AreaPen = new Pen() { Brush = AreaBrush, Thickness = _borderThickness }.ToImmutable();

    private Matrix _scale = Matrix.Identity;

    /// <summary>
    /// Defines the <see cref="VisibleArea"/> property.
    /// </summary>
    public static readonly StyledProperty<Rect?> VisibleAreaProperty =
        AvaloniaProperty.Register<ThumbnailControl, Rect?>(nameof(VisibleArea));

    /// <summary>
    /// Defines the <see cref="ThumbnailSize"/> property.
    /// </summary>
    public static readonly StyledProperty<PixelSize> ThumbnailSizeProperty =
        AvaloniaProperty.Register<ThumbnailControl, PixelSize>(nameof(ThumbnailSize));

    /// <summary>
    /// Defines the <see cref="PageSize"/> property.
    /// </summary>
    public static readonly StyledProperty<Size> PageSizeProperty =
        AvaloniaProperty.Register<ThumbnailControl, Size>(nameof(PageSize));

    /// <summary>
    /// Defines the <see cref="Thumbnail"/> property.
    /// </summary>
    public static readonly StyledProperty<IImage?> ThumbnailProperty =
        AvaloniaProperty.Register<ThumbnailControl, IImage?>(nameof(Thumbnail));

    static ThumbnailControl()
    {
        AffectsRender<ThumbnailControl>(ThumbnailProperty, VisibleAreaProperty,
            ThumbnailSizeProperty, PageSizeProperty);
        AffectsMeasure<ThumbnailControl>(ThumbnailSizeProperty, PageSizeProperty);
    }

    public Rect? VisibleArea
    {
        get => GetValue(VisibleAreaProperty);
        set => SetValue(VisibleAreaProperty, value);
    }

    public PixelSize ThumbnailSize
    {
        get => GetValue(ThumbnailSizeProperty);
        set => SetValue(ThumbnailSizeProperty, value);
    }

    public Size PageSize
    {
        get => GetValue(PageSizeProperty);
        set => SetValue(PageSizeProperty, value);
    }
    
    public IImage? Thumbnail
    {
        get => GetValue(ThumbnailProperty);
        set => SetValue(ThumbnailProperty, value);
    }
    
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ThumbnailSizeProperty ||
            change.Property == PageSizeProperty)
        {
            UpdateScaleMatrix();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        try
        {
            var thumbnail = Thumbnail;
            if (thumbnail is not null && Bounds is { Width: > 0, Height: > 0 })
            {
                context.DrawImage(thumbnail, Bounds);
            }
            else
            {
                context.FillRectangle(BackgroundBrush, Bounds);
            }
        }
        catch (Exception e)
        {
            // We just ignore for the moment
            context.FillRectangle(BackgroundBrush, Bounds);
            Debug.WriteExceptionToFile(e);
        }

        if (!VisibleArea.HasValue)
        {
            return;
        }

        var area = GetAdjustedVisibleArea(VisibleArea.Value.TransformToAABB(_scale), out bool isFull);
        if (isFull)
        {
            context.DrawRectangle(AreaTransparentBrush, AreaPen, area);
        }
        else
        {
            context.DrawRectangle(AreaBrush, null, area);
        }
    }

    private static Rect GetAdjustedVisibleArea(in Rect area, out bool isFull)
    {
        const double minSize = _borderThickness * 2;
        isFull = true;

        switch (area)
        {
            case { Width: > minSize, Height: > minSize }:
            {
                return area.Deflate(_borderThickness / 2.0);
            }
            case { Width: <= minSize, Height: <= minSize }:
            {
                isFull = false;
                return area.CenterRect(new Rect(0, 0, minSize, minSize));
            }
            default:
            {
                isFull = false;
                return area.CenterRect(area.Width > minSize
                    ? new Rect(0, 0, area.Width, minSize)
                    : new Rect(0, 0, minSize, area.Height));
            }
        }
    }

    private void UpdateScaleMatrix()
    {
        if (IsNotValid(PageSize))
        {
            return;
        }

        double ratio = Math.Round(ThumbnailSize.Height / PageSize.Height, 7);
        _scale = Matrix.CreateScale(ratio, ratio);
    }

    private static bool IsNotValid(Size v)
    {
        return v.Height <= 0 ||
               v.Width <= 0 ||
               double.IsInfinity(v.Height) || double.IsNaN(v.Height) ||
               double.IsInfinity(v.Width) || double.IsNaN(v.Width);
    }
}
