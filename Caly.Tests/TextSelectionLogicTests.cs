using Caly.Core.Utilities;
using Caly.Pdf.Models;
using UglyToad.PdfPig.Core;

namespace Caly.Tests;

public class TextSelectionLogicTests
{
    /*
     * Fixture layout, in text-layer coordinates ((0, 0) is top left, y grows downward),
     * mirroring the index assignment done by PdfTextLayerHelper.GetTextLayer():
     *
     * Block 0:
     *   line 0 (y 0..10):  w0 [x 0..10]   w1 [x 20..30]
     *   line 1 (y 20..30): w2 [x 0..10]   w3 [x 20..30]   w4 [x 40..50]
     * Block 1:
     *   line 2 (y 50..60): w5 [x 0..10]   w6 [x 20..30]
     */

    private static readonly PdfWord W0 = Word(0, 10, 0, 10);
    private static readonly PdfWord W1 = Word(20, 30, 0, 10);
    private static readonly PdfWord W2 = Word(0, 10, 20, 30);
    private static readonly PdfWord W3 = Word(20, 30, 20, 30);
    private static readonly PdfWord W4 = Word(40, 50, 20, 30);
    private static readonly PdfWord W5 = Word(0, 10, 50, 60);
    private static readonly PdfWord W6 = Word(20, 30, 50, 60);

    private static readonly PdfTextLayer Layer = BuildLayer(
        new PdfTextBlock([new PdfTextLine([W0, W1]), new PdfTextLine([W2, W3, W4])]),
        new PdfTextBlock([new PdfTextLine([W5, W6])]));

    private static PdfWord Word(double xStart, double xEnd, double yTop, double yBottom)
    {
        // Bottom-left first: in inverse-y coordinates the (visual) bottom has the larger y.
        var letter = new PdfLetter("a", new PdfRectangle(xStart, yBottom, xEnd, yTop), 10f, 0);
        return new PdfWord([letter]);
    }

    /// <summary>
    /// Assigns the word/line/block indices exactly like PdfTextLayerHelper.GetTextLayer().
    /// </summary>
    private static PdfTextLayer BuildLayer(params PdfTextBlock[] blocks)
    {
        ushort wordIndex = 0;
        ushort lineIndex = 0;
        ushort blockIndex = 0;

        foreach (PdfTextBlock block in blocks)
        {
            ushort blockStartIndex = wordIndex;

            foreach (PdfTextLine line in block.TextLines)
            {
                ushort lineStartIndex = wordIndex;

                foreach (PdfWord word in line.Words)
                {
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
            block.WordEndIndex = (ushort)(wordIndex - 1);
        }

        return new PdfTextLayer(blocks, []);
    }

    #region TryGetMultipleClickSelection

    [Fact]
    public void DoubleClick_SelectsClickedWord()
    {
        bool handled = TextSelectionLogic.TryGetMultipleClickSelection(Layer, W3, 2, out var start, out var end);

        Assert.True(handled);
        Assert.Same(W3, start);
        Assert.Same(W3, end);
    }

    [Fact]
    public void TripleClick_SelectsWholeLine()
    {
        bool handled = TextSelectionLogic.TryGetMultipleClickSelection(Layer, W3, 3, out var start, out var end);

        Assert.True(handled);
        Assert.Same(W2, start);
        Assert.Same(W4, end);
    }

    [Fact]
    public void TripleClick_InSecondBlock_UsesBlockRelativeLineIndex()
    {
        // W6 is in line 2 (page index), which is line 0 of block 1 — exercises the
        // TextLineIndex - TextLines[0].IndexInPage offset arithmetic.
        bool handled = TextSelectionLogic.TryGetMultipleClickSelection(Layer, W6, 3, out var start, out var end);

        Assert.True(handled);
        Assert.Same(W5, start);
        Assert.Same(W6, end);
    }

    [Fact]
    public void QuadrupleClick_SelectsWholeBlock()
    {
        bool handled = TextSelectionLogic.TryGetMultipleClickSelection(Layer, W3, 4, out var start, out var end);

        Assert.True(handled);
        Assert.Same(W0, start);
        Assert.Same(W4, end);
    }

    [Fact]
    public void QuadrupleClick_InSecondBlock_SelectsThatBlockOnly()
    {
        bool handled = TextSelectionLogic.TryGetMultipleClickSelection(Layer, W5, 4, out var start, out var end);

        Assert.True(handled);
        Assert.Same(W5, start);
        Assert.Same(W6, end);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void OtherClickCounts_AreNotHandled(int clickCount)
    {
        bool handled = TextSelectionLogic.TryGetMultipleClickSelection(Layer, W3, clickCount, out _, out _);

        Assert.False(handled);
    }

    #endregion

    #region FindNearestWordWhileSelecting

    [Fact]
    public void NearestWord_EmptyLayer_ReturnsNull()
    {
        var empty = new PdfTextLayer([], []);

        Assert.Null(TextSelectionLogic.FindNearestWordWhileSelecting(5, 5, empty));
    }

    [Fact]
    public void NearestWord_PointBeforeStartOfEveryLine_ReturnsNull()
    {
        // x = -5 projects before the start of all three lines (s < 0).
        Assert.Null(TextSelectionLogic.FindNearestWordWhileSelecting(-5, 10, Layer));
    }

    [Fact]
    public void NearestWord_PointAfterEndOfLine_ReturnsLastWordOfThatLine()
    {
        // (35, 10) is just right of line 0's end: projection s > 1 on line 0 (distance
        // measured from its bottom-right corner), while line 1 is 20 below with a 4x
        // y-weight. Line 0 wins and the cursor is past its end, so its last word returns.
        var word = TextSelectionLogic.FindNearestWordWhileSelecting(35, 10, Layer);

        Assert.Same(W1, word);
    }

    [Fact]
    public void NearestWord_PointBetweenWords_ReturnsNearestWordInNearestLine()
    {
        // (25, 26) projects onto line 1 (baseline y = 30) at s = 0.5; W3's bottom-left
        // corner (20, 30) is the closest word corner.
        var word = TextSelectionLogic.FindNearestWordWhileSelecting(25, 26, Layer);

        Assert.Same(W3, word);
    }

    [Fact]
    public void NearestWord_PointBelowLastLine_ReturnsWordFromLastLine()
    {
        // (25, 70) is below every line; line 2 (baseline y = 60) is nearest, and W6's
        // bottom-left corner (20, 60) is closer than W5's bottom-right corner (10, 60).
        var word = TextSelectionLogic.FindNearestWordWhileSelecting(25, 70, Layer);

        Assert.Same(W6, word);
    }

    #endregion
}
