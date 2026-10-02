using Caly.Core.Services;
using Caly.Pdf;
using Caly.Pdf.Models;
using Caly.Pdf.PageFactories;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Tokens;

namespace Caly.Tests;

public class SearchValuesTextSearchServiceTests
{
    /*
     * Page words as the word extractor produces them when the PDF encodes spaces:
     * whitespace (and punctuation) are standalone words. Index words:
     *   0: "foo"   1: " "   2: "bar"   3: " "   4: "baz"
     */
    private static readonly string[] PageWords = ["foo", " ", "bar", " ", "baz"];

    /// <summary>
    /// Runs a search over <paramref name="words"/> (default <see cref="PageWords"/>) and
    /// returns the word-level results as (WordIndex, WordCount) pairs, i.e. the highlight
    /// ranges the document view model derives (Range(WordIndex, WordIndex + WordCount - 1),
    /// end inclusive). The page index is built exactly as BuildPdfDocumentIndex does.
    /// </summary>
    private static (int WordIndex, int WordCount)[] Search(string query, string[]? words = null)
    {
        words ??= PageWords;
        var page = SearchValuesTextSearchService.BuildPageIndex(words, words.Length);

        var textQuery = SearchValuesTextSearchService.PrepareQuery(query);

        return SearchValuesTextSearchService.SearchPage(textQuery, page, 1, CancellationToken.None)
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
    public void TrailingSpaceQuery_MatchesWholeWordEndOnly()
    {
        // A trailing space means "word ends here": "foo " covers "foo" only,
        // and "fo " matches nothing because "fo" is not the end of a word.
        Assert.Equal([(0, 1)], Search("foo "));
        Assert.Empty(Search("fo "));
    }

    [Fact]
    public void LeadingSpaceQuery_MatchesWholeWordStartOnly()
    {
        // A leading space means "word starts here": " bar" covers "bar" only,
        // and " ar" matches nothing because "ar" is not the start of a word.
        Assert.Equal([(2, 1)], Search(" bar"));
        Assert.Empty(Search(" ar"));
    }

    [Fact]
    public void LeadingAndTrailingSpaceQuery_MatchesFirstAndLastWordsOfPage()
    {
        Assert.Equal([(0, 1)], Search(" foo"));
        Assert.Equal([(4, 1)], Search("baz "));
    }

    [Fact]
    public void MultiWordQuery_WithoutWhitespaceWords_Matches()
    {
        // PDFs that encode no space glyphs (e.g. LaTeX output) yield adjacent words
        // with no whitespace word between them: a query space is a word boundary.
        string[] words = ["of", "Naive", "Bayes", "is"];
        Assert.Equal([(1, 2)], Search("naive bayes", words));
        Assert.Equal([(0, 4)], Search("of naive bayes is", words));
    }

    [Fact]
    public void MultiWordQuery_AcrossPunctuation_MatchesWithAndWithoutWhitespaceWords()
    {
        Assert.Equal([(0, 3)], Search("Bayes, the", ["Bayes", ",", "the"]));
        Assert.Equal([(0, 4)], Search("Bayes, the", ["Bayes", ",", " ", "the"]));
    }

    [Fact]
    public void MultipleSpacesInQuery_ActAsOneWordBoundary()
    {
        Assert.Equal([(0, 3)], Search("foo   bar"));
    }

    [Fact]
    public void WholeWordQuery_MatchesAdjacentOccurrences()
    {
        // Consecutive matches share the separator between them.
        Assert.Equal([(0, 1), (1, 1), (2, 1)], Search(" a ", ["a", "a", "a"]));
        Assert.Equal([(0, 1), (2, 1), (4, 1)], Search(" a ", ["a", " ", "a", " ", "a"]));
    }

    [Fact]
    public void RepeatedPunctuationQuery_Matches()
    {
        Assert.Equal([(0, 3)], Search("a..", ["a", ".", "."]));
        Assert.Equal([(1, 2)], Search("?!", ["x", "?", "!"]));
    }

    [Fact]
    public void SampleText_AtPageStartAndEnd_HasNoSpaceNextToEllipsis()
    {
        var page = SearchValuesTextSearchService.BuildPageIndex(PageWords, PageWords.Length)!;
        var query = SearchValuesTextSearchService.PrepareQuery("foo");

        var result = Assert.Single(SearchValuesTextSearchService.SearchPage(query, page, 1, CancellationToken.None));

        Assert.Equal("...foo bar baz...", result.ToString());
    }

    [Fact]
    public void QueryWithinWordContainingSpace_Matches()
    {
        // A single extracted word can itself contain a space.
        Assert.Equal([(1, 1)], Search("foo bar", ["x", "foo bar", "y"]));
    }

    [Fact]
    public void PartialWordsAroundSpace_Match()
    {
        // "oo ba" matches the end of "foo" through the start of "bar".
        Assert.Equal([(0, 3)], Search("oo ba"));
    }

    [Fact]
    public void ICML03_NaiveBayes_IsFound()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Documents", "ICML03-081.pdf");
        using var document = PdfDocument.Open(path, new ParsingOptions { UseActualText = true });

        // TextLayerFactory reads the PPI scale back out of this fake indirect object, the way
        // PdfPigDocumentService puts it there.
        document.Advanced.ReplaceIndirectObject(CalyPdfHelper.FakePpiReference, new NumericToken(1));
        document.AddPageFactory<PageTextLayerContent, TextLayerFactory>();

        var textLayer = PdfTextLayerHelper.GetTextLayer(
            document.GetPageTextLayerContent(1, CancellationToken.None), CancellationToken.None);
        var words = textLayer.Select(w => w.Value).ToArray();

        // Page 1 title: "Tackling the Poor Assumptions of Naive Bayes Text Classifiers".
        var results = Search("naive bayes", words);

        Assert.NotEmpty(results);
        foreach (var (wordIndex, wordCount) in results)
        {
            Assert.Equal("Naive", words[wordIndex]);
            Assert.Equal("Bayes", words[wordIndex + wordCount - 1]);
        }
    }
}
