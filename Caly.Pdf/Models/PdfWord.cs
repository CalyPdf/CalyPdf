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

using CommunityToolkit.HighPerformance.Buffers;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using UglyToad.PdfPig.Geometry;

namespace Caly.Pdf.Models;

public sealed class PdfWord : IPdfTextElement
{
#if DEBUG
        public override string ToString()
        {
            return Value;
        }
#endif

    private readonly ushort[]? _toCharIndex;

    private readonly float[]? _letterPositions;

    private readonly PdfRectangle[]? _lettersBoundingBoxes;

    public TextOrientation TextOrientation { get; }

    /// <summary>
    /// The rectangle completely containing the block.
    /// </summary>
    public PdfRectangle BoundingBox { get; }

    /// <summary>
    /// Word index in the page.
    /// </summary>
    public ushort IndexInPage { get; internal set; }

    /// <summary>
    /// Text line index in the page the word belongs to.
    /// </summary>
    public ushort TextLineIndex { get; internal set; }

    /// <summary>
    /// Text block index in the page the word belongs to.
    /// </summary>
    public ushort TextBlockIndex { get; internal set; }

    public string Value { get; }

    public ushort Count { get; }

    public PdfWord(IReadOnlyList<PdfLetter> letters)
    {
        ArgumentNullException.ThrowIfNull(letters, nameof(letters));

        if (letters.Count == 0)
        {
            throw new ArgumentException("Cannot construct word if no letters provided.", nameof(letters));
        }

        TextOrientation = PdfTextLayerHelper.GetTextOrientation(letters);

        Count = (ushort)letters.Count;

        var firstLetter = letters[0];
        int charsCount = firstLetter.Value.Length;

        switch (TextOrientation)
        {
            case TextOrientation.Horizontal:
                BoundingBox = GetBoundingBoxH(letters);
                break;

            case TextOrientation.Rotate180:
                BoundingBox = GetBoundingBox180(letters);
                break;

            case TextOrientation.Rotate90:
                BoundingBox = GetBoundingBox90(letters);
                break;

            case TextOrientation.Rotate270:
                BoundingBox = GetBoundingBox270(letters);
                break;

            default: // Other
                BoundingBox = GetBoundingBoxOther(letters);
                break;
        }

        if (Count == 1)
        {
            // Do nothing
        }
        else if (TextOrientation == TextOrientation.Other || !IsConsistent(BoundingBox, letters))
        {
            // We keep all bounding boxes
            _lettersBoundingBoxes = new PdfRectangle[letters.Count];
            _lettersBoundingBoxes[0] = firstLetter.BoundingBox;

            for (int i = 1; i < letters.Count; ++i)
            {
                var letter = letters[i];

                _lettersBoundingBoxes[i] = letter.BoundingBox;
                charsCount += letter.Value.Length;
            }
        }
        else
        {
            // Only keep positions
            _letterPositions = new float[letters.Count - 1];

            for (int i = 0; i < letters.Count - 1; ++i)
            {
                var nextStart = letters[i + 1].BoundingBox.BottomLeft;
                float dx = (float)(nextStart.X - BoundingBox.BottomLeft.X);
                float dy = (float)(nextStart.Y - BoundingBox.BottomLeft.Y);
                _letterPositions[i] = MathF.Sqrt(dx * dx + dy * dy);
                charsCount += letters[i + 1].Value.Length;
            }
        }

        Span<char> chars = charsCount <= 512 ? stackalloc char[charsCount] : new char[charsCount];

        if (chars.Length == letters.Count)
        {
            for (int l = 0; l < letters.Count; ++l)
            {
                var letter = letters[l].Value.AsSpan();
                System.Diagnostics.Debug.Assert(letter.Length == 1);
                chars[l] = letter[0];
            }
        }
        else
        {
            // Usually because of ligatures
            _toCharIndex = new ushort[letters.Count];

            ushort k = 0;
            for (int l = 0; l < letters.Count; ++l)
            {
                var letter = letters[l].Value.AsSpan();
                for (int c = 0; c < letter.Length; ++c)
                {
                    chars[k++] = letter[c];
                }

                _toCharIndex[l] = k;
            }
        }

        Value = StringPool.Shared.GetOrAdd(chars);

        static bool IsConsistent(PdfRectangle bbox, IReadOnlyList<PdfLetter> letters)
        {
            // If the sum of the letters width is too different from the bbox width,
            // we don't trust knowing where the letters are based on their width
            double expectedWidth = letters.Sum(l => l.BoundingBox.Width);
            double delta = expectedWidth / bbox.Width;
            if (delta < 0.8 || delta > 1.2)
            {
                return false;
            }

            return true;
        }
    }

    public int GetCharIndexFromBboxIndex(int bboxIndex)
    {
        if (_toCharIndex is null)
        {
            return bboxIndex;
        }

        return _toCharIndex[bboxIndex] - 1;
    }

