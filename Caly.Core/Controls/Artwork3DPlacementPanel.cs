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
using Caly.Avalonia.Scene3D;
using System;

namespace Caly.Core.Controls;

/// <summary>
/// Lays a 3D control, and whatever accompanies it, over a piece of 3D artwork on a page.
/// <para>
/// The panel fills the page and works in page units, under the page's zoom. Its children are arranged
/// at the size they have on screen and drawn scaled by the inverse of the zoom: the 3D control then has
/// one framebuffer pixel per screen pixel, and its companions (a toolbar) keep one size on screen. Which
/// part of the artwork the 3D control covers is decided by <see cref="Artwork3DPlacement"/>.
/// </para>
/// </summary>
internal sealed class Artwork3DPlacementPanel : Panel
{
    /// <summary>Defines the <see cref="ArtworkBounds"/> property.</summary>
    public static readonly StyledProperty<Rect> ArtworkBoundsProperty =
        AvaloniaProperty.Register<Artwork3DPlacementPanel, Rect>(nameof(ArtworkBounds));

    /// <summary>Defines the <see cref="VisibleArea"/> property.</summary>
    public static readonly StyledProperty<Rect?> VisibleAreaProperty =
        AvaloniaProperty.Register<Artwork3DPlacementPanel, Rect?>(nameof(VisibleArea));

    /// <summary>Defines the <see cref="ZoomLevel"/> property.</summary>
    public static readonly StyledProperty<double> ZoomLevelProperty =
        AvaloniaProperty.Register<Artwork3DPlacementPanel, double>(nameof(ZoomLevel), 1.0);

    private readonly ScaleTransform _inverseZoom = new(1, 1);

    static Artwork3DPlacementPanel()
    {
        AffectsArrange<Artwork3DPlacementPanel>(ArtworkBoundsProperty, VisibleAreaProperty, ZoomLevelProperty);
    }

    /// <summary>The artwork's rectangle on the page, in page units.</summary>
    public Rect ArtworkBounds
    {
        get => GetValue(ArtworkBoundsProperty);
        set => SetValue(ArtworkBoundsProperty, value);
    }

    /// <summary>The part of the page on screen, in page units; null when none is.</summary>
    public Rect? VisibleArea
    {
        get => GetValue(VisibleAreaProperty);
        set => SetValue(VisibleAreaProperty, value);
    }

    /// <summary>The page zoom.</summary>
    public double ZoomLevel
    {
        get => GetValue(ZoomLevelProperty);
        set => SetValue(ZoomLevelProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (Control child in Children)
        {
            child.Measure(Size.Infinity);
        }

        // The page gives this panel its size; its children ask for none.
        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double zoom = ZoomLevel;
        if (!(zoom > 0) || double.IsInfinity(zoom))
        {
            zoom = 1;
        }

        Rect artwork = ArtworkBounds;
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        Artwork3DLayout layout = Artwork3DPlacement.Compute(artwork, VisibleArea, zoom, scaling);

        // Companions go to the corner of what is on screen (the 3D control may be drawn beyond it).
        Point corner = artwork.TopLeft;
        if (VisibleArea is { } visible && artwork.Intersect(visible) is { Width: > 0, Height: > 0 } onScreen)
        {
            corner = onScreen.TopLeft;
        }

        _inverseZoom.ScaleX = _inverseZoom.ScaleY = 1 / zoom;
        foreach (Control child in Children)
        {
            child.RenderTransformOrigin = RelativePoint.TopLeft;
            child.RenderTransform = _inverseZoom;

            if (child is Scene3DControl view3D)
            {
                if (layout.IsEmpty)
                {
                    view3D.Arrange(new Rect(artwork.TopLeft, default(Size)));
                }
                else
                {
                    view3D.VisibleRegion = layout.VisibleRegion;
                    view3D.Arrange(new Rect(layout.Part.TopLeft, layout.ControlSize));
                }
            }
            else
            {
                child.Arrange(new Rect(corner, layout.IsEmpty ? default : child.DesiredSize));
            }
        }

        return finalSize;
    }
}
