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
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Caly.Pdf.Models;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis;

namespace Caly.Core.Utilities;

internal static class PdfWordHelpers
{
    public static PdfRectangle GetRectangle(ReadOnlySpan<PdfRectangle> rects, TextOrientation orientation)
    {
        return orientation switch
        {
            TextOrientation.Horizontal => GetBoundingBoxH(rects),
            TextOrientation.Rotate180 => GetBoundingBox180(rects),
            TextOrientation.Rotate90 => GetBoundingBox90(rects),
            TextOrientation.Rotate270 => GetBoundingBox270(rects),
            _ => GetBoundingBoxOther(rects)
        };
    }

    public static StreamGeometry GetGeometry(PdfRectangle rect, bool isFilled = false)
    {
        var sg = new StreamGeometry();
        using (var ctx = sg.Open())
        {
            ctx.BeginFigure(new Point(rect.BottomLeft.X, rect.BottomLeft.Y), isFilled);
            ctx.LineTo(new Point(rect.TopLeft.X, rect.TopLeft.Y));
            ctx.LineTo(new Point(rect.TopRight.X, rect.TopRight.Y));
            ctx.LineTo(new Point(rect.BottomRight.X, rect.BottomRight.Y));
            ctx.EndFigure(true);
        }

        return sg;
    }

    public static PdfRectangle GetRectangle(PdfWord word)
    {
        return word.BoundingBox;
    }

    public static PdfRectangle GetRectangle(PdfWord word, int startIndex, int endIndex)
    {
        System.Diagnostics.Debug.Assert(startIndex > -1);
        System.Diagnostics.Debug.Assert(endIndex > -1);
        System.Diagnostics.Debug.Assert(startIndex <= endIndex);

        int length = endIndex - startIndex + 1;

        if (length == 1)
        {
            return word.GetLetterBoundingBox(startIndex);
        }

        Span<PdfRectangle> rects = length <= 128 ? stackalloc PdfRectangle[length] : new PdfRectangle[length];

        for (int l = startIndex; l <= endIndex; ++l)
        {
            rects[l - startIndex] = word.GetLetterBoundingBox(l);
        }
        return GetRectangle(rects, word.TextOrientation);
    }

    public static StreamGeometry GetGeometry(PdfWord word)
    {
        return GetGeometry(word.BoundingBox, true);
    }

    public static StreamGeometry? GetGeometry(PdfWord word, int startIndex, int endIndex)
    {
        System.Diagnostics.Debug.Assert(startIndex > -1);
        System.Diagnostics.Debug.Assert(endIndex > -1);
        System.Diagnostics.Debug.Assert(startIndex <= endIndex);

        int length = endIndex - startIndex + 1;

        if (length == 1)
        {
            return GetGeometry(word.GetLetterBoundingBox(startIndex), true);
        }

        Span<PdfRectangle> rects = length <= 128 ? stackalloc PdfRectangle[length] : new PdfRectangle[length];

        for (int l = startIndex; l <= endIndex; ++l)
        {
            rects[l - startIndex] = word.GetLetterBoundingBox(l);
        }
        return GetGeometry(rects, word.TextOrientation);
    }

    public static StreamGeometry GetGeometry(ReadOnlySpan<PdfRectangle> rects, TextOrientation orientation)
    {
        return GetGeometry(GetRectangle(rects, orientation), true);
    }

    /// <summary>
    /// Splits rectangles, given in reading order, into one group per text line.
    /// A new group starts each time the line index changes.
    /// </summary>
    public static void GroupByLine(IEnumerable<(ushort LineIndex, PdfRectangle Rect)> rects, List<PdfRectangle[]> lines)
    {
        var current = new List<PdfRectangle>();
        int currentLineIndex = -1;

        foreach (var (lineIndex, rect) in rects)
        {
            if (lineIndex != currentLineIndex && current.Count > 0)
            {
                lines.Add(current.ToArray());
                current.Clear();
            }

            currentLineIndex = lineIndex;
            current.Add(rect);
        }

        if (current.Count > 0)
        {
            lines.Add(current.ToArray());
        }
    }

