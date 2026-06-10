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

using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using UglyToad.PdfPig.Geometry;

namespace Caly.Pdf.Models;

public sealed class PdfTextLine : IPdfTextElement
{
#if DEBUG
        public override string ToString()
        {
            return string.Join(' ', Words.Select(l => l.ToString()));
        }
#endif

    public bool IsInteractive { get; internal set; }

    public string? InteractiveLink { get; internal set; }

    internal ushort WordStartIndex { get; set; }

    public TextOrientation TextOrientation { get; }

    /// <summary>
    /// The rectangle completely containing the block.
    /// </summary>
    public PdfRectangle BoundingBox { get; }

    /// <summary>
    /// Text line index in the page.
    /// </summary>
    public ushort IndexInPage { get; internal set; }

    /// <summary>
    /// Text block index in the page the text line belongs to.
    /// </summary>
    public ushort TextBlockIndex { get; internal set; }

    /// <summary>
    /// The words contained in the line.
    /// </summary>
    public IReadOnlyList<PdfWord> Words { get; }

    public PdfTextLine(IReadOnlyList<PdfWord> words)
    {
        ArgumentNullException.ThrowIfNull(words, nameof(words));

        if (words.Count == 0)
        {
            throw new ArgumentException("Cannot construct text line if no word provided.", nameof(words));
        }

        Words = words;

        if (Words.Count == 1)
        {
            // This is not correct
            BoundingBox = Words[0].BoundingBox;
            TextOrientation = Words[0].TextOrientation;
        }
        else
        {
            TextOrientation = PdfTextLayerHelper.GetTextOrientation(words);

            switch (TextOrientation)
            {
                case TextOrientation.Horizontal:
                    BoundingBox = GetBoundingBoxH(words);
                    break;

                case TextOrientation.Rotate180:
                    BoundingBox = GetBoundingBox180(words);
                    break;

                case TextOrientation.Rotate90:
                    BoundingBox = GetBoundingBox90(words);
                    break;

                case TextOrientation.Rotate270:
                    BoundingBox = GetBoundingBox270(words);
                    break;

                default: // Other
                    BoundingBox = GetBoundingBoxOther(words);
                    break;
            }
        }
    }

    public bool Contains(double x, double y)
    {
        return BoundingBox.Contains(new PdfPoint(x, y), true);
    }

    public PdfWord? FindWordOver(double x, double y)
    {
        if (Words.Count == 0)
        {
            return null;
        }

        System.Diagnostics.Debug.Assert(Contains(x, y));

        var point = new PdfPoint(x, y);
        foreach (var word in Words)
        {
            if (word.BoundingBox.Contains(point, true))
            {
                return word;
            }
        }

        return null;
    }

    public PdfWord? FindNearestWord(double x, double y)
    {
        if (Words.Count == 0)
        {
            return null;
        }

        // TODO - Improve performance
        var point = new PdfPoint(x, y);
        double dist = double.MaxValue;
        PdfWord? w = null;

        foreach (var word in Words)
        {
            double localDist = Math.Min(Distances.Euclidean(point, word.BoundingBox.BottomLeft),
                                        Distances.Euclidean(point, word.BoundingBox.BottomRight));
            if (localDist < dist)
            {
                dist = localDist;
                w = word;
            }
        }

        return w;
    }

    public PdfWord GetWordInPageAt(int indexInPage)
    {
        int indexInLine = indexInPage - WordStartIndex;

        if (indexInLine < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(indexInPage));
        }

