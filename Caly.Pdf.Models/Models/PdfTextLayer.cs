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

using System.Collections;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Geometry;

namespace Caly.Pdf.Models;

public sealed record PdfTextLayer : IReadOnlyList<PdfWord>
{
    internal static readonly PdfTextLayer Empty = new(Array.Empty<PdfTextBlock>(), Array.Empty<PdfAnnotation>());

    public PdfTextLayer(IReadOnlyList<PdfTextBlock> textBlocks, IReadOnlyList<PdfAnnotation> annotations)
    {
        Annotations = annotations;
        TextBlocks = textBlocks;
        if (textBlocks?.Count > 0)
        {
            Count = textBlocks.Sum(b => b.TextLines.Sum(l => l.Words.Count));
            System.Diagnostics.Debug.Assert(Count == textBlocks.SelectMany(b => b.TextLines.SelectMany(l => l.Words)).Count());
        }
    }

    /// <summary>
    /// The text lines contained in the block.
    /// </summary>
    public IReadOnlyList<PdfTextBlock> TextBlocks { get; }

    public IReadOnlyList<PdfAnnotation> Annotations { get; }

    public PdfAnnotation? FindAnnotationOver(double x, double y)
    {
        if (Annotations is null || Annotations.Count == 0) return null;

        var point = new PdfPoint(x, y);

        foreach (PdfAnnotation annotation in Annotations)
        {
            if (annotation.BoundingBox.Contains(point, true))
            {
                return annotation;
            }
        }

        return null;
    }

    public PdfWord? FindWordOver(double x, double y)
    {
        if (TextBlocks is null || TextBlocks.Count == 0) return null;

        foreach (PdfTextBlock block in TextBlocks)
        {
            if (!block.Contains(x, y))
            {
                continue;
            }

            PdfWord? candidate = block.FindWordOver(x, y);
            if (candidate is not null)
            {
                return candidate;
            }
        }

        return null;
    }

    public PdfTextLine? FindLineOver(double x, double y)
    {
        if (TextBlocks is null || TextBlocks.Count == 0) return null;

        foreach (PdfTextBlock block in TextBlocks)
        {
            if (!block.Contains(x, y))
            {
                continue;
            }

            PdfTextLine? candidate = block.FindTextLineOver(x, y);
            if (candidate is not null)
            {
                return candidate;
            }
        }

        return null;
    }

    public PdfTextLine? GetLine(PdfWord word)
    {
        var block = TextBlocks[word.TextBlockIndex];
        int lineStartIndex = block.TextLines[0].IndexInPage;
        return block.TextLines[word.TextLineIndex - lineStartIndex];
    }

    public IEnumerable<PdfWord> GetWords(PdfWord start, PdfWord end)
    {
        System.Diagnostics.Debug.Assert(start.IndexInPage <= end.IndexInPage);

        if (TextBlocks is null || TextBlocks.Count == 0)
        {
            yield break;
        }

        // Handle single word selected
        if (start == end)
        {
            yield return start;
            yield break;
        }

        // Handle whole page selected
        if (start.IndexInPage == 0 && end.IndexInPage == this.Count - 1)
        {
            foreach (PdfWord word in this)
            {
                yield return word;
            }

            yield break;
        }

        // Handle single block
        if (start.TextBlockIndex == end.TextBlockIndex)
        {
            PdfTextBlock block = TextBlocks[start.TextBlockIndex];

            int lineStartIndex = block.TextLines[0].IndexInPage;

            int startIndex = start.TextLineIndex - lineStartIndex;
            int endIndex = end.TextLineIndex - lineStartIndex;

            for (int l = startIndex; l <= endIndex; ++l)
            {
                PdfTextLine line = block.TextLines[l];
                if (l == startIndex)
                {
                    // we are in first line
                    int wordIndex = start.IndexInPage - line.WordStartIndex;

                    // Check if there is a single line selected
                    int lastIndex = start.TextLineIndex != end.TextLineIndex ? line.Words.Count : end.IndexInPage - line.WordStartIndex + 1;

                    for (int i = wordIndex; i < lastIndex; ++i)
                    {
                        yield return line.Words[i];
                    }
                }
                else if (l == endIndex)
                {
                    // we are in last line
                    int wordIndex = end.IndexInPage - line.WordStartIndex;
                    for (int i = 0; i <= wordIndex; ++i)
                    {
                        yield return line.Words[i];
                    }
                }
                else
                {
                    foreach (PdfWord word in line.Words)
                    {
                        yield return word;
                    }
                }
            }

            yield break;
        }

        for (int b = start.TextBlockIndex; b <= end.TextBlockIndex; ++b)
        {
            PdfTextBlock block = TextBlocks[b];

            int lineStartIndex = block.TextLines[0].IndexInPage;

            if (b == start.TextBlockIndex)
            {
                // we are in first block
                int startIndex = start.TextLineIndex - lineStartIndex;
                for (int l = startIndex; l < block.TextLines.Count; ++l)
                {
                    PdfTextLine line = block.TextLines[l];
                    if (l == startIndex)
                    {
                        // we are in first line (no need to check if there is a
                        // single line selected as it's not possible)
                        int wordIndex = start.IndexInPage - line.WordStartIndex;
                        for (int i = wordIndex; i < line.Words.Count; ++i)
                        {
                            yield return line.Words[i];
                        }
                    }
                    else
                    {
                        foreach (PdfWord word in line.Words)
                        {
                            yield return word;
                        }
                    }
                }
            }
            else if (b == end.TextBlockIndex)
            {
                // we are in last block
                int endIndex = end.TextLineIndex - lineStartIndex;
                for (int l = 0; l <= endIndex; ++l)
                {
                    var line = block.TextLines[l];
                    if (l == endIndex)
                    {
                        // we are in last line
                        int wordIndex = end.IndexInPage - line.WordStartIndex;
                        for (int i = 0; i <= wordIndex; ++i)
                        {
                            yield return line.Words[i];
                        }
                    }
                    else
                    {
                        foreach (PdfWord word in line.Words)
                        {
                            yield return word;
                        }
                    }
                }
            }
            else
            {
                // we are in a block in the middle
                foreach (PdfTextLine line in block.TextLines)
                {
                    foreach (PdfWord word in line.Words)
                    {
                        yield return word;
                    }
                }
            }
        }
    }

    public IEnumerator<PdfWord> GetEnumerator()
    {
        if (TextBlocks is null)
        {
            yield break;
        }

        foreach (PdfTextBlock block in TextBlocks)
        {
            foreach (PdfTextLine line in block.TextLines)
            {
                foreach (PdfWord word in line.Words)
                {
                    yield return word;
                }
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public int Count { get; }

    public PdfWord this[int index]
    {
        get
        {
            System.Diagnostics.Debug.Assert(Count > 0);
            System.Diagnostics.Debug.Assert(index >= 0 && index < Count);

            if (TextBlocks is null)
            {
                throw new NullReferenceException($"Cannot access word at index {index} because TextBlocks is null.");
            }

            foreach (PdfTextBlock block in TextBlocks)
            {
                if (block.ContainsWord(index))
                {
                    return block.GetWordInPageAt(index);
                }
            }

            throw new NullReferenceException($"Cannot find word at index {index}.");
        }
    }
}