    /// <summary>
    /// Builds a single filled geometry for the rectangles of one text line, given in reading order.
    /// <para>
    /// Consecutive rectangles are joined by a quad going from the end edge of one to the start edge
    /// of the next, so the highlight follows the baseline even when the line is rotated or curved.
    /// All figures are wound the same way and filled with <see cref="FillRule.NonZero"/>, so overlaps
    /// are painted once (no darker spots with a translucent brush).
    /// </para>
    /// </summary>
    public static StreamGeometry GetLineGeometry(IReadOnlyList<PdfRectangle> rects)
    {
        var quads = new List<Quad>(rects.Count * 2);
        GetLineQuads(rects, quads);

        var sg = new StreamGeometry();
        using (var ctx = sg.Open())
        {
            ctx.SetFillRule(FillRule.NonZero);

            foreach (var q in quads)
            {
                ctx.BeginFigure(new Point(q.A.X, q.A.Y), true);
                ctx.LineTo(new Point(q.B.X, q.B.Y));
                ctx.LineTo(new Point(q.C.X, q.C.Y));
                ctx.LineTo(new Point(q.D.X, q.D.Y));
                ctx.EndFigure(true);
            }
        }

        return sg;
    }

    internal readonly record struct Quad(PdfPoint A, PdfPoint B, PdfPoint C, PdfPoint D);

    /// <summary>
    /// The figures of <see cref="GetLineGeometry"/>: one quad per rectangle, plus one joining quad
    /// per gap between consecutive rectangles. All quads have a non-negative signed area.
    /// </summary>
    internal static void GetLineQuads(IReadOnlyList<PdfRectangle> rects, List<Quad> quads)
    {
        for (int i = 0; i < rects.Count; ++i)
        {
            var rect = rects[i];

            if (i > 0)
            {
                var previous = rects[i - 1];
                if (HasGap(previous, rect))
                {
                    quads.Add(NormaliseWinding(previous.BottomRight, previous.TopRight, rect.TopLeft, rect.BottomLeft));
                }
            }

            quads.Add(NormaliseWinding(rect.BottomLeft, rect.TopLeft, rect.TopRight, rect.BottomRight));
        }
    }

    /// <summary>
    /// <c>true</c> if <paramref name="next"/> starts after <paramref name="previous"/> ends, along the
    /// reading direction of <paramref name="previous"/>, on both the bottom and top edges. Otherwise, the
    /// joining quad would be self-intersecting (the rectangles overlap and need no joining anyway).
    /// </summary>
    private static bool HasGap(PdfRectangle previous, PdfRectangle next)
    {
        double dirX = previous.BottomRight.X - previous.BottomLeft.X;
        double dirY = previous.BottomRight.Y - previous.BottomLeft.Y;

        double bottomGap = (next.BottomLeft.X - previous.BottomRight.X) * dirX +
                           (next.BottomLeft.Y - previous.BottomRight.Y) * dirY;

        double topGap = (next.TopLeft.X - previous.TopRight.X) * dirX +
                        (next.TopLeft.Y - previous.TopRight.Y) * dirY;

        return bottomGap > 0 && topGap > 0;
    }

    /// <summary>
    /// Twice the signed area of the quad (shoelace formula).
    /// </summary>
    internal static double SignedArea(Quad q)
    {
        return q.A.X * q.B.Y - q.B.X * q.A.Y +
               q.B.X * q.C.Y - q.C.X * q.B.Y +
               q.C.X * q.D.Y - q.D.X * q.C.Y +
               q.D.X * q.A.Y - q.A.X * q.D.Y;
    }

    /// <summary>
    /// Word rectangles do not all have the same handedness (e.g. mirrored text, with a negative scale in
    /// its text matrix), so the winding is normalised for <see cref="FillRule.NonZero"/> to union the figures, rather than
    /// cancel them out where they overlap.
    /// </summary>
    private static Quad NormaliseWinding(PdfPoint a, PdfPoint b, PdfPoint c, PdfPoint d)
    {
        var quad = new Quad(a, b, c, d);
        return SignedArea(quad) < 0 ? new Quad(a, d, c, b) : quad;
    }

