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

    private static string CleanText(string text, out int count)
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

        var span = text.AsSpan();
        count = span.Count(WordSeparator) + 1;

        if (span.StartsWith(WordSeparator))
        {
            count--;
        }

        if (span.EndsWith(WordSeparator))
        {
            count--;
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
        text = CleanText(text, out int count);
        // END TODO

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
        
        var searchValue = SearchValues.Create(searchValues, StringComparison.OrdinalIgnoreCase);

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

            int lastSpanIndex = 0;

            var pageResults = new HashSet<TextSearchResult>(); // Ensure results are unique

            /*
             * TODO - If the page text start with the word but the word starts with a space.
             * The search won't be pick up
             */

            while (lastSpanIndex < pageText.Length)
            {
                token.ThrowIfCancellationRequested();

                int currentSpanIndex = pageText.AsSpan(lastSpanIndex).IndexOfAny(searchValue);
                if (currentSpanIndex == -1)
                {
                    break;
                }

                currentSpanIndex += indexAdj;

                lastSpanIndex += currentSpanIndex;

                var wordIndex = pageText.AsSpan(0, lastSpanIndex).Count(WordSeparator);
                
                int k = lastSpanIndex;
                pageResults.Add(new TextSearchResult()
                {
                    PageNumber = pageNumber,
                    ItemType = SearchResultItemType.Word,
                    WordIndex = wordIndex,
                    WordCount = count,
                    SampleText = () => GetSampleText(pageText, k, 20)
                });

                lastSpanIndex += text.Length;
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
}
