using Caly.Pdf.Models;
using UglyToad.PdfPig.Core;

namespace Caly.Tests;

public sealed class PdfWordTests
{
    /// <summary>
    /// A word can have more letters, and more characters, than a <see cref="ushort"/> can count,
    /// e.g. a long run of text without any space.
    /// </summary>
    [Fact]
    public void LongWordDoesNotWrapAboveUshortMaxValue()
    {
        const int lettersCount = 70_000;

        // Two characters per letter, as with ligatures, so that the letters are mapped to characters
        var letters = new List<PdfLetter>(lettersCount);
        for (int i = 0; i < lettersCount; i++)
        {
            var bbox = new PdfRectangle(new PdfPoint(i, 7), new PdfPoint(i + 1, 7), new PdfPoint(i, 0), new PdfPoint(i + 1, 0));
            letters.Add(new PdfLetter(i % 2 == 0 ? "fi" : "fl", bbox, 10f, 0));
        }

        var word = new PdfWord(letters);

        Assert.Equal(lettersCount, word.Count);
        Assert.Equal(2 * lettersCount, word.Value.Length);
        Assert.Equal("fl", word.Value[^2..]);

        // The last character of each letter
        Assert.Equal(1, word.GetCharIndexFromBboxIndex(0));
        Assert.Equal(2 * 40_000 + 1, word.GetCharIndexFromBboxIndex(40_000));
        Assert.Equal(2 * lettersCount - 1, word.GetCharIndexFromBboxIndex(lettersCount - 1));
    }
}
