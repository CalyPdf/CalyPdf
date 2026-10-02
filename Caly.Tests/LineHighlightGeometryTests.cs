using Caly.Core.Utilities;
using SkiaSharp;
using UglyToad.PdfPig.Core;

namespace Caly.Tests;

/// <summary>
/// Highlights (selection, search results) are drawn as one geometry per text line: the word
/// rectangles plus quads joining consecutive words, filled NonZero. Avalonia's headless geometry
/// ignores fill rules, so coverage is checked against a Skia path with the same (Winding) fill.
/// </summary>
public class LineHighlightGeometryTests
{
    // Y axis points down (0, 0 is top left): the top edge has the smaller Y.
    private static PdfRectangle Horizontal(double left, double right, double baseline = 20, double top = 10)
    {
        return new PdfRectangle(new PdfPoint(left, top), new PdfPoint(right, top),
            new PdfPoint(left, baseline), new PdfPoint(right, baseline));
    }

    // Upside-down text reading right-to-left: text-relative corners are mirrored in page space.
    private static PdfRectangle Rotate180(double right, double left, double baseline = 10, double top = 20)
    {
        return new PdfRectangle(new PdfPoint(right, top), new PdfPoint(left, top),
            new PdfPoint(right, baseline), new PdfPoint(left, baseline));
    }

    // A word whose baseline starts at (x, y) and runs along angle (radians), with the given length and height.
    private static PdfRectangle Oriented(double x, double y, double angle, double length, double height)
    {
        double dx = Math.Cos(angle), dy = Math.Sin(angle);
        // Perpendicular towards the top of the glyphs (Y down => rotate the direction by -90°).
        double ux = dy, uy = -dx;

        var bl = new PdfPoint(x, y);
        var br = new PdfPoint(x + dx * length, y + dy * length);
        var tl = new PdfPoint(x + ux * height, y + uy * height);
        var tr = new PdfPoint(br.X + ux * height, br.Y + uy * height);
        return new PdfRectangle(tl, tr, bl, br);
    }

    private static List<PdfWordHelpers.Quad> Quads(params PdfRectangle[] rects)
    {
        var quads = new List<PdfWordHelpers.Quad>();
        PdfWordHelpers.GetLineQuads(rects, quads);
        return quads;
    }

    private static SKPath ToWindingPath(IEnumerable<PdfWordHelpers.Quad> quads)
    {
        var path = new SKPath { FillType = SKPathFillType.Winding };
        foreach (var q in quads)
        {
            path.MoveTo((float)q.A.X, (float)q.A.Y);
            path.LineTo((float)q.B.X, (float)q.B.Y);
            path.LineTo((float)q.C.X, (float)q.C.Y);
            path.LineTo((float)q.D.X, (float)q.D.Y);
            path.Close();
        }

        return path;
    }

    [Fact]
    public void GroupByLine_StartsNewGroupWhenLineIndexChanges()
    {
        var r = Horizontal(0, 1);
        var lines = new List<PdfRectangle[]>();

        PdfWordHelpers.GroupByLine(
            [((ushort)0, r), ((ushort)0, r), ((ushort)1, r), ((ushort)1, r), ((ushort)1, r), ((ushort)0, r)],
            lines);

        Assert.Equal([2, 3, 1], lines.Select(l => l.Length));
    }

    [Fact]
    public void GroupByLine_AppendsToExistingGroups()
    {
        var r = Horizontal(0, 1);
        var lines = new List<PdfRectangle[]>();

        // Two search results on the same line must stay two groups.
        PdfWordHelpers.GroupByLine([((ushort)3, r)], lines);
        PdfWordHelpers.GroupByLine([((ushort)3, r)], lines);

        Assert.Equal(2, lines.Count);
    }

    [Fact]
    public void Horizontal_GapBetweenWords_IsFilled()
    {
        var quads = Quads(Horizontal(0, 10), Horizontal(14, 30));

        Assert.Equal(3, quads.Count); // 2 words + 1 join

        using var path = ToWindingPath(quads);
        Assert.True(path.Contains(12, 15));  // in the gap
        Assert.False(path.Contains(12, 5));  // above the line
        Assert.False(path.Contains(35, 15)); // after the last word
    }

