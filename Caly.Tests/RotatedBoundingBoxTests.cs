using Caly.Pdf.Models;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace Caly.Tests;

/// <summary>
/// Regression tests for rotated word/line bounding boxes when constituent boxes have
/// non-uniform (or degenerate) heights — as produced by tight Type 3 glyph bounds, where
/// no-ink glyphs (spaces) have an almost-zero bounding box. The cross-axis thickness of a
/// Rotate90/Rotate270 box must bound every glyph, not collapse to the shortest one.
/// </summary>
public class RotatedBoundingBoxTests
{
    // Rotate90: baseline runs vertically at X = baseX, reading downward (Y decreasing).
    // The glyph rises toward smaller X (top edge at baseX - thickness).
    private static PdfLetter Rot90Letter(string v, double baseX, double yStart, double length, double thickness)
    {
        var bl = new PdfPoint(baseX, yStart);
        var br = new PdfPoint(baseX, yStart - length);
        var tl = new PdfPoint(baseX - thickness, yStart);
        var tr = new PdfPoint(baseX - thickness, yStart - length);
        return new PdfLetter(v, new PdfRectangle(tl, tr, bl, br), 10f, 0);
    }

    // Rotate270: baseline runs vertically at X = baseX, reading upward (Y increasing).
    // The glyph rises toward larger X (top edge at baseX + thickness).
    private static PdfLetter Rot270Letter(string v, double baseX, double yStart, double length, double thickness)
    {
        var bl = new PdfPoint(baseX, yStart);
        var br = new PdfPoint(baseX, yStart + length);
        var tl = new PdfPoint(baseX + thickness, yStart);
        var tr = new PdfPoint(baseX + thickness, yStart + length);
        return new PdfLetter(v, new PdfRectangle(tl, tr, bl, br), 10f, 0);
    }

    [Fact]
    public void Rotate90_Line_ThicknessNotCollapsedByDegenerateSpaceWord()
    {
        var word = new PdfWord([Rot90Letter("A", 100, 200, 8, 6.0)]);
        var space = new PdfWord([Rot90Letter(" ", 100, 190, 0.1, 0.1)]); // no-ink glyph
        Assert.Equal(TextOrientation.Rotate90, word.TextOrientation);
        Assert.Equal(TextOrientation.Rotate90, space.TextOrientation);

        var line = new PdfTextLine([word, space]);

        Assert.Equal(TextOrientation.Rotate90, line.TextOrientation);
        // The line must be as thick as the real glyph (~6), not the degenerate space (~0.1).
        Assert.True(line.BoundingBox.Height >= 5.5,
            $"Rotate90 line collapsed: Height={line.BoundingBox.Height:0.00}");
    }

    [Fact]
    public void Rotate270_Line_ThicknessNotCollapsedByDegenerateSpaceWord()
    {
        var word = new PdfWord([Rot270Letter("A", 100, 200, 8, 6.0)]);
        var space = new PdfWord([Rot270Letter(" ", 100, 210, 0.1, 0.1)]);
        Assert.Equal(TextOrientation.Rotate270, word.TextOrientation);
        Assert.Equal(TextOrientation.Rotate270, space.TextOrientation);

        var line = new PdfTextLine([word, space]);

        Assert.Equal(TextOrientation.Rotate270, line.TextOrientation);
        Assert.True(line.BoundingBox.Height >= 5.5,
            $"Rotate270 line collapsed: Height={line.BoundingBox.Height:0.00}");
    }

    [Fact]
    public void Rotate90_Word_ThicknessBoundsTallestLetter()
    {
        // Letters of differing tight heights, like real Type 3 glyphs.
        var word = new PdfWord([
            Rot90Letter("A", 100, 200, 4, 6.8),
            Rot90Letter(" ", 100, 196, 0.1, 0.1),
            Rot90Letter("B", 100, 195, 4, 5.9),
        ]);

        Assert.Equal(TextOrientation.Rotate90, word.TextOrientation);
        // Must bound the tallest letter (6.8), not collapse to the space (0.1).
        Assert.True(word.BoundingBox.Height >= 6.5,
            $"Rotate90 word too thin: Height={word.BoundingBox.Height:0.00}");
    }

    [Fact]
    public void Rotate90_Line_ReadingLengthAndRotationPreserved()
    {
        var word = new PdfWord([Rot90Letter("A", 100, 200, 8, 6.0)]);
        var space = new PdfWord([Rot90Letter(" ", 100, 190, 0.1, 0.1)]);
        var line = new PdfTextLine([word, space]);

        // Reading direction spans Y from 200 (start) down to ~189.9 (end) => ~10.1
        Assert.Equal(10.1, line.BoundingBox.Width, 1);
        Assert.Equal(-90.0, line.BoundingBox.Rotation, 1);
        // BottomLeft is the baseline start (largest X, largest Y).
        Assert.Equal(100.0, line.BoundingBox.BottomLeft.X, 1);
        Assert.Equal(200.0, line.BoundingBox.BottomLeft.Y, 1);
    }
}
