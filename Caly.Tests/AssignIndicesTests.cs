using Caly.Pdf;
using Caly.Pdf.Models;
using UglyToad.PdfPig.Core;

namespace Caly.Tests;

/// <summary>
/// <see cref="PdfTextLayerHelper.AssignIndices"/> numbers the words, lines and blocks of a page. Pages can
/// have more words than a <see cref="ushort"/> can count, e.g. 118,740 on page 2 of MOZILLA-7952-0.pdf.
/// </summary>
public sealed class AssignIndicesTests
{
    [Fact]
    public void IndicesDoNotWrapAboveUshortMaxValue()
    {
        const int wordsPerLine = 100;
        const int linesPerBlock = 400;
        const int blocksCount = 2;
        const int wordsCount = wordsPerLine * linesPerBlock * blocksCount; // 80,000

        var blocks = new List<PdfTextBlock>();
        for (int b = 0; b < blocksCount; b++)
        {
            var lines = new List<PdfTextLine>();
            for (int l = 0; l < linesPerBlock; l++)
            {
                double y = 1000 - (b * linesPerBlock + l) * 10;
                var words = new List<PdfWord>();
                for (int w = 0; w < wordsPerLine; w++)
                {
                    words.Add(new PdfWord([CreateLetter(w * 10, y)]));
                }

                lines.Add(new PdfTextLine(words));
            }

            blocks.Add(new PdfTextBlock(lines));
        }

        PdfTextLayerHelper.AssignIndices(blocks);

        var allWords = blocks.SelectMany(b => b.TextLines).SelectMany(l => l.Words).ToList();
        Assert.Equal(wordsCount, allWords.Count);
        Assert.Equal(Enumerable.Range(0, wordsCount), allWords.Select(w => w.IndexInPage));

        var allLines = blocks.SelectMany(b => b.TextLines).ToList();
        Assert.Equal(Enumerable.Range(0, allLines.Count), allLines.Select(l => l.IndexInPage));
        Assert.Equal(Enumerable.Range(0, allLines.Count).Select(i => i * wordsPerLine), allLines.Select(l => l.WordStartIndex));

        Assert.Equal(wordsCount / 2, blocks[1].WordStartIndex);
        Assert.Equal(wordsCount - 1, blocks[^1].WordEndIndex);
    }

    private static PdfLetter CreateLetter(double x, double y)
    {
        var bbox = new PdfRectangle(new PdfPoint(x, y + 7), new PdfPoint(x + 5, y + 7), new PdfPoint(x, y), new PdfPoint(x + 5, y));
        return new PdfLetter("a", bbox, 10f, 0);
    }
}