    #region Bounding box - Same as PdfWord
    private static PdfRectangle GetBoundingBoxH(ReadOnlySpan<PdfRectangle> rects)
    {
        var blX = double.MaxValue;
        var trX = double.MinValue;

        // Inverse Y axis - (0, 0) is top left
        var blY = double.MinValue;
        var trY = double.MaxValue;

        for (var i = 0; i < rects.Length; ++i)
        {
            var rect = rects[i];

            if (rect.BottomLeft.X < blX)
            {
                blX = rect.BottomLeft.X;
            }

            if (rect.BottomLeft.Y > blY)
            {
                blY = rect.BottomLeft.Y;
            }

            var right = rect.BottomLeft.X + rect.Width;
            if (right > trX)
            {
                trX = right;
            }

            if (rect.TopLeft.Y < trY)
            {
                trY = rect.TopLeft.Y;
            }
        }

        return new PdfRectangle(blX, blY, trX, trY);
    }

    private static PdfRectangle GetBoundingBox180(ReadOnlySpan<PdfRectangle> rects)
    {
        var blX = double.MinValue;
        var trX = double.MaxValue;

        // Inverse Y axis - (0, 0) is top left
        var blY = double.MaxValue;
        var trY = double.MinValue;

        for (var i = 0; i < rects.Length; ++i)
        {
            var rect = rects[i];

            if (rect.BottomLeft.X > blX)
            {
                blX = rect.BottomLeft.X;
            }

            if (rect.BottomLeft.Y < blY)
            {
                blY = rect.BottomLeft.Y;
            }

            var right = rect.BottomLeft.X - rect.Width;
            if (right < trX)
            {
                trX = right;
            }

            if (rect.TopRight.Y > trY)
            {
                trY = rect.TopRight.Y;
            }
        }

        return new PdfRectangle(blX, blY, trX, trY);
    }

    private static PdfRectangle GetBoundingBox90(ReadOnlySpan<PdfRectangle> rects)
    {
        var b = double.MaxValue;
        var r = double.MaxValue;
        var t = double.MinValue;
        var l = double.MinValue;

        for (var i = 0; i < rects.Length; ++i)
        {
            var rect = rects[i];
            if (rect.BottomLeft.X < b)
            {
                b = rect.BottomLeft.X;
            }

            if (rect.BottomRight.Y < r)
            {
                r = rect.BottomRight.Y;
            }

            var right = rect.BottomLeft.X - rect.Height;
            if (right > t)
            {
                t = right;
            }

            if (rect.BottomLeft.Y > l)
            {
                l = rect.BottomLeft.Y;
            }
        }

        return new PdfRectangle(new PdfPoint(b, l), new PdfPoint(b, r),
            new PdfPoint(t, l), new PdfPoint(t, r));
    }

    private static PdfRectangle GetBoundingBox270(ReadOnlySpan<PdfRectangle> rects)
    {
        var t = double.MaxValue;
        var b = double.MinValue;
        var l = double.MaxValue;
        var r = double.MinValue;

        for (var i = 0; i < rects.Length; ++i)
        {
            var rect = rects[i];
            if (rect.TopLeft.X > b)
            {
                b = rect.TopLeft.X;
            }

            if (rect.BottomLeft.Y < l)
            {
                l = rect.BottomLeft.Y;
            }

            var right = rect.BottomRight.X;
            if (right < t)
            {
                t = right;
            }

            if (rect.BottomRight.Y > r)
            {
                r = rect.BottomRight.Y;
            }
        }

        return new PdfRectangle(new PdfPoint(b, l), new PdfPoint(b, r),
            new PdfPoint(t, l), new PdfPoint(t, r));
    }

