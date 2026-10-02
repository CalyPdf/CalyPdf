using Caly.Core.Models;
using Caly.Core.Services.Interfaces;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Caly.Core.Services;

internal sealed class SearchValuesTextSearchService : ITextSearchService
{
    internal const char WordSeparator = '\u2060';
    internal const char WhiteSpaceProxy = '\u00A0';
    internal const char WhiteSpace = ' ';

    private PageIndex?[]? _index;

    public void Dispose()
    {
        if (_index is null)
        {
            return;
        }

        for (int i = 0; i < _index.Length; ++i)
        {
            _index[i] = null;
        }
    }

    private readonly PdfPageService _pdfPageService;

    public SearchValuesTextSearchService(PdfPageService pdfPageService)
    {
        _pdfPageService = pdfPageService;
    }

    public async Task BuildPdfDocumentIndex(IProgress<int> progress, CancellationToken token)
    {
        System.Diagnostics.Debug.Assert(_pdfPageService.NumberOfPages > 0);
        _index = new PageIndex?[_pdfPageService.NumberOfPages];

        int done = 0;

        var options = new ParallelOptions()
        {
            MaxDegreeOfParallelism = 4,
            CancellationToken = token
        };

        await Parallel.ForAsync(0, _pdfPageService.NumberOfPages, options, async (p, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            var textLayer = await _pdfPageService.GetTextLayer(p + 1, ct)
                .ConfigureAwait(false);

            if (textLayer is null)
            {
                ct.ThrowIfCancellationRequested();
                throw new NullReferenceException("Cannot index search on a null PdfTextLayer.");
            }

            _index[p] = BuildPageIndex(textLayer.Select(w => w.Value), textLayer.Count);
            progress.Report(Interlocked.Add(ref done, 1));
        });
    }

    private static string CleanText(string text)
    {
        bool hasPunctuation = false;
        for (int i = 0; i < text.Length; ++i)
        {
            if (char.IsPunctuation(text[i]))
            {
                hasPunctuation = true;
                break;
            }
        }

        if (hasPunctuation)
        {
            var sb = new StringBuilder(text.Length + text.Length / 2);

            for (int i = 0; i < text.Length; ++i)
            {
                if (char.IsPunctuation(text[i]))
                {
                    if (i != 0)
                    {
                        sb.Append(WordSeparator);
                    }

                    sb.Append(text[i]);

                    if (i < text.Length - 1)
                    {
                        sb.Append(WordSeparator);
                    }
                }
                else
                {
                    sb.Append(text[i]);
                }
            }

            text = sb.ToString();
        }

        if (text.Contains(WhiteSpace))
        {
            text = text.Replace(WhiteSpace, WhiteSpaceProxy);
        }

        return text; //.Normalize(NormalizationForm.FormKD);
    }

    /// <summary>
    /// A page's searchable text: the page words joined by <see cref="WordSeparator"/>,
    /// with a separator at both ends so every word is bounded by one. Whitespace-only
    /// words are left out, because PDFs disagree on whether they exist at all (LaTeX
    /// output, for instance, encodes no space glyphs, so its words are adjacent) and a
    /// query space is matched as a word boundary either way.
    /// </summary>
    internal sealed class PageIndex
    {
        public required string Text { get; init; }

        /// <summary>
        /// Text-layer word index of each word in <see cref="Text"/>, in order.
        /// </summary>
        public required int[] WordIndices { get; init; }
    }

