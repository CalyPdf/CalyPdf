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
using System;

namespace Caly.Core.Controls;

/// <summary>
/// Where a 3D control is put over its artwork, and what it shows there.
/// </summary>
/// <param name="Part">The part of the artwork's rectangle the control covers, in page units.</param>
/// <param name="ControlSize">
/// The size to arrange the control with: its size on screen, in device-independent pixels. It is drawn
/// scaled by the inverse of the page zoom, so that it covers <paramref name="Part"/> on the page.
/// </param>
/// <param name="VisibleRegion"><paramref name="Part"/> in fractions of the whole artwork.</param>
internal readonly record struct Artwork3DLayout(Rect Part, Size ControlSize, Rect VisibleRegion)
{
    /// <summary>Nothing of the artwork is on screen: there is nothing to draw.</summary>
    public bool IsEmpty => !(ControlSize.Width > 0 && ControlSize.Height > 0);
}

/// <summary>
/// Places a 3D control on a zoomed page.
/// <para>
/// An OpenGL control has as many pixels as its own bounds, whatever the transforms above it. Inside a
/// page that is zoomed by a layout transform it is therefore arranged at the size it has on screen and
/// scaled back by the inverse of the zoom (see probes/E in the 3D workspace). That gives a sharp picture
/// at every zoom, and a framebuffer that grows with the zoom squared: past a budget only the visible
/// part of the view is drawn, and the control is told which part that is.
/// </para>
/// </summary>
internal static class Artwork3DPlacement
{
    /// <summary>
    /// The most pixels a view is drawn with as a whole. Below this the control keeps one size while its
    /// page scrolls, so that nothing is reallocated; it is also the most the control anti-aliases with
    /// four samples.
    /// </summary>
    public const double MaxUncroppedPixels = 3_000_000;

    /// <summary>
    /// What a cropped view is drawn beyond the visible part on each side, in screen units: the page's
    /// visible area is known one layout pass after a scroll.
    /// </summary>
    public const double CropMargin = 64;

    /// <param name="artwork">The artwork's rectangle, in page units.</param>
    /// <param name="visibleArea">The part of the page on screen, in the same units; null when none is.</param>
    /// <param name="zoom">The page zoom.</param>
    /// <param name="renderScaling">Device pixels per device-independent pixel.</param>
    public static Artwork3DLayout Compute(Rect artwork, Rect? visibleArea, double zoom, double renderScaling)
    {
        if (!(zoom > 0) || double.IsInfinity(zoom))
        {
            zoom = 1;
        }

        if (!(renderScaling > 0) || double.IsInfinity(renderScaling))
        {
            renderScaling = 1;
        }

        if (!(artwork.Width > 0 && artwork.Height > 0) || double.IsInfinity(artwork.Width) || double.IsInfinity(artwork.Height) ||
            visibleArea is not { } visible)
        {
            return default;
        }

        Rect onScreen = artwork.Intersect(visible);
        if (!(onScreen.Width > 0 && onScreen.Height > 0))
        {
            return default;
        }

        double pixelsPerUnit = zoom * renderScaling;
        Rect part = artwork;
        if (artwork.Width * pixelsPerUnit * (artwork.Height * pixelsPerUnit) > MaxUncroppedPixels)
        {
            Rect wanted = artwork.Intersect(visible.Inflate(CropMargin / zoom));

            // Edges on whole device pixels of the view, moved outwards and never past the artwork.
            double left = Math.Floor((wanted.X - artwork.X) * pixelsPerUnit + 1e-6) / pixelsPerUnit;
            double top = Math.Floor((wanted.Y - artwork.Y) * pixelsPerUnit + 1e-6) / pixelsPerUnit;
            double right = Math.Min(artwork.Width, Math.Ceiling((wanted.Right - artwork.X) * pixelsPerUnit - 1e-6) / pixelsPerUnit);
            double bottom = Math.Min(artwork.Height, Math.Ceiling((wanted.Bottom - artwork.Y) * pixelsPerUnit - 1e-6) / pixelsPerUnit);
            part = new Rect(artwork.X + left, artwork.Y + top, right - left, bottom - top);
        }

        return new Artwork3DLayout(
            part,
            new Size(part.Width * zoom, part.Height * zoom),
            new Rect((part.X - artwork.X) / artwork.Width, (part.Y - artwork.Y) / artwork.Height, part.Width / artwork.Width, part.Height / artwork.Height));
    }
}
