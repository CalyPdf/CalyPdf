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
    internal static readonly string SpaceInText = $"{WordSeparator}{WhiteSpaceProxy}{WordSeparator}";

    private string?[]? _index;

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
        _index = new string?[_pdfPageService.NumberOfPages];

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

            _index[p] = string.Join(WordSeparator, textLayer.Select(w =>
            {
                string text = w.Value;
                if (text.Contains(WordSeparator))
                {
                    text = text.Replace(WordSeparator, WhiteSpaceProxy);
                }

                if (text.Contains(' '))
                {
                    text = text.Replace(' ', WhiteSpaceProxy);
                }

                return text; //.Normalize(NormalizationForm.FormKD);
            }));
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

        if (text.Contains(' '))
        {
            text = text.Replace(' ', WhiteSpaceProxy);
        }

        return text; //.Normalize(NormalizationForm.FormKD);
    }

    private static ReadOnlySpan<char> GetSampleText(string pageText, int startIndex, int length)
    {
        int sampleStart = Math.Max(0, startIndex - 10);
        int sampleLength = Math.Min(length + 20, pageText.Length - sampleStart);
        return pageText.AsSpan(sampleStart, sampleLength);
    }

    public IEnumerable<TextSearchResult> Search(string text, IReadOnlyCollection<int> pagesToSkip, CancellationToken token)
    {
        Debug.ThrowOnUiThread();

        ArgumentNullException.ThrowIfNull(_index);
        System.Diagnostics.Debug.Assert(_index.Length > 0);

        token.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(text))
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

            string? pageText = _index[i];
            if (string.IsNullOrEmpty(pageText))
            {
                continue;
            }

            var pageResults = new HashSet<TextSearchResult>(); // Ensure results are unique

            /*
             * TODO - If the page text start with the word but the word starts with a space.
             * The search won't be pick up
             */

            foreach (TextSearchResult result in SearchPage(query, pageText, pageNumber, token))
            {
                pageResults.Add(result);
            }

            if (pageResults.Count > 0)
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
    /// A prepared search query: the cleaned query text, its match variants (with and
    /// without spaces expanded to <see cref="SpaceInText"/>) and the pre-built matcher.
    /// Built once per search via <see cref="PrepareQuery"/>, then applied to each page's
    /// index text by <see cref="SearchPage"/>.
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

        int indexAdj = text.StartsWith(WhiteSpaceProxy) ? 1 : 0;

        string[] searchValues;
        if (text.Contains(WhiteSpaceProxy))
        {
            searchValues = [text, text.Replace(WhiteSpaceProxy.ToString(), SpaceInText)];
        }
        else
        {
            searchValues = [text];
        }

        return new TextQuery()
        {
            Text = text,
            Values = searchValues,
            Matcher = SearchValues.Create(searchValues, StringComparison.OrdinalIgnoreCase),
            IndexAdjustment = indexAdj
        };
    }

    /// <summary>
    /// Finds every match of the query in a single page's index text. Pure logic with no
    /// UI dependency so it is unit-testable (same approach as TextSelectionLogic).
    /// </summary>
    internal static IEnumerable<TextSearchResult> SearchPage(TextQuery query, string pageText, int pageNumber, CancellationToken token)
    {
        int lastSpanIndex = 0;
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

            var wordIndex = pageText.AsSpan(0, highlightStart).Count(WordSeparator);

            // The number of page words the match spans can only be derived from the
            // matched text itself: the query's spaces match across word boundaries
            // (see SpaceInText), so a match may cover more words than the query's
            // punctuation-split token count. Separators bounding the span do not
            // introduce a following/preceding word, hence the trim.
            ReadOnlySpan<char> matched = pageText
                .AsSpan(highlightStart, matchStart + matchLength - highlightStart)
                .Trim(WordSeparator);
            int wordCount = matched.Count(WordSeparator) + 1;

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
