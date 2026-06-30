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
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Input.Platform;
using Caly.Core.Models;
using Caly.Core.Services.Interfaces;
using Caly.Pdf.Models;

using Caly.Avalonia.Pdf.Text;

namespace Caly.Core.Services;

internal sealed class ClipboardService : IClipboardService
{
    // English rules
    private static readonly FrozenSet<UnicodeCategory> _noSpaceAfter = new HashSet<UnicodeCategory>
        {
            UnicodeCategory.OpenPunctuation,        // ( [ {
            UnicodeCategory.InitialQuotePunctuation,// “ ‘
            UnicodeCategory.DashPunctuation,        // -
            UnicodeCategory.ConnectorPunctuation,   // _
            UnicodeCategory.SpaceSeparator
        }.ToFrozenSet();

    private static readonly FrozenSet<UnicodeCategory> _noSpaceBefore = new HashSet<UnicodeCategory>
        {
            UnicodeCategory.ClosePunctuation,       // ) ] }
            UnicodeCategory.FinalQuotePunctuation,  // ” ’
            UnicodeCategory.OtherPunctuation,       // , . ! ?
            UnicodeCategory.DashPunctuation,        // -
            UnicodeCategory.MathSymbol,             // + = 
            UnicodeCategory.CurrencySymbol,         // $
            UnicodeCategory.SpaceSeparator
        }.ToFrozenSet();

    private readonly IClipboard _clipboard;

    public ClipboardService(IClipboard? clipboard)
    {
#if DEBUG
        if (global::Avalonia.Controls.Design.IsDesignMode)
        {
            _clipboard = clipboard!;
            return;
        }
#endif
        _clipboard = clipboard ?? throw new ArgumentNullException($"Could not find {typeof(IClipboard)}.");
    }

    public static bool ShouldDehyphenate(in UnicodeCategory prevCategory, in int previousLine, in int currentLine)
    {
        // This is a very basic dehyphenation method
        return currentLine > previousLine && prevCategory is UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation;
    }

    private static bool ShouldAddWhitespace(in UnicodeCategory prevCategory, in UnicodeCategory currentCategory)
    {
        return !_noSpaceAfter.Contains(prevCategory) &&
               !_noSpaceBefore.Contains(currentCategory);
    }

    public async Task<bool> SetAsync(TextSelection selection, PdfPageService pdfPageService, CancellationToken token)
    {
        // TODO - Check use of tasks here

        ArgumentNullException.ThrowIfNull(selection, nameof(selection));

        if (!selection.IsValid)
        {
            return false;
        }

        System.Diagnostics.Debug.WriteLine("Starting IClipboardService.SetAsync");

        string text = await Task.Run(async () =>
        {
            var request = selection.GetDocumentSelectionAsAsync(
                GetWord, GetPartialWord,
                pdfPageService.GetTextLayer, token);

            await using (var enumerator = request.GetAsyncEnumerator(token))
            {
                while (enumerator.Current.IsEmpty && await enumerator.MoveNextAsync())
                {
                    // Find first word that is not empty
                }

                if (enumerator.Current.IsEmpty)
                {
                    return string.Empty;
                }

                var sb = new StringBuilder().Append(enumerator.Current.Word);
                int previousLine = enumerator.Current.LineNumber;

                while (await enumerator.MoveNextAsync())
                {
                    var blob = enumerator.Current;

                    if (blob.Word.AsSpan().IsEmpty)
                    {
                        continue;
                    }

                    var prevLast = CharUnicodeInfo.GetUnicodeCategory(sb[^1]);

                    if (ShouldDehyphenate(in prevLast, in previousLine, blob.LineNumber))
                    {
                        sb.Length--; // Remove hyphen
                    }
                    else if (ShouldAddWhitespace(in prevLast, CharUnicodeInfo.GetUnicodeCategory(blob.Word.AsSpan()[0])))
                    {
                        sb.Append(' '); // TODO - Add condition to check if next word exist
                    }

                    sb.Append(blob.Word);
                    previousLine = blob.LineNumber;
                }

                if (sb.Length > 0 && char.IsWhiteSpace(sb[^1]))
                {
                    sb.Length--; // Last char added was a space
                }

                return sb.ToString();
            }
        }, token);

        await SetAsync(text);

        System.Diagnostics.Debug.WriteLine("Ended IClipboardService.SetAsync");

        return true;
    }

    public async Task SetAsync(string text)
    {
        await _clipboard.SetTextAsync(text);
    }

    public async Task ClearAsync()
    {
        await _clipboard.ClearAsync();
    }

    private readonly struct TextBlob
    {
        public TextBlob(string word, int lineNumber)
        {
            Word = word;
            LineNumber = lineNumber;
        }

        public string Word { get; }

        public int LineNumber { get; }

        public bool IsEmpty => Word.AsSpan().IsEmpty;
    }

    private static TextBlob GetWord(PdfWord word)
    {
        return new TextBlob(word.Value, word.TextLineIndex);
    }

    private static TextBlob GetPartialWord(PdfWord word, int startIndex, int endIndex)
    {
        System.Diagnostics.Debug.Assert(startIndex != -1);
        System.Diagnostics.Debug.Assert(endIndex != -1);
        System.Diagnostics.Debug.Assert(startIndex <= endIndex);

        endIndex = word.GetCharIndexFromBboxIndex(endIndex);

        var span = word.Value.AsSpan().Slice(startIndex, endIndex - startIndex + 1);

        return new TextBlob(span.ToString(), word.TextLineIndex);
    }
}
