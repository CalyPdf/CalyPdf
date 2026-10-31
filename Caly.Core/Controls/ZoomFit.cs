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

using System;
using Avalonia;
using Caly.Core.Models;

namespace Caly.Core.Controls;

/// <summary>
/// What <see cref="ZoomFit.TryCompute"/> fits, and into what. Sizes are in device-independent
/// pixels: <see cref="Page"/>, <see cref="Content"/> and <see cref="DocumentHeight"/> are unscaled
/// (zoom 1), the others are on screen.
/// </summary>
/// <param name="Mode">The fit mode.</param>
/// <param name="Viewport">The scroll viewer's outer size: the room the pages get with no scroll bar shown.</param>
/// <param name="ScrollBarThickness">The room a shown scroll bar takes from the viewport; 0 when the bars overlay the content.</param>
/// <param name="Page">The reference page, as displayed (rotation included).</param>
/// <param name="Content">The reference page's content box, in the displayed page's coordinates; <c>null</c> when unknown.</param>
/// <param name="DocumentHeight">The height of all the pages, without the gaps between them.</param>
/// <param name="GapCount">The number of page gaps in the document height.</param>
/// <param name="Gap">The on-screen gap below each page.</param>
/// <param name="MinZoom">The smallest zoom allowed.</param>
/// <param name="MaxZoom">The largest zoom allowed.</param>
internal readonly record struct ZoomFitInput(
    ZoomMode Mode,
    Size Viewport,
    double ScrollBarThickness,
    Size Page,
    Rect? Content,
    double DocumentHeight,
    int GapCount,
    double Gap,
    double MinZoom,
    double MaxZoom);

/// <summary>
/// The zoom each fit mode gives. Pure: <see cref="Controls.PageItemsControl"/> gathers the inputs
/// from its layout.
/// </summary>
internal static class ZoomFit
{
    /// <summary>
    /// On-screen margin every fit keeps on either side of what it fits: the page in fit page and
    /// fit width, the content box in the content modes. Fit content also keeps it above and below
    /// the box; fit page keeps none there, as it lands the page's top edge on the viewport's.
    /// <para>
    /// It also leaves room for the pages to end up a little wider than the fitted page, where 1 px
    /// of <see cref="FitSlack"/> alone would bring the horizontal scroll bar: a neighbouring page
    /// slightly wider (page sizes vary in scanned documents), or the pages panel rounding a
    /// fractional page width up to a whole pixel before the zoom scales it.
    /// </para>
    /// </summary>
    public const double FitMargin = 10;

    /// <summary>
    /// Content modes zoom no further than this (800%) on nearly empty pages, unless fitting the
    /// whole page already zooms further.
    /// </summary>
    public const double MaxContentZoom = 8;

    /// <summary>
    /// Room left unused by every fit. With layout rounding, Avalonia rounds desired sizes up to
    /// whole device pixels (at scale 1 a bare <c>Math.Ceiling</c>), so an exact fit such as
    /// <c>100 × 3.84 = 384.00000000000006</c> would round over the viewport and bring a scroll bar.
    /// </summary>
    public const double FitSlack = 1;

    private const double OverflowTolerance = 1e-6;

    /// <summary>
    /// Whether <paramref name="mode"/> fits the content box rather than the page.
    /// </summary>
    public static bool IsContentMode(ZoomMode mode) => mode is ZoomMode.FitContent or ZoomMode.FitContentWidth;

    /// <summary>
    /// Computes the zoom <paramref name="input"/>'s mode gives.
    /// <para>
    /// Shown scroll bars take room from the viewport, and whether they show depends on the zoom.
    /// Each combination of bars is tried in turn, and the first one the resulting zoom agrees with
    /// wins: the content overflows exactly on the axes that have a bar. When none agrees (the
    /// content fits only once a bar's room is taken away), the room of both is kept; a bar may
    /// then hide and leave a strip of desk, which is stable because fits never react to the
    /// viewport alone changing.
    /// </para>
    /// </summary>
    /// <returns><c>false</c> in <see cref="ZoomMode.Manual"/>, when there is nothing to fit or nothing
    /// to fit into, or when no zoom fits the zoom range (minimum above maximum, or either is NaN).</returns>
    public static bool TryCompute(in ZoomFitInput input, out double zoom)
    {
        zoom = 0;

        if (input.Mode == ZoomMode.Manual ||
            !(input.Viewport.Width > 0) || !(input.Viewport.Height > 0) ||
            !(input.Page.Width > 0) || !(input.Page.Height > 0) ||
            !(input.MinZoom <= input.MaxZoom)) // Also false when either is NaN; Math.Clamp below throws if Min > Max
        {
            return false;
        }

        ZoomMode mode = GetEffectiveMode(input.Mode, input.Content);
        double thickness = Math.Max(0, input.ScrollBarThickness);

        for (int state = 0; state < 4; ++state)
        {
            bool vertical = (state & 1) != 0;
            bool horizontal = (state & 2) != 0;

            double availableWidth = input.Viewport.Width - (vertical ? thickness : 0);
            double availableHeight = input.Viewport.Height - (horizontal ? thickness : 0);

            zoom = Math.Clamp(GetRawZoom(mode, in input, availableWidth - FitSlack, availableHeight - FitSlack),
                input.MinZoom, input.MaxZoom);

            bool needsVertical = input.DocumentHeight * zoom + input.GapCount * input.Gap >
                                 availableHeight + OverflowTolerance;
            bool needsHorizontal = input.Page.Width * zoom > availableWidth + OverflowTolerance;

            if (needsVertical == vertical && needsHorizontal == horizontal)
            {
                return true;
            }
        }

        // No combination agrees: keep the last one, which reserves both bars' room.
        return true;
    }

    /// <summary>
    /// A content mode without a usable content box fits the page instead.
    /// </summary>
    private static ZoomMode GetEffectiveMode(ZoomMode mode, Rect? content)
    {
        if (!IsContentMode(mode) || content is { Width: > 0, Height: > 0 })
        {
            return mode;
        }

        return mode == ZoomMode.FitContent ? ZoomMode.FitPage : ZoomMode.FitWidth;
    }

    private static double GetRawZoom(ZoomMode mode, in ZoomFitInput input, double width, double height)
    {
        // Fit page shares fit width's side margins, so that it never zooms past fit width.
        double fitWidth = (width - 2 * FitMargin) / input.Page.Width;
        double fitPage = Math.Min(fitWidth, (height - input.Gap) / input.Page.Height);

        switch (mode)
        {
            case ZoomMode.FitPage:
                return fitPage;

            case ZoomMode.FitWidth:
                return fitWidth;

            case ZoomMode.FitContent:
            {
                Rect content = input.Content!.Value;
                double fitContent = Math.Min((width - 2 * FitMargin) / content.Width,
                    (height - 2 * FitMargin) / content.Height);
                return Math.Min(fitContent, Math.Max(MaxContentZoom, fitPage));
            }

            case ZoomMode.FitContentWidth:
            {
                Rect content = input.Content!.Value;
                double fitContentWidth = (width - 2 * FitMargin) / content.Width;
                return Math.Min(fitContentWidth, Math.Max(MaxContentZoom, fitWidth));
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }
    }
}
