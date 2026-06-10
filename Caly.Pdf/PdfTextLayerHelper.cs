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

using System.Buffers;
using Caly.Pdf.Layout;
using Caly.Pdf.Models;
using Caly.Pdf.PageFactories;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace Caly.Pdf
{
    public static class PdfTextLayerHelper
    {
        public static PageTextLayerContent GetPageTextLayerContent(this PdfDocument document, int pageNumber, CancellationToken cancellationToken)
        {
            CancellationToken previous = TextLayerFactory.CurrentToken;
            TextLayerFactory.CurrentToken = cancellationToken;
            try
            {
                return document.GetPage<PageTextLayerContent>(pageNumber);
            }
            finally
            {
                TextLayerFactory.CurrentToken = previous;
            }
        }

        public static bool IsInteractive(PdfTextLine textLine)
        {
            var words = textLine.Words;

            if (words.Count == 1)
            {
                return PdfTextRegexHelper.UrlMatch().IsMatch(words[0].Value.AsSpan());
            }

            int length = words.Sum(w => w.Value.Length);

            char[]? pooled = null;
            try
            {
                Span<char> span = length <= 512 ?
                    stackalloc char[length] :
                    pooled = ArrayPool<char>.Shared.Rent(length);

                int i = 0;
                foreach (var w in words)
                {
                    w.Value.CopyTo(span.Slice(i));
                    i += w.Count;
                }

                return PdfTextRegexHelper.UrlMatch().IsMatch(span.Slice(0, length));
            }
            finally
            {
                if (pooled is not null)
                {
                    ArrayPool<char>.Shared.Return(pooled);
                }
            }
        }

        // https://source.dot.net/System.Text.RegularExpressions/System/Text/RegularExpressions/Regex.EnumerateMatches.cs.html#5d2974897ba7a25d

        public static ReadOnlySpan<char> GetInteractiveMatch(PdfTextLine textLine)
        {
            var words = textLine.Words;

            if (words.Count == 1)
            {
                var word = words[0];
                foreach (var match in PdfTextRegexHelper.UrlMatch().EnumerateMatches(word.Value.AsSpan()))
                {
                    return word.Value.AsSpan().Slice(match.Index, match.Length);
                }
            }

            int length = words.Sum(w => w.Count);

            char[]? pooled = null;
            try
            {
                Span<char> span = length <= 512 ?
                    stackalloc char[length] :
                    pooled = ArrayPool<char>.Shared.Rent(length);

                int i = 0;
                foreach (var w in words)
                {
                    w.Value.CopyTo(span.Slice(i));
                    i += w.Count;
                }

                foreach (var match in PdfTextRegexHelper.UrlMatch().EnumerateMatches(span.Slice(0, length)))
                {
                    Span<char> output = new char[match.Length];
                    span.Slice(match.Index, match.Length).CopyTo(output);
                    return output;
                }
            }
            finally
            {
                if (pooled is not null)
                {
                    ArrayPool<char>.Shared.Return(pooled);
                }
            }

            return [];
        }

        public static bool IsStroke(this TextRenderingMode textRenderingMode)
        {
            switch (textRenderingMode)
            {
                case TextRenderingMode.Stroke:
                case TextRenderingMode.StrokeClip:
                case TextRenderingMode.FillThenStroke:
                case TextRenderingMode.FillThenStrokeClip:
                    return true;

                case TextRenderingMode.Fill:
                case TextRenderingMode.FillClip:
                case TextRenderingMode.NeitherClip:
                case TextRenderingMode.Neither:
                    return false;

                default:
                    return false;
            }
        }

        public static bool IsFill(this TextRenderingMode textRenderingMode)
        {
            switch (textRenderingMode)
            {
                case TextRenderingMode.Fill:
                case TextRenderingMode.FillClip:
                case TextRenderingMode.FillThenStroke:
                case TextRenderingMode.FillThenStrokeClip:
                    return true;

                case TextRenderingMode.Stroke:
                case TextRenderingMode.StrokeClip:
                case TextRenderingMode.NeitherClip:
                case TextRenderingMode.Neither:
                    return false;

                default:
                    return false;
            }
        }

        public static PdfTextLayer GetTextLayer(PageTextLayerContent page, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            if (page.Letters.Count == 0)
            {
                if (page.Annotations.Count == 0)
                {
                    return PdfTextLayer.Empty;
                }

                return new PdfTextLayer([], page.Annotations);
            }

            var letters = CalyDuplicateOverlappingTextProcessor.GetInPlace(page.Letters, token);
            var words = CalyNNWordExtractor.Instance.GetWords(letters, token);
            var pdfBlocks = CalyDocstrum.Instance.GetBlocks(words, token);

            ushort wordIndex = 0;
            ushort lineIndex = 0;
            ushort blockIndex = 0;

            foreach (PdfTextBlock block in pdfBlocks)
            {
                ushort blockStartIndex = wordIndex;

                foreach (PdfTextLine line in block.TextLines)
                {
                    line.IsInteractive = IsInteractive(line);
                    if (line.IsInteractive)
                    {
                        var match = GetInteractiveMatch(line);
                        if (!match.IsEmpty)
                        {
                            line.InteractiveLink = new string(match);
                        }
                    }

                    ushort lineStartIndex = wordIndex;

                    foreach (PdfWord word in line.Words)
                    {
                        // throw if cancelled every now and then
                        if (wordIndex % 100 == 0)
                        {
                            token.ThrowIfCancellationRequested();
                        }

                        word.IndexInPage = wordIndex++;
                        word.TextLineIndex = lineIndex;
                        word.TextBlockIndex = blockIndex;
                    }

                    line.IndexInPage = lineIndex++;
                    line.TextBlockIndex = blockIndex;
                    line.WordStartIndex = lineStartIndex;
                }

                block.IndexInPage = blockIndex++;
                block.WordStartIndex = blockStartIndex;
                block.WordEndIndex = ushort.CreateChecked(wordIndex - 1);
            }

            return new PdfTextLayer(pdfBlocks, page.Annotations);
        }

        private static PdfPoint InverseYAxis(PdfPoint point, double height)
        {
            return new PdfPoint(point.X, height - point.Y);
        }

        private static PdfRectangle InverseYAxis(PdfRectangle rectangle, double height)
        {
            PdfPoint topLeft = InverseYAxis(rectangle.TopLeft, height);
            PdfPoint topRight = InverseYAxis(rectangle.TopRight, height);
            PdfPoint bottomLeft = InverseYAxis(rectangle.BottomLeft, height);
            PdfPoint bottomRight = InverseYAxis(rectangle.BottomRight, height);
            return new PdfRectangle(topLeft, topRight, bottomLeft, bottomRight);
        }

        internal static TextOrientation GetTextOrientation(IReadOnlyList<IPdfTextElement> letters)
        {
            if (letters.Count == 1)
            {
                return letters[0].TextOrientation;
            }

            TextOrientation tempTextOrientation = letters[0].TextOrientation;
            if (tempTextOrientation == TextOrientation.Other)
            {
                return tempTextOrientation;
            }

            foreach (IPdfTextElement letter in letters)
            {
                if (letter.TextOrientation != tempTextOrientation)
                {
                    return TextOrientation.Other;
                }
            }
            return tempTextOrientation;
        }

        /// <summary>
        /// Returns the axis-aligned rectangle that completely contains
        /// <paramref name="rectangle"/>, with no rotation, while preserving the
        /// input's Y orientation.
        /// </summary>
        /// <remarks>
        /// PdfPig's <c>Normalise</c> always emits a PDF-style Y-up rectangle
        /// (TopLeft has the larger Y). That mislabels the corners when the
        /// caller is already working in Y-down screen space — e.g. after
        /// <c>InverseYAxis</c> — leaving TopLeft below BottomLeft on screen.
        /// This version inspects the original TopLeft vs BottomLeft Y to keep
        /// the labels consistent with the input convention.
        /// </remarks>
        public static PdfRectangle NormaliseCaly(this PdfRectangle rectangle)
        {
            PdfPoint tl = rectangle.TopLeft;
            PdfPoint tr = rectangle.TopRight;
            PdfPoint bl = rectangle.BottomLeft;
            PdfPoint br = rectangle.BottomRight;

            double minX = Math.Min(Math.Min(tl.X, tr.X), Math.Min(bl.X, br.X));
            double maxX = Math.Max(Math.Max(tl.X, tr.X), Math.Max(bl.X, br.X));
            double minY = Math.Min(Math.Min(tl.Y, tr.Y), Math.Min(bl.Y, br.Y));
            double maxY = Math.Max(Math.Max(tl.Y, tr.Y), Math.Max(bl.Y, br.Y));

            // tl.Y == bl.Y happens for exactly-90° rotations and degenerate
            // zero-height rects; defaulting to Y-up matches PDF convention.
            bool yUp = tl.Y >= bl.Y;
            double topY = yUp ? maxY : minY;
            double bottomY = yUp ? minY : maxY;

            return new PdfRectangle(
                new PdfPoint(minX, topY),
                new PdfPoint(maxX, topY),
                new PdfPoint(minX, bottomY),
                new PdfPoint(maxX, bottomY));
        }

        /// <summary>
        /// Whether the rectangle is mirrored (reflected) rather than a pure rotation of an
        /// upright rectangle, i.e. "flipped on the X axis", with its top edge below its bottom edge.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Coordinates are assumed to be in the text layer's top-left origin (inverse-Y) space, where a
        /// correctly-oriented box has its <see cref="PdfRectangle.TopLeft"/> / <see cref="PdfRectangle.TopRight"/>
        /// visually <em>above</em> (smaller Y than) its <see cref="PdfRectangle.BottomLeft"/> /
        /// <see cref="PdfRectangle.BottomRight"/>.
        /// </para>
        /// </remarks>
        public static bool IsMirrored(this PdfRectangle rectangle)
        {
            double baseX = rectangle.BottomRight.X - rectangle.BottomLeft.X;
            double baseY = rectangle.BottomRight.Y - rectangle.BottomLeft.Y;
            double leftX = rectangle.TopLeft.X - rectangle.BottomLeft.X;
            double leftY = rectangle.TopLeft.Y - rectangle.BottomLeft.Y;

            return baseX * leftY - baseY * leftX > 0;
        }

        /// <summary>
        /// Returns an equivalent rectangle that is not <see cref="IsMirrored">mirrored</see>: it covers the
        /// exact same four corner points, but the top and bottom corners are swapped so the labels are
        /// upright again. If the rectangle is already upright it is returned unchanged.
        /// </summary>
        public static PdfRectangle UnMirror(this PdfRectangle rectangle)
        {
            if (!rectangle.IsMirrored())
            {
                return rectangle;
            }

            // Swap top <-> bottom corners (mirror back across the baseline).
            return new PdfRectangle(
                rectangle.BottomLeft,   // -> TopLeft
                rectangle.BottomRight,  // -> TopRight
                rectangle.TopLeft,      // -> BottomLeft
                rectangle.TopRight);    // -> BottomRight
        }
    }
}