    [Fact]
    public void OverlappingWords_AreNotJoined()
    {
        var quads = Quads(Horizontal(0, 10), Horizontal(8, 20));

        Assert.Equal(2, quads.Count);
    }

    // Mirrored text (negative horizontal scale): upright, but reading right-to-left, so the
    // text-relative corners have the opposite handedness to normal text.
    private static PdfRectangle Mirrored(double right, double left, double baseline = 20, double top = 10)
    {
        return new PdfRectangle(new PdfPoint(right, top), new PdfPoint(left, top),
            new PdfPoint(right, baseline), new PdfPoint(left, baseline));
    }

    [Fact]
    public void AllQuads_HaveNonNegativeWinding_WhateverTheWordHandedness()
    {
        // Mixed handedness overlapping: without normalisation, NonZero would cancel the overlap out.
        var quads = Quads(Horizontal(0, 10), Mirrored(20, 5));

        Assert.All(quads, q => Assert.True(PdfWordHelpers.SignedArea(q) >= 0));

        using var path = ToWindingPath(quads);
        Assert.True(path.Contains(7, 15)); // in the overlap
    }

    [Fact]
    public void Rotate180_GapBetweenWords_IsFilled()
    {
        // Reading right-to-left: first word spans x 100 -> 90, second 86 -> 70.
        var quads = Quads(Rotate180(100, 90), Rotate180(86, 70));

        Assert.Equal(3, quads.Count);

        using var path = ToWindingPath(quads);
        Assert.True(path.Contains(88, 15));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(90)]
    [InlineData(-135)]
    public void RotatedLine_GapBetweenWords_IsFilled(double angleDeg)
    {
        double angle = angleDeg * Math.PI / 180;
        double dx = Math.Cos(angle), dy = Math.Sin(angle);

        var first = Oriented(100, 100, angle, 20, 8);
        var second = Oriented(100 + dx * 26, 100 + dy * 26, angle, 20, 8);
        var quads = Quads(first, second);

        Assert.Equal(3, quads.Count);

        // Middle of the gap, half way up the glyphs.
        double ux = dy, uy = -dx;
        double gx = 100 + dx * 23 + ux * 4;
        double gy = 100 + dy * 23 + uy * 4;

        using var path = ToWindingPath(quads);
        Assert.True(path.Contains((float)gx, (float)gy));
    }

    [Fact]
    public void CurvedLine_FollowsTheBaseline()
    {
        // Words laid out along an arc (text on a path): each word has its own orientation.
        const double cx = 200, cy = 200, radius = 100, height = 8;
        var words = new List<PdfRectangle>();
        var gapMidpoints = new List<SKPoint>();

        double theta = -Math.PI * 0.9; // position on the circle, clockwise in Y-down space
        for (int i = 0; i < 5; ++i)
        {
            // Baseline start on the circle, word direction tangent to it.
            double x = cx + radius * Math.Cos(theta);
            double y = cy + radius * Math.Sin(theta);
            double tangent = theta + Math.PI / 2;
            words.Add(Oriented(x, y, tangent, 15, height));

            double wordArc = 15.0 / radius, gapArc = 6.0 / radius;
            // Mid-gap point, half way up the glyph band (away from the centre is 'up' for this layout).
            double mid = theta + wordArc + gapArc / 2;
            double r = radius + height / 2;
            gapMidpoints.Add(new SKPoint((float)(cx + r * Math.Cos(mid)), (float)(cy + r * Math.Sin(mid))));

            theta += wordArc + gapArc;
        }

        var quads = Quads(words.ToArray());
        Assert.Equal(words.Count * 2 - 1, quads.Count);

        using var path = ToWindingPath(quads);
        foreach (var p in gapMidpoints.Take(words.Count - 1))
        {
            Assert.True(path.Contains(p.X, p.Y), $"Gap point ({p.X}, {p.Y}) not covered.");
        }

        // A single bounding box would cover the centre of the arc; the per-line geometry must not.
        Assert.False(path.Contains((float)cx, (float)cy));
    }
}
