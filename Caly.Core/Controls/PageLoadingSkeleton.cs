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
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Caly.Core.Controls;

internal static class PageLoadingSkeletonLayout
{
    // All proportions are fractions of the page, so the skeleton scales with page size and zoom.
    private const double MarginX = 0.08;
    private const double MarginY = 0.06;

    private const double HeadingWidth = 0.55;   // of the content width
    private const double HeadingHeight = 0.035; // of the page height
    private const double HeadingGap = 0.05;     // of the page height

    private const double Gutter = 0.04;         // of the content width
    private const double LineHeight = 0.014;    // of the page height
    private const double LineGap = 0.010;       // of the page height
    private const double ParagraphGap = 0.022;  // of the page height

    private const double LastLineWidth = 0.65;  // of the column width
    private const double CornerRadiusRatio = 0.004;

    /// <summary>Lines per paragraph, down a column. Fixed, so every page looks the same.</summary>
    private static readonly int[] ParagraphLines = [5, 7, 6, 4, 6];

    /// <summary>
    /// Width of each line as a fraction of the column, cycled down the column so the block edges
    /// are ragged like text rather than a solid rectangle.
    /// </summary>
    private static readonly double[] LineWidths = [1.0, 0.97, 1.0, 0.93, 0.99, 0.95, 1.0];

    /// <summary>Corner radius to draw the blocks with, for the given page size.</summary>
    public static double CornerRadius(Size size) => size.Width * CornerRadiusRatio;

    /// <summary>
    /// The blocks making up the fake page, in page coordinates. Ordered heading first, then the
    /// left column top to bottom, then the right column.
    /// </summary>
    public static IReadOnlyList<Rect> Build(Size size)
    {
        double w = size.Width;
        double h = size.Height;

        if (w <= 0 || h <= 0)
        {
            return [];
        }

        double contentWidth = w * (1.0 - 2.0 * MarginX);
        double left = w * MarginX;
        double top = h * MarginY;

        var blocks = new List<Rect>
        {
            new(left, top, contentWidth * HeadingWidth, h * HeadingHeight)
        };

        double columnWidth = (contentWidth - contentWidth * Gutter) / 2.0;
        double columnTop = top + h * (HeadingHeight + HeadingGap);
        double bottomLimit = h * (1.0 - MarginY);

        AddColumn(blocks, left, columnTop, columnWidth, h, bottomLimit);
        AddColumn(blocks, left + columnWidth + contentWidth * Gutter, columnTop, columnWidth, h, bottomLimit);

        return blocks;
    }

    private static void AddColumn(List<Rect> blocks, double x, double top, double columnWidth, double pageHeight,
        double bottomLimit)
    {
        double lineHeight = pageHeight * LineHeight;
        double lineGap = pageHeight * LineGap;
        double paragraphGap = pageHeight * ParagraphGap;

        double y = top;
        int widthIndex = 0;

        foreach (int lines in ParagraphLines)
        {
            for (int line = 0; line < lines; ++line)
            {
                if (y + lineHeight > bottomLimit)
                {
                    // Ran out of page - a short page simply shows fewer paragraphs.
                    return;
                }

                bool isLastOfParagraph = line == lines - 1;
                double width = isLastOfParagraph
                    ? columnWidth * LastLineWidth
                    : columnWidth * LineWidths[widthIndex % LineWidths.Length];

                blocks.Add(new Rect(x, y, width, lineHeight));

                widthIndex++;
                y += lineHeight + lineGap;
            }

            y += paragraphGap - lineGap;
        }
    }
}
/// <summary>
/// A "fake page" shown while a page renders: grey blocks standing in for a heading and two columns
/// of text, with a highlight band sweeping across them.
/// </summary>
/// <remarks>
/// The animation is driven from <see cref="TopLevel.RequestAnimationFrame"/> rather than from a
/// style, deliberately. Style animations are torn down when a control is detached and nothing
/// re-arms them, which is why the progress ring this replaces could not simply be reused across
/// container recycling. Here the frame loop is owned by the control and restarts on attach.
/// </remarks>
internal sealed class PageLoadingSkeleton : Control
{
    private static readonly Color BaseColour = Color.FromRgb(0xE4, 0xE4, 0xE6);
    private static readonly Color HighlightColour = Color.FromRgb(0xF7, 0xF7, 0xF9);

    /// <summary>Seconds for the highlight to cross the page once.</summary>
    private const double SweepSeconds = 1.8;

    /// <summary>Half-width of the highlight band, as a fraction of the page width.</summary>
    private const double HalfBand = 0.12;

    private GeometryGroup? _geometry;
    private Size _geometrySize;
    private TimeSpan? _firstFrame;
    private double _phase;
    private bool _running;

    public PageLoadingSkeleton()
    {
        IsHitTestVisible = false;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _running = true;
        _firstFrame = null;
        RequestFrame();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _running = false;
    }

    private void RequestFrame()
    {
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(OnFrame);
    }

    private void OnFrame(TimeSpan now)
    {
        if (!_running)
        {
            return;
        }

        _firstFrame ??= now;

        double elapsed = (now - _firstFrame.Value).TotalSeconds;
        _phase = (elapsed % SweepSeconds) / SweepSeconds;

        InvalidateVisual();
        RequestFrame();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        var geometry = GetGeometry(size);
        if (geometry is null)
        {
            return;
        }

        using (context.PushGeometryClip(geometry))
        {
            context.FillRectangle(CreateShimmerBrush(), new Rect(size));
        }
    }

    private GeometryGroup? GetGeometry(Size size)
    {
        if (_geometry is not null && _geometrySize == size)
        {
            return _geometry;
        }

        var blocks = PageLoadingSkeletonLayout.Build(size);
        if (blocks.Count == 0)
        {
            return null;
        }

        double radius = PageLoadingSkeletonLayout.CornerRadius(size);
        var group = new GeometryGroup();

        foreach (var block in blocks)
        {
            group.Children.Add(new RectangleGeometry(block) { RadiusX = radius, RadiusY = radius });
        }

        _geometry = group;
        _geometrySize = size;
        return group;
    }

    private LinearGradientBrush CreateShimmerBrush()
    {
        // Sweep the band in from off the left edge and out past the right one.
        double centre = -HalfBand + _phase * (1.0 + 2.0 * HalfBand);
        double from = Math.Clamp(centre - HalfBand, 0.0, 1.0);
        double to = Math.Clamp(centre + HalfBand, 0.0, 1.0);
        double peak = Math.Clamp(centre, from, to);

        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(BaseColour, 0.0),
                new GradientStop(BaseColour, from),
                new GradientStop(HighlightColour, peak),
                new GradientStop(BaseColour, to),
                new GradientStop(BaseColour, 1.0)
            }
        };
    }
}
