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

using Caly.Pdf;
using Caly.Pdf.Models;
using UglyToad.PdfPig.Core;

namespace Caly.Core.Utilities;

/// <summary>
/// Text-selection semantics operating purely on the text layer model. Deliberately
/// free of any Avalonia/UI dependency so the logic is unit-testable.
/// </summary>
internal static class TextSelectionLogic
{
    /// <summary>
    /// Resolves the word range selected by a multiple click over a word:
    /// 2 clicks select the word, 3 clicks the whole line, 4 clicks the whole
    /// block (paragraph).
    /// </summary>
    /// <param name="textLayer">The text layer the word belongs to.</param>
    /// <param name="word">The word under the pointer.</param>
    /// <param name="clickCount">The click count.</param>
    /// <param name="startWord">First word of the resolved selection.</param>
    /// <param name="endWord">Last word of the resolved selection.</param>
    /// <returns><c>false</c> for click counts that select nothing (1, or more than 4).</returns>
    internal static bool TryGetMultipleClickSelection(PdfTextLayer textLayer, PdfWord word, int clickCount,
        out PdfWord startWord, out PdfWord endWord)
    {
        switch (clickCount)
        {
            case 2:
                {
                    // Select whole word
                    startWord = word;
                    endWord = word;
                    return true;
                }
            case 3:
                {
                    // Select whole line
                    var block = textLayer.TextBlocks![word.TextBlockIndex];
                    var line = block.TextLines![word.TextLineIndex - block.TextLines[0].IndexInPage];

                    startWord = line.Words[0];
                    endWord = line.Words[^1];
                    return true;
                }
            case 4:
                {
                    // Select whole paragraph
                    var block = textLayer.TextBlocks![word.TextBlockIndex];

                    startWord = block.TextLines![0].Words![0];
                    endWord = block.TextLines![^1].Words![^1];
                    return true;
                }
            default:
                startWord = word;
                endWord = word;
                return false;
        }
    }

    /// <summary>
    /// Finds the word nearest to the point while a selection is already in progress and
    /// the pointer is not directly over a line. The point is projected on each line's
    /// baseline; lines whose projection falls before their start are ignored, and
    /// vertical distance is weighted heavier than horizontal so the line under or over
    /// the pointer wins against a horizontally closer one.
    /// </summary>
    /// <param name="x">X coordinate of the point.</param>
    /// <param name="y">Y coordinate of the point.</param>
    /// <param name="textLayer">The text layer.</param>
    /// <returns>The nearest word, or <c>null</c> if the layer is empty or the point is
    /// before the start of every line.</returns>
    internal static PdfWord? FindNearestWordWhileSelecting(double x, double y, PdfTextLayer textLayer)
    {
        if (textLayer.TextBlocks is null || textLayer.TextBlocks.Count == 0)
        {
            return null;
        }

        // Try finding the closest line as we are already selecting something

        // TODO - To finish, improve performance
        var point = new PdfPoint(x, y);

        double dist = double.MaxValue;
        double projectionOnLine = 0;
        PdfTextLine? l = null;

        foreach (var block in textLayer.TextBlocks)
        {
            foreach (var line in block.TextLines)
            {
                PdfPoint? projection = PdfPointExtensions.ProjectPointOnLine(in point,
                    line.BoundingBox.BottomLeft,
                    line.BoundingBox.BottomRight,
                    out double s);

                if (!projection.HasValue || s < 0)
                {
                    // If s < 0, the cursor is before the line (to the left), we ignore
                    continue;
                }

                // If s > 1, the cursor is after the line (to the right), we measure distance from bottom right corner
                PdfPoint referencePoint = s > 1 ? line.BoundingBox.BottomRight : projection.Value;

                double localDist = SquaredWeightedEuclidean(in point, in referencePoint, wY: 4); // Make y direction farther

                // TODO - Prevent selection line 'below' cursor

                if (localDist < dist)
                {
                    dist = localDist;
                    l = line;
                    projectionOnLine = s;
                }
            }
        }

        if (l is null)
        {
            return null;
        }

        if (projectionOnLine >= 1)
        {
            // Cursor after line, return last word
            return l.Words[^1];
        }

        // TODO - to improve, we already know where on the line is the point thanks to 'projectionOnLine'
        return l.FindNearestWord(x, y);

        static double SquaredWeightedEuclidean(in PdfPoint point1, in PdfPoint point2, double wX = 1.0, double wY = 1.0)
        {
            double dx = point1.X - point2.X;
            double dy = point1.Y - point2.Y;
            return wX * dx * dx + wY * dy * dy;
        }
    }
}