        if (indexInLine > Words.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(indexInPage));
        }

        return Words[indexInLine];
    }

    #region Bounding box
    private static PdfRectangle GetBoundingBoxH(IReadOnlyList<PdfWord> words)
    {
        var blX = double.MaxValue;
        var trX = double.MinValue;

        // Inverse Y axis - (0, 0) is top left
        var blY = double.MinValue;
        var trY = double.MaxValue;

        for (var i = 0; i < words.Count; i++)
        {
            var word = words[i];
            if (word.BoundingBox.BottomLeft.X < blX)
            {
                blX = word.BoundingBox.BottomLeft.X;
            }

            if (word.BoundingBox.BottomLeft.Y > blY)
            {
                blY = word.BoundingBox.BottomLeft.Y;
            }

            var right = word.BoundingBox.BottomLeft.X + word.BoundingBox.Width;
            if (right > trX)
            {
                trX = right;
            }

            if (word.BoundingBox.TopLeft.Y < trY)
            {
                trY = word.BoundingBox.TopLeft.Y;
            }
        }

        return new PdfRectangle(blX, blY, trX, trY);
    }

    private static PdfRectangle GetBoundingBox180(IReadOnlyList<PdfWord> words)
    {
        var blX = double.MinValue;
        var trX = double.MaxValue;

        // Inverse Y axis - (0, 0) is top left
        var blY = double.MaxValue;
        var trY = double.MinValue;

        for (var i = 0; i < words.Count; i++)
        {
            var word = words[i];
            if (word.BoundingBox.BottomLeft.X > blX)
            {
                blX = word.BoundingBox.BottomLeft.X;
            }

            if (word.BoundingBox.BottomLeft.Y < blY)
            {
                blY = word.BoundingBox.BottomLeft.Y;
            }

            var right = word.BoundingBox.BottomLeft.X - word.BoundingBox.Width;
            if (right < trX)
            {
                trX = right;
            }

            if (word.BoundingBox.TopRight.Y > trY)
            {
                trY = word.BoundingBox.TopRight.Y;
            }
        }

        return new PdfRectangle(blX, blY, trX, trY);
    }

    private static PdfRectangle GetBoundingBox90(IReadOnlyList<PdfWord> words)
    {
        GetCornerBounds(words, out double minX, out double maxX, out double minY, out double maxY);

        return new PdfRectangle(new PdfPoint(minX, maxY), new PdfPoint(minX, minY),
            new PdfPoint(maxX, maxY), new PdfPoint(maxX, minY));
    }

    private static PdfRectangle GetBoundingBox270(IReadOnlyList<PdfWord> words)
    {
        GetCornerBounds(words, out double minX, out double maxX, out double minY, out double maxY);

        return new PdfRectangle(new PdfPoint(maxX, minY), new PdfPoint(maxX, maxY),
            new PdfPoint(minX, minY), new PdfPoint(minX, maxY));
    }

    private static void GetCornerBounds(IReadOnlyList<PdfWord> words,
        out double minX, out double maxX, out double minY, out double maxY)
    {
        minX = double.MaxValue;
        maxX = double.MinValue;
        minY = double.MaxValue;
        maxY = double.MinValue;

        for (var i = 0; i < words.Count; ++i)
        {
            var bb = words[i].BoundingBox;
            Expand(bb.BottomLeft, ref minX, ref maxX, ref minY, ref maxY);
            Expand(bb.BottomRight, ref minX, ref maxX, ref minY, ref maxY);
            Expand(bb.TopLeft, ref minX, ref maxX, ref minY, ref maxY);
            Expand(bb.TopRight, ref minX, ref maxX, ref minY, ref maxY);
        }

        static void Expand(PdfPoint p, ref double minX, ref double maxX, ref double minY, ref double maxY)
        {
            if (p.X < minX) minX = p.X;
            if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
        }
    }

    private static PdfRectangle GetBoundingBoxOther(IReadOnlyList<PdfWord> words)
    {
        if (words.Count == 1)
        {
            return words[0].BoundingBox;
        }

        var baseLinePoints = words.SelectMany(r => new[]
        {
                r.BoundingBox.BottomLeft,
                r.BoundingBox.BottomRight,
            }).ToList();

        // Fitting a line through the base lines points
        // to find the orientation (slope)
        double x0 = baseLinePoints.Average(p => p.X);
        double y0 = baseLinePoints.Average(p => p.Y);
        double sumProduct = 0;
        double sumDiffSquaredX = 0;

        for (int i = 0; i < baseLinePoints.Count; i++)
        {
            var point = baseLinePoints[i];
            var x_diff = point.X - x0;
            var y_diff = point.Y - y0;
            sumProduct += x_diff * y_diff;
            sumDiffSquaredX += x_diff * x_diff;
        }

        double cos = 0;
        double sin = 1;
        if (sumDiffSquaredX > 1e-3)
        {
            // not a vertical line
            double angleRad = Math.Atan(sumProduct / sumDiffSquaredX); // -π/2 ≤ θ ≤ π/2
            cos = Math.Cos(angleRad);
            sin = Math.Sin(angleRad);
        }

        // Rotate the points to build the axis-aligned bounding box (AABB)
        var inverseRotation = new TransformationMatrix(
            cos, -sin, 0,
            sin, cos, 0,
            0, 0, 1);

        var transformedPoints = words.SelectMany(r => new[]
        {
                r.BoundingBox.BottomLeft,
                r.BoundingBox.BottomRight,
                r.BoundingBox.TopLeft,
                r.BoundingBox.TopRight
            }).Distinct().Select(p => inverseRotation.Transform(p));

        // Inverse Y axis - (0, 0) is top left
        var aabb = new PdfRectangle(transformedPoints.Min(p => p.X),
                                    transformedPoints.Max(p => p.Y),
                                    transformedPoints.Max(p => p.X),
                                    transformedPoints.Min(p => p.Y));

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
        var firstWord = words[0];
        var lastWord = words[words.Count - 1];

        var baseLineAngle = Distances.Angle(firstWord.BoundingBox.BottomLeft, lastWord.BoundingBox.BottomRight);

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