    /// <summary>
    /// Builds the <see cref="PageIndex"/> of a page from its text-layer word values, in
    /// text-layer order. Returns <c>null</c> if the page has no searchable word.
    /// </summary>
    /// <param name="words">The text-layer word values.</param>
    /// <param name="wordCount">Number of items in <paramref name="words"/>.</param>
    internal static PageIndex? BuildPageIndex(IEnumerable<string> words, int wordCount)
    {
        // Rough capacity: ~5 chars per word plus its separator.
        var sb = new StringBuilder(wordCount * 6 + 1);
        var wordIndices = new int[wordCount];
        int indexed = 0;

        sb.Append(WordSeparator);

        int i = 0;
        foreach (string word in words)
        {
            if (!string.IsNullOrWhiteSpace(word))
            {
                int start = sb.Length;
                sb.Append(word);

                if (word.AsSpan().ContainsAny(WordSeparator, WhiteSpace))
                {
                    // Replace in place rather than allocating a new string per word.
                    sb.Replace(WordSeparator, WhiteSpaceProxy, start, word.Length);
                    sb.Replace(WhiteSpace, WhiteSpaceProxy, start, word.Length);
                }

                sb.Append(WordSeparator); //.Normalize(NormalizationForm.FormKD);
                wordIndices[indexed++] = i;
            }

            ++i;
        }

        System.Diagnostics.Debug.Assert(i == wordCount);

        if (indexed == 0)
        {
            return null;
        }

        if (indexed < wordIndices.Length)
        {
            Array.Resize(ref wordIndices, indexed);
        }

        return new PageIndex()
        {
            Text = sb.ToString(),
            WordIndices = wordIndices
        };
    }

    private static ReadOnlySpan<char> GetSampleText(string pageText, int startIndex, int length)
    {
        int sampleStart = Math.Max(0, startIndex - 10);
        int sampleLength = Math.Min(length + 20, pageText.Length - sampleStart);
        return pageText.AsSpan(sampleStart, sampleLength).Trim(WordSeparator);
    }

    public IEnumerable<TextSearchResult> Search(string text, IReadOnlyCollection<int> pagesToSkip, CancellationToken token)
    {
        Debug.ThrowOnUiThread();

        ArgumentNullException.ThrowIfNull(_index);
        System.Diagnostics.Debug.Assert(_index.Length > 0);

        token.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        // TODO - Move the below out of here as it reruns while indexing
        TextQuery query = PrepareQuery(text);
        // END TODO

        for (int i = 0; i < _index.Length; ++i)
        {
            token.ThrowIfCancellationRequested();
            int pageNumber = i + 1;
            if (pagesToSkip.Contains(pageNumber))
            {
                continue;
            }

            PageIndex? page = _index[i];
            if (page is null)
            {
                continue;
            }

            HashSet<TextSearchResult>? pageResults = null; // Ensure results are unique

            foreach (TextSearchResult result in SearchPage(query, page, pageNumber, token))
            {
                pageResults ??= new HashSet<TextSearchResult>();
                pageResults.Add(result);
            }

            if (pageResults is not null)
            {
                yield return new TextSearchResult()
                {
                    ItemType = SearchResultItemType.Unspecified,
                    PageNumber = pageNumber,
                    Nodes = pageResults
                };
            }
        }
    }

    /// <summary>
    /// A prepared search query: the cleaned query text, its match variants and the
    /// pre-built matcher. Built once per search via <see cref="PrepareQuery"/>, then
    /// applied to each page's <see cref="PageIndex"/> by <see cref="SearchPage"/>.
    /// </summary>
    internal sealed class TextQuery
    {
        public required string Text { get; init; }

        public required string[] Values { get; init; }

        public required SearchValues<string> Matcher { get; init; }

        public required int IndexAdjustment { get; init; }
    }

    internal static TextQuery PrepareQuery(string text)
    {
        text = CleanText(text);

        // Always collapsed, even without spaces: repeated punctuation ("a..", "?!")
        // gets two separators in a row from CleanText, which an index never has.
        string boundaries = ToWordBoundaries(text);

        string[] searchValues;
        if (text.Contains(WhiteSpaceProxy))
        {
            // Spaces are word boundaries (see PageIndex), unless the matched page word
            // itself contains a space: keep the literal text as a second variant.
            searchValues = [boundaries, text];
        }
        else
        {
            searchValues = [boundaries];
        }

        return new TextQuery()
        {
            Text = text,
            Values = searchValues,
            Matcher = SearchValues.Create(searchValues, StringComparison.OrdinalIgnoreCase),
            // A leading space matches the separator before the word, which is not highlighted.
            IndexAdjustment = text.StartsWith(WhiteSpaceProxy) ? 1 : 0
        };
    }