    public bool Contains(double x, double y)
    {
        return BoundingBox.Contains(new PdfPoint(x, y), true);
    }

    public PdfRectangle GetLetterBoundingBox(int index)
    {
        if (Count == 1)
        {
            return BoundingBox;
        }

        if (_letterPositions is not null)
        {
            float startPosition = index == 0 ? 0 : _letterPositions[index - 1];
            float endPosition = index == Count - 1 ? (float)BoundingBox.Width : _letterPositions[index];

            switch (TextOrientation)
            {
                case TextOrientation.Horizontal:
                    {
                        double startX = BoundingBox.BottomLeft.X + startPosition;
                        double endX = BoundingBox.BottomLeft.X + endPosition;
                        double startY = BoundingBox.BottomLeft.Y;
                        double endY = BoundingBox.TopLeft.Y;
                        var rect = new PdfRectangle(startX, startY, endX, endY);
                        System.Diagnostics.Debug.Assert(rect.Rotation == 0);
                        return rect;
                    }

                case TextOrientation.Rotate180:
                    {
                        double startX = BoundingBox.BottomLeft.X - startPosition;
                        double endX = BoundingBox.BottomLeft.X - endPosition;
                        double startY = BoundingBox.BottomLeft.Y;
                        double endY = BoundingBox.TopLeft.Y;
                        return new PdfRectangle(startX, startY, endX, endY);
                    }

                case TextOrientation.Rotate270:
                    {
                        double l = BoundingBox.BottomLeft.Y + startPosition;
                        double r = BoundingBox.BottomLeft.Y + endPosition;
                        double b = BoundingBox.TopLeft.X;
                        double t = BoundingBox.BottomRight.X;
                        return new PdfRectangle(new PdfPoint(b, l), new PdfPoint(b, r),
                            new PdfPoint(t, l), new PdfPoint(t, r));
                    }

                case TextOrientation.Rotate90:
                    {
                        double l = BoundingBox.BottomLeft.Y - startPosition;
                        double r = BoundingBox.BottomLeft.Y - endPosition;
                        double b = BoundingBox.TopLeft.X;
                        double t = BoundingBox.BottomRight.X;
                        return new PdfRectangle(new PdfPoint(b, l), new PdfPoint(b, r),
                            new PdfPoint(t, l), new PdfPoint(t, r));
                    }
            }
        }

        if (_lettersBoundingBoxes is not null)
        {
            return _lettersBoundingBoxes[index];
        }

        throw new Exception();
    }

    public double GetWithinLetterOffset(int index, double x, double y)
    {
        var point = new PdfPoint(x, y);

        if (Count == 1)
        {
            return PdfPointExtensions.ProjectPointOnLineM(point, BoundingBox.BottomLeft, BoundingBox.BottomRight);
        }

        if (_letterPositions is not null)
        {
            double position = PdfPointExtensions.ProjectPointOnLineM(point, BoundingBox.BottomLeft, BoundingBox.BottomRight);
            if (index == 0)
            {
                return position;
            }

            return position - _letterPositions[index - 1];
        }

        if (_lettersBoundingBoxes is not null)
        {
            var bbox = _lettersBoundingBoxes[index];
            return PdfPointExtensions.ProjectPointOnLineM(point, bbox.BottomLeft, bbox.BottomRight);
        }

        return double.NaN;
    }

    public int FindLetterIndexOver(double x, double y)
    {
        if (Count == 0)
        {
            return -1;
        }

        var point = new PdfPoint(x, y);
        if (!BoundingBox.Contains(point))
        {
            return -1;
        }

        if (Count == 1)
        {
            return 0;
        }

        if (_letterPositions is not null)
        {
            double position = PdfPointExtensions.ProjectPointOnLineM(point, BoundingBox.BottomLeft, BoundingBox.BottomRight);

            for (int i = 0; i < Count - 1; ++i)
            {
                if (position <= _letterPositions[i] / BoundingBox.Width)
                {
                    return i;
                }
            }

            return Count - 1;
        }

        if (_lettersBoundingBoxes is not null)
        {
            for (int i = 0; i < Count; ++i)
            {
                if (_lettersBoundingBoxes[i].Contains(point, true))
                {
                    return i;
                }
            }
        }

        return -1;
    }

    public int FindNearestLetterIndex(double x, double y)
    {
        if (Count == 0)
        {
            return -1;
        }

        if (Count == 1)
        {
            return 0;
        }

        var point = new PdfPoint(x, y);
        double dist = double.MaxValue;
        int index = -1;

        if (_letterPositions is not null)
        {
            double position = PdfPointExtensions.ProjectPointOnLineM(point, BoundingBox.BottomLeft, BoundingBox.BottomRight);
            double localDist = 0;
            for (int i = 0; i < Count - 1; ++i)
            {
                var letter = _letterPositions[i] / BoundingBox.Width;
                localDist = Math.Abs(position - letter);
                if (localDist < dist)
                {
                    dist = localDist;
                    index = i;
                }
            }

            localDist = Math.Abs(position - 1);
            if (localDist < dist)
            {
                index = Count - 1;
            }
        }
        else if (_lettersBoundingBoxes is not null)
        {
            for (int i = 0; i < Count; ++i)
            {
                var letter = _lettersBoundingBoxes[i];
                double localDist = Distances.Euclidean(point, letter.BottomRight);
                if (localDist < dist)
                {
                    dist = localDist;
                    index = i;
                }
            }
        }

        return index;
    }

