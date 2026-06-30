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
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Caly.Pdf.Models;

namespace Caly.Avalonia.Pdf.Text;

public partial class TextSelection
{
    public IEnumerable<PdfWord> GetSelectedWords(int pageNumber, PdfTextLayer pdfTextLayer)
    {
        System.Diagnostics.Debug.Assert(pdfTextLayer is not null);

        if (!IsPageInSelection(pageNumber))
        {
            yield break;
        }

        PdfWord? anchor = AnchorPageIndex == pageNumber ? AnchorWord : pdfTextLayer[GetFirstWordIndex()];
        PdfWord? focus = FocusPageIndex == pageNumber ? FocusWord : pdfTextLayer[GetLastWordIndex()];

        PdfWord? selectionStart = IsBackward ? focus : anchor;
        if (selectionStart is null)
        {
            yield break;
        }

        PdfWord? selectionEnd = IsBackward ? anchor : focus;
        foreach (var w in pdfTextLayer.GetWords(selectionStart, selectionEnd!))
        {
            yield return w;
        }
    }

    public IEnumerable<int> GetSelectedPagesIndexes()
    {
        if (!IsValid)
        {
            yield break;
        }

        int start = GetStartPageIndex();
        int end = GetEndPageIndex();

        System.Diagnostics.Debug.Assert(start <= end);

        if (start > end)
        {
            throw new ArgumentOutOfRangeException($"The selection's start page ({start}) is after the end page ({end}).");
        }

        for (int p = start; p <= end; ++p)
        {
            yield return p;
        }
    }

    /// <summary>
    /// Get the page selection.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="pageNumber">The page number. Starts at 1.</param>
    /// <param name="processFull">Handle fully selected words.</param>
    /// <param name="processPartial">Handle partially selected words.</param>
    /// <param name="pdfPageService">The pdf page service.</param>
    /// <param name="token"></param>
    private async IAsyncEnumerable<T> GetPageSelectionAsAsync<T>(int pageNumber, Func<PdfWord, T> processFull,
        Func<PdfWord, int, int, T> processPartial, Func<int, CancellationToken, Task<PdfTextLayer?>> getTextLayerAsync, [EnumeratorCancellation] CancellationToken token)
    {
        System.Diagnostics.Debug.Assert(IsValid);

#if DEBUG
        System.Diagnostics.Debug.Assert(pageNumber >= 1 && pageNumber <= NumberOfPages);
#endif

        var textLayer = await getTextLayerAsync(pageNumber, token);
        ArgumentNullException.ThrowIfNull(textLayer, nameof(textLayer));
        
        var selectedWords = GetSelectedWords(pageNumber, textLayer).ToArray();
        if (selectedWords.Length == 0)
        {
            yield break;
        }

        int i = 0;
        foreach (T w in GetPageSelectionAs(selectedWords, pageNumber, processFull, processPartial))
        {
            if (i % 100 == 0) // Check every 100 words
            {
                token.ThrowIfCancellationRequested();
            }

            yield return w;
            i++;
        }
    }

    public IEnumerable<T> GetPageSelectionAs<T>(IReadOnlyList<PdfWord> selectedWords, int pageNumber,
        Func<PdfWord, T> processFull, Func<PdfWord, int, int, T> processPartial)
    {
        System.Diagnostics.Debug.Assert(IsValid);

        if (selectedWords.Count == 0)
        {
            yield break;
        }

        int wordStartIndex;
        int wordEndIndex;

        if (IsBackward)
        {
            wordStartIndex = FocusPageIndex == pageNumber ? FocusOffset : -1;
            wordEndIndex = AnchorPageIndex == pageNumber ? AnchorOffset : -1;
        }
        else
        {
            wordStartIndex = AnchorPageIndex == pageNumber ? AnchorOffset : -1;
            wordEndIndex = FocusPageIndex == pageNumber ? FocusOffset : -1;
        }

        if (selectedWords.Count == 1)
        {
            // Single word selected
            var word = selectedWords[0];
            int lastIndex = word.Count - 1;
            if ((wordStartIndex == -1 || wordStartIndex == 0) &&
                (wordEndIndex == -1 || wordEndIndex == lastIndex))
            {
                yield return processFull(word);
            }
            else
            {
                yield return processPartial(word, wordStartIndex == -1 ? 0 : wordStartIndex,
                    wordEndIndex == -1 ? lastIndex : wordEndIndex);
            }

            yield break;
        }

        if (wordStartIndex == -1 && wordEndIndex == -1)
        {
            // Only whole words
            foreach (var word in selectedWords)
            {
                yield return processFull(word); // Return full words
            }
            yield break; // We are done
        }

        // Do first word
        var firstWord = selectedWords[0];
        if (wordStartIndex != -1 && wordStartIndex != 0)
        {
            int endIndex = firstWord.Count - 1;
            if (wordStartIndex > endIndex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR: wordStartIndex {wordStartIndex} is larger than {endIndex}.");
                wordStartIndex = endIndex;
            }

            yield return processPartial(firstWord, wordStartIndex, endIndex);
        }
        else
        {
            yield return processFull(firstWord);
        }

        // Do words in the middle
        for (int i = 1; i < selectedWords.Count - 1; ++i)
        {
            var word = selectedWords[i];
            yield return processFull(word);
        }

        // Do last word
        var lastWord = selectedWords[^1];
        if (wordEndIndex != -1 && wordEndIndex != lastWord.Count - 1)
        {
            yield return processPartial(lastWord, 0, wordEndIndex);
        }
        else
        {
            yield return processFull(lastWord);
        }
    }

    /// <summary>
    /// Get the document selection.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="processFull">Handle fully selected words.</param>
    /// <param name="processPartial">Handle partially selected words.</param>
    /// <param name="pdfPageService">The pdf page service.</param>
    /// <param name="token"></param>
    public async IAsyncEnumerable<T> GetDocumentSelectionAsAsync<T>(Func<PdfWord, T> processFull,
        Func<PdfWord, int, int, T> processPartial, Func<int, CancellationToken, Task<PdfTextLayer?>> getTextLayerAsync, [EnumeratorCancellation] CancellationToken token)
    {
        
        System.Diagnostics.Debug.Assert(IsValid);

        foreach (int p in GetSelectedPagesIndexes())
        {
            await foreach (T b in GetPageSelectionAsAsync(p, processFull, processPartial, getTextLayerAsync, token))
            {
                yield return b;
            }
        }
    }
}