    /// <summary>
    /// Replaces each run of spaces and/or separators in the cleaned query (the ones
    /// <see cref="CleanText"/> put around punctuation) by a single
    /// <see cref="WordSeparator"/>: a page index never has two separators in a row.
    /// </summary>
    private static string ToWordBoundaries(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c == WhiteSpaceProxy || c == WordSeparator)
            {
                if (sb.Length == 0 || sb[^1] != WordSeparator)
                {
                    sb.Append(WordSeparator);
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Finds every match of the query in a single page's index. Pure logic with no
    /// UI dependency so it is unit-testable (same approach as TextSelectionLogic).
    /// </summary>
    internal static IEnumerable<TextSearchResult> SearchPage(TextQuery query, PageIndex page, int pageNumber, CancellationToken token)
    {
        string pageText = page.Text;
        int lastSpanIndex = 0;

        // Separators counted so far in pageText[0..countedUpTo). Matches come in
        // increasing order, so each one only counts from the previous highlight start.
        int countedUpTo = 0;
        int separatorCount = 0;
        while (lastSpanIndex < pageText.Length)
        {
            token.ThrowIfCancellationRequested();

            int currentSpanIndex = pageText.AsSpan(lastSpanIndex).IndexOfAny(query.Matcher);
            if (currentSpanIndex == -1)
            {
                yield break;
            }

            int matchStart = lastSpanIndex + currentSpanIndex;
            int matchLength = GetMatchLength(pageText.AsSpan(matchStart), query.Values);
            int highlightStart = matchStart + query.IndexAdjustment;

            // Position in the index of the word the match starts in: the page text
            // starts with a separator, so count the ones before the match, minus one.
            separatorCount += pageText.AsSpan(countedUpTo, highlightStart - countedUpTo).Count(WordSeparator);
            countedUpTo = highlightStart;
            int firstWord = separatorCount - 1;

            // The number of index words the match spans can only be derived from the
            // matched text itself: the query's spaces match word boundaries, so a match
            // may cover more words than the query's punctuation-split token count.
            // Separators bounding the span do not introduce a following/preceding
            // word, hence the trim.
            ReadOnlySpan<char> matched = pageText
                .AsSpan(highlightStart, matchStart + matchLength - highlightStart)
                .Trim(WordSeparator);
            int lastWord = firstWord + matched.Count(WordSeparator);

            // Map back to text-layer words, so the range also covers any whitespace
            // words left out of the index.
            int wordIndex = page.WordIndices[firstWord];
            int wordCount = page.WordIndices[lastWord] - wordIndex + 1;

            int k = highlightStart;
            yield return new TextSearchResult()
            {
                PageNumber = pageNumber,
                ItemType = SearchResultItemType.Word,
                WordIndex = wordIndex,
                WordCount = wordCount,
                SampleText = () => GetSampleText(pageText, k, 20)
            };

            lastSpanIndex = matchStart + matchLength;

            // A trailing query space matched the separator after the word. Resume on it,
            // so a leading query space can match it again for the next word (" a " must
            // find every "a" in "a a a"). The match length check guarantees progress.
            if (matchLength > 1 && pageText[lastSpanIndex - 1] == WordSeparator)
            {
                --lastSpanIndex;
            }
        }
    }

    /// <summary>
    /// Length of the query variant that matched at the given position. The matcher
    /// found one of <paramref name="searchValues"/> here, so at least one comparison
    /// succeeds; when both variants match (no spaces expanded) they are identical.
    /// </summary>
    private static int GetMatchLength(ReadOnlySpan<char> matchText, string[] searchValues)
    {
        int length = 0;
        foreach (string value in searchValues)
        {
            if (value.Length > length && matchText.StartsWith(value, StringComparison.OrdinalIgnoreCase))
            {
                length = value.Length;
            }
        }

        System.Diagnostics.Debug.Assert(length > 0);
        return length;
    }
}