    #region Bounding box
    private static PdfRectangle GetBoundingBoxH(IReadOnlyList<PdfLetter> letters)
    {
        var blX = double.MaxValue;
        var trX = double.MinValue;

        // Inverse Y axis - (0, 0) is top left
        var blY = double.MinValue;
        var trY = double.MaxValue;

        for (var i = 0; i < letters.Count; i++)
        {
            var letter = letters[i];

            if (letter.StartBaseLine.X < blX)
            {
                blX = letter.StartBaseLine.X;
            }

            if (letter.StartBaseLine.Y > blY)
            {
                blY = letter.StartBaseLine.Y;
            }

            var right = letter.StartBaseLine.X + letter.BoundingBox.Width;
            if (right > trX)
            {
                trX = right;
            }

            if (letter.BoundingBox.TopLeft.Y < trY)
            {
                trY = letter.BoundingBox.TopLeft.Y;
            }
        }

        return new PdfRectangle(blX, blY, trX, trY);
    }

    private static PdfRectangle GetBoundingBox180(IReadOnlyList<PdfLetter> letters)
    {
        var blX = double.MinValue;
        var trX = double.MaxValue;

        // Inverse Y axis - (0, 0) is top left
        var blY = double.MaxValue;
        var trY = double.MinValue;

        for (var i = 0; i < letters.Count; i++)
        {
            var letter = letters[i];

            if (letter.StartBaseLine.X > blX)
            {
                blX = letter.StartBaseLine.X;
            }

            if (letter.StartBaseLine.Y < blY)
            {
                blY = letter.StartBaseLine.Y;
            }

            var right = letter.StartBaseLine.X - letter.BoundingBox.Width;
            if (right < trX)
            {
                trX = right;
            }

            if (letter.BoundingBox.TopRight.Y > trY)
            {
                trY = letter.BoundingBox.TopRight.Y;
            }
        }

        return new PdfRectangle(blX, blY, trX, trY);
    }

    private static PdfRectangle GetBoundingBox90(IReadOnlyList<PdfLetter> letters)
    {
        GetCornerBounds(letters, out double minX, out double maxX, out double minY, out double maxY);

        return new PdfRectangle(new PdfPoint(minX, maxY), new PdfPoint(minX, minY),
            new PdfPoint(maxX, maxY), new PdfPoint(maxX, minY));
    }

    private static PdfRectangle GetBoundingBox270(IReadOnlyList<PdfLetter> letters)
    {
        GetCornerBounds(letters, out double minX, out double maxX, out double minY, out double maxY);

        return new PdfRectangle(new PdfPoint(maxX, minY), new PdfPoint(maxX, maxY),
            new PdfPoint(minX, minY), new PdfPoint(minX, maxY));
    }

    private static void GetCornerBounds(IReadOnlyList<PdfLetter> letters,
        out double minX, out double maxX, out double minY, out double maxY)
    {
        minX = double.MaxValue;
        maxX = double.MinValue;
        minY = double.MaxValue;
        maxY = double.MinValue;

        for (var i = 0; i < letters.Count; ++i)
        {
            var bb = letters[i].BoundingBox;
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

    private static PdfRectangle GetBoundingBoxOther(IReadOnlyList<PdfLetter> letters)
    {
        if (letters.Count == 1)
        {
            return letters[0].BoundingBox;
        }

        var baseLinePoints = letters.SelectMany(r => new[]
        {
                r.StartBaseLine,
                r.EndBaseLine,
            }).ToArray();

        // Fitting a line through the base lines points
        // to find the orientation (slope)
        double x0 = baseLinePoints.Average(p => p.X);
        double y0 = baseLinePoints.Average(p => p.Y);
        double sumProduct = 0;
        double sumDiffSquaredX = 0;

        for (int i = 0; i < baseLinePoints.Length; i++)
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

        var transformedPoints = letters.SelectMany(r => new[]
        {
                r.StartBaseLine,
                r.EndBaseLine,
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
        var firstLetter = letters[0];
        var lastLetter = letters[letters.Count - 1];

        var baseLineAngle = Math.Atan2(
            lastLetter.EndBaseLine.Y - firstLetter.StartBaseLine.Y,
            lastLetter.EndBaseLine.X - firstLetter.StartBaseLine.X) * 180 / Math.PI;

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
