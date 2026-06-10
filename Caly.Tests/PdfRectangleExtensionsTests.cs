using Caly.Pdf;
using Caly.Pdf.Models;
using UglyToad.PdfPig.Core;

namespace Caly.Tests;

public class PdfRectangleExtensionsTests
{
    // Rotates a point by angleDeg (counter-clockwise) about (cx, cy). A proper rotation
    // (determinant +1) preserves handedness, so it never changes the flipped state.
    private static PdfPoint Rotate(PdfPoint p, double cx, double cy, double angleDeg)
    {
        double a = angleDeg * Math.PI / 180.0;
        double cos = Math.Cos(a), sin = Math.Sin(a);
        double dx = p.X - cx, dy = p.Y - cy;
        return new PdfPoint(cx + dx * cos - dy * sin, cy + dx * sin + dy * cos);
    }

    /// <summary>
    /// Builds a w x h rectangle centred at (cx, cy) in the text layer's top-left origin (inverse-Y)
    /// space, optionally flipped across its baseline, then rotated by <paramref name="angleDeg"/>.
    /// </summary>
    private static PdfRectangle Make(double cx, double cy, double w, double h, double angleDeg, bool flipped)
    {
        double l = cx - w / 2, r = cx + w / 2;
        double top = cy - h / 2;     // smaller Y is visually higher (inverse-Y)
        double bottom = cy + h / 2;

        // Upright: top corners have the smaller Y. Flipped: top/bottom Y swapped.
        double topY = flipped ? bottom : top;
        double bottomY = flipped ? top : bottom;

        var tl = new PdfPoint(l, topY);
        var tr = new PdfPoint(r, topY);
        var bl = new PdfPoint(l, bottomY);
        var br = new PdfPoint(r, bottomY);

        return new PdfRectangle(
            Rotate(tl, cx, cy, angleDeg), Rotate(tr, cx, cy, angleDeg),
            Rotate(bl, cx, cy, angleDeg), Rotate(br, cx, cy, angleDeg));
    }

    private static IEnumerable<double> Angles =>
        new double[] { 0, 34, 90, 137, 180, 215, 270, 313, -34, -90 };

    [Fact]
    public void UprightRectangles_AreNotFlipped_AtEveryRotation()
    {
        foreach (var angle in Angles)
        {
            var rect = Make(100, 200, 40, 16, angle, flipped: false);
            Assert.False(rect.IsMirrored(), $"upright @ {angle}° reported flipped");
        }
    }

    [Fact]
    public void FlippedRectangles_AreFlipped_AtEveryRotation()
    {
        foreach (var angle in Angles)
        {
            var rect = Make(100, 200, 40, 16, angle, flipped: true);
            Assert.True(rect.IsMirrored(), $"flipped @ {angle}° not detected");
        }
    }

    [Fact]
    public void Unflip_MakesFlippedUpright_AtEveryRotation_PreservingGeometry()
    {
        foreach (var angle in Angles)
        {
            var flipped = Make(100, 200, 40, 16, angle, flipped: true);
            var fixedRect = flipped.UnMirror();

            Assert.True(flipped.IsMirrored(), $"precondition @ {angle}°");
            Assert.False(fixedRect.IsMirrored(), $"still flipped after Unflip @ {angle}°");

            // Same region (same four corner points as a set) and same size.
            Assert.Equal(flipped.Width, fixedRect.Width, 6);
            Assert.Equal(flipped.Height, fixedRect.Height, 6);
            AssertSameCornerSet(flipped, fixedRect);

            // Reading direction (rotation of the bottom edge) is preserved.
            Assert.Equal(flipped.Rotation, fixedRect.Rotation, 6);
        }
    }

    [Fact]
    public void Unflip_IsNoOp_WhenAlreadyUpright()
    {
        foreach (var angle in Angles)
        {
            var upright = Make(100, 200, 40, 16, angle, flipped: false);
            var result = upright.UnMirror();
            Assert.Equal(upright, result);
        }
    }

    [Fact]
    public void Unflip_IsIdempotent()
    {
        var flipped = Make(100, 200, 40, 16, 34, flipped: true);
        var once = flipped.UnMirror();
        var twice = once.UnMirror();
        Assert.Equal(once, twice);
    }
    
    // ---- Real captured glyph boxes (text layer, top-left origin) ----

    [Fact]
    public void RealUprightGlyph_IsNotFlipped()
    {
        // test_a-5.pdf 'P'
        var rect = new PdfRectangle(
            new PdfPoint(36.0, 33.8), new PdfPoint(42.0, 33.8),
            new PdfPoint(36.0, 43.8), new PdfPoint(42.0, 43.8));
        Assert.Equal(0, rect.Rotation, 3);
        Assert.False(rect.IsMirrored());
    }

    [Fact]
    public void RealFlippedGlyph_WithZeroRotation_IsFlipped()
    {
        // GHOSTSCRIPT-695513-0.pdf '1' — rotation reads 0 but the box is mirrored.
        var rect = new PdfRectangle(
            new PdfPoint(95.6, 145.7), new PdfPoint(118.7, 145.7),
            new PdfPoint(95.6, 128.3), new PdfPoint(118.7, 128.3));
        Assert.Equal(0, rect.Rotation, 3);
        Assert.True(rect.IsMirrored());
        Assert.False(rect.UnMirror().IsMirrored());
    }

    [Fact]
    public void RealUprightRotate90Glyph_IsNotFlipped()
    {
        // GHOSTSCRIPT-697234-0.pdf — vertical (Rotate90) text.
        var rect = new PdfRectangle(
            new PdfPoint(22.3, 198.5), new PdfPoint(22.3, 188.3),
            new PdfPoint(32.9, 198.5), new PdfPoint(32.9, 188.3));
        Assert.False(rect.IsMirrored());
    }

    [Fact]
    public void DegenerateBox_IsNotFlipped()
    {
        var rect = new PdfRectangle(
            new PdfPoint(10.0, 10.0), new PdfPoint(10.0, 10.0),
            new PdfPoint(10.0, 10.0), new PdfPoint(10.0, 10.0));
        Assert.False(rect.IsMirrored());
        Assert.Equal(rect, rect.UnMirror());
    }

    private static void AssertSameCornerSet(PdfRectangle a, PdfRectangle b)
    {
        var setA = new[] { a.TopLeft, a.TopRight, a.BottomLeft, a.BottomRight };
        var setB = new[] { b.TopLeft, b.TopRight, b.BottomLeft, b.BottomRight };
        foreach (var pa in setA)
        {
            Assert.Contains(setB, pb => Math.Abs(pa.X - pb.X) < 1e-6 && Math.Abs(pa.Y - pb.Y) < 1e-6);
        }
    }
}
