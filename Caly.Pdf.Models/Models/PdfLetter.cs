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

namespace Caly.Pdf.Models;

public sealed class PdfLetter : IPdfTextElement
{
    public string Value { get; }

    public TextOrientation TextOrientation { get; }

    /// <summary>
    /// The rectangle completely containing the block.
    /// </summary>
    public PdfRectangle BoundingBox { get; }

    /// <summary>
    /// The placement position of the character in PDF space (the start point of the baseline).
    /// </summary>
    public PdfPoint StartBaseLine => BoundingBox.BottomLeft;

    /// <summary>
    /// The end point of the baseline.
    /// </summary>
    public PdfPoint EndBaseLine => BoundingBox.BottomRight;

    /// <summary>
    /// The size of the font in points.
    /// </summary>
    public float PointSize { get; }

    /// <summary>
    /// Sequence number of the ShowText operation that printed this letter.
    /// </summary>
    public int TextSequence { get; }

    public PdfLetter(string value, PdfRectangle boundingBox, float pointSize, int textSequence)
    {
        Value = StringPool.Shared.GetOrAdd(value);
        BoundingBox = boundingBox;
        PointSize = pointSize;
        TextSequence = textSequence;

        TextOrientation = GetTextOrientation();
    }

    private TextOrientation GetTextOrientation()
    {
        if (Math.Abs(StartBaseLine.Y - EndBaseLine.Y) < 10e-5)
        {
            if (Math.Abs(StartBaseLine.X - EndBaseLine.X) < 10e-5)
            {
                // Start and End point are the same
                return GetTextOrientationRot();
            }

            if (StartBaseLine.X > EndBaseLine.X)
            {
                return TextOrientation.Rotate180;
            }

            return TextOrientation.Horizontal;
        }

        if (Math.Abs(StartBaseLine.X - EndBaseLine.X) < 10e-5)
        {
            if (Math.Abs(StartBaseLine.Y - EndBaseLine.Y) < 10e-5)
            {
                // Start and End point are the same
                return GetTextOrientationRot();
            }

            if (StartBaseLine.Y > EndBaseLine.Y)
            {
                return TextOrientation.Rotate90;
            }

            return TextOrientation.Rotate270;
        }

        return TextOrientation.Other;
    }

    private TextOrientation GetTextOrientationRot()
    {
        double rotation = BoundingBox.Rotation;

        if (Math.Abs(rotation % 90) >= 10e-5)
        {
            return TextOrientation.Other;
        }

        int rotationInt = (int)Math.Round(rotation, MidpointRounding.AwayFromZero);
        switch (rotationInt)
        {
            case 0:
                return TextOrientation.Horizontal;

            case -90:
                return TextOrientation.Rotate90;

            case 180:
            case -180:
                return TextOrientation.Rotate180;

            case 90:
                return TextOrientation.Rotate270;
        }

        throw new Exception($"Could not find TextOrientation for rotation '{rotation}'.");
    }
}