    private static PdfRectangle GetBoundingBoxOther(ReadOnlySpan<PdfRectangle> rects)
    {
        Span<PdfPoint> baseLinePoints = rects.Length * 2 <= 256
            ? stackalloc PdfPoint[rects.Length * 2]
            : new PdfPoint[rects.Length * 2];

        // Fitting a line through the baselines points
        // to find the orientation (slope)
        double x0 = 0;
        double y0 = 0;

        for (int i = 0; i < rects.Length; ++i)
        {
            var r = rects[i];
            baseLinePoints[2 * i] = r.BottomLeft;
            baseLinePoints[2 * i + 1] = r.BottomRight;

            x0 += r.BottomLeft.X + r.BottomRight.X;
            y0 += r.BottomLeft.Y + r.BottomRight.Y;
        }

        x0 /= baseLinePoints.Length;
        y0 /= baseLinePoints.Length;

        double sumProduct = 0;
        double sumDiffSquaredX = 0;

        for (int i = 0; i < baseLinePoints.Length; ++i)
        {
            var point = baseLinePoints[i];
            var xDiff = point.X - x0;
            var yDiff = point.Y - y0;
            sumProduct += xDiff * yDiff;
            sumDiffSquaredX += xDiff * xDiff;
        }

        double cos = 0;
        double sin = 1;
        if (sumDiffSquaredX > 1e-3)
        {
            // not vertical line
            double angleRad = Math.Atan(sumProduct / sumDiffSquaredX); // -π/2 ≤ θ ≤ π/2
            cos = Math.Cos(angleRad);
            sin = Math.Sin(angleRad);
        }

        // Rotate the points to build the axis-aligned bounding box (AABB)
        var inverseRotation = new TransformationMatrix(
            cos, -sin, 0,
            sin, cos, 0,
            0, 0, 1);

        double minX = double.MaxValue;
        double minY = double.MaxValue;
        double maxX = double.MinValue;
        double maxY = double.MinValue;

        void UpdateMinMax(PdfPoint point)
        {
            if (point.X < minX) minX = point.X;
            if (point.Y < minY) minY = point.Y;
            if (point.X > maxX) maxX = point.X;
            if (point.Y > maxY) maxY = point.Y;
        }

        foreach (var r in rects)
        {
            UpdateMinMax(inverseRotation.Transform(r.BottomLeft));
            UpdateMinMax(inverseRotation.Transform(r.BottomRight));
            UpdateMinMax(inverseRotation.Transform(r.TopLeft));
            UpdateMinMax(inverseRotation.Transform(r.TopRight));
        }

        // Inverse Y axis - (0, 0) is top left
        var aabb = new PdfRectangle(minX, maxY, maxX, minY);

        // Rotate back the AABB to obtain to oriented bounding box (OBB)
        var rotateBack = new TransformationMatrix(
            cos, sin, 0,
            -sin, cos, 0,
            0, 0, 1);

        // Candidates bounding boxes
        var obb = rotateBack.Transform(aabb);
        var obb1 = new PdfRectangle(obb.BottomLeft, obb.TopLeft, obb.BottomRight, obb.TopRight);
        var obb2 = new PdfRectangle(obb.BottomRight, obb.BottomLeft, obb.TopRight, obb.TopLeft);
        var obb3 = new PdfRectangle(obb.TopRight, obb.BottomRight, obb.TopLeft, obb.BottomLeft);

        // Find the orientation of the OBB, using the baseline angle
        // Assumes word order is correct
        var firstRect = rects[0];
        var lastRect = rects[^1];

        var baseLineAngle = Math.Atan2(
            lastRect.BottomRight.Y - firstRect.BottomLeft.Y,
            lastRect.BottomRight.X - firstRect.BottomLeft.X) * 180 / Math.PI;

        double deltaAngle = Math.Abs(Distances.BoundAngle180(obb.Rotation - baseLineAngle));
        double deltaAngle1 = Math.Abs(Distances.BoundAngle180(obb1.Rotation - baseLineAngle));
        if (deltaAngle1 < deltaAngle)
        {
            deltaAngle = deltaAngle1;
            obb = obb1;
        }

        double deltaAngle2 = Math.Abs(Distances.BoundAngle180(obb2.Rotation - baseLineAngle));
        if (deltaAngle2 < deltaAngle)
        {
            deltaAngle = deltaAngle2;
            obb = obb2;
        }

        double deltaAngle3 = Math.Abs(Distances.BoundAngle180(obb3.Rotation - baseLineAngle));
        if (deltaAngle3 < deltaAngle)
        {
            obb = obb3;
        }

        return obb;
    }
    #endregion
}
