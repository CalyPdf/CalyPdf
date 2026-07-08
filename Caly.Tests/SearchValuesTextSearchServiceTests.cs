using Caly.Core.Services;

namespace Caly.Tests;

public class SearchValuesTextSearchServiceTests
{
    private const char Sep = SearchValuesTextSearchService.WordSeparator;
    private const char Proxy = SearchValuesTextSearchService.WhiteSpaceProxy;

    /*
     * Page words as the word extractor produces them: whitespace (and punctuation)
     * are standalone words. Index words:
     *   0: "foo"   1: " "   2: "bar"   3: " "   4: "baz"
     */
    private static readonly string[] PageWords = ["foo", " ", "bar", " ", "baz"];

    /// <summary>
    /// Runs a search over <see cref="PageWords"/> and returns the word-level results
    /// as (WordIndex, WordCount) pairs, i.e. the highlight ranges the document view
    /// model derives (Range(WordIndex, WordIndex + WordCount - 1), end inclusive).
    /// The page text mirrors BuildPdfDocumentIndex's format: word values joined by
    /// <see cref="Sep"/>, spaces inside a word replaced by <see cref="Proxy"/>.
    /// </summary>
    private static (int WordIndex, int WordCount)[] Search(string query)
    {
        string pageText = string.Join(Sep, PageWords.Select(w => w.Replace(' ', Proxy)));

        var textQuery = SearchValuesTextSearchService.PrepareQuery(query);

        return SearchValuesTextSearchService.SearchPage(textQuery, pageText, 1, CancellationToken.None)
            .Where(n => n.WordIndex.HasValue && n.WordCount.HasValue)
            .Select(n => (n.WordIndex!.Value, n.WordCount!.Value))
            .OrderBy(x => x.Item1)
            .ToArray();
    }

    [Fact]
    public void SingleWordQuery_CoversThatWordOnly()
    {
        Assert.Equal([(2, 1)], Search("bar"));
    }

    [Fact]
    public void MultiWordQuery_CoversAllSpannedWords()
    {
        // "foo bar" matches page words 0..2 ("foo", " ", "bar"),
        // so the highlight range must cover all three.
        Assert.Equal([(0, 3)], Search("foo bar"));
    }

    [Fact]
    public void MultiWordQuery_InMiddleOfPage_CoversAllSpannedWords()
    {
        // "bar baz" matches page words 2..4 ("bar", " ", "baz").
        Assert.Equal([(2, 3)], Search("bar baz"));
    }

    [Fact]
    public void TrailingSpaceQuery_DoesNotCoverTheFollowingWord()
    {
        // "foo " matches "foo" plus the whitespace word, not "bar".
        Assert.Equal([(0, 2)], Search("foo "));
    }

    [Fact]
    public void LeadingSpaceQuery_StartsAtTheWhitespaceWord()
    {
        // " bar" matches the whitespace word plus "bar".
        Assert.Equal([(1, 2)], Search(" bar"));
    }
}
