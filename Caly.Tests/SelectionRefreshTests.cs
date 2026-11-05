using Avalonia;
using Caly.Core.Controls;
using Caly.Core.Models;
using Caly.Pdf.Models;
using UglyToad.PdfPig.Core;

namespace Caly.Tests;

public class SelectionRefreshTests
{
    private static PdfWord Word(double xStart)
    {
        var a = new PdfLetter("a", new PdfRectangle(xStart, 10, xStart + 10, 0), 10f, 0);
        var b = new PdfLetter("b", new PdfRectangle(xStart + 10, 10, xStart + 20, 0), 10f, 0);
        return new PdfWord([a, b]);
    }

    [Fact]
    public void Extend_WithinSameLetter_DoesNotRaiseExtended()
    {
        var word = Word(0);
        var sel = new TextSelection(1);
        int extended = 0;
        sel.TextSelectionExtended += (_, _) => extended++;

        sel.Start(1, word, new Point(1, 5));
        sel.Extend(1, word, new Point(2, 5));
        sel.Extend(1, word, new Point(3, 5)); // same letter 'a'
        Assert.Equal(1, extended);

        sel.Extend(1, word, new Point(15, 5)); // letter 'b'
        Assert.Equal(2, extended);
        Assert.Equal(1, sel.FocusOffset);
    }

    [Fact]
    public void Extend_AfterStart_RaisesExtendedEvenIfFocusUnchanged()
    {
        var w1 = Word(0);
        var w2 = Word(30);
        var sel = new TextSelection(1);
        int extended = 0;
        sel.TextSelectionExtended += (_, _) => extended++;

        sel.Start(1, w1);
        sel.Extend(1, w2);
        sel.Start(1, w2); // anchor moved, focus did not
        sel.Extend(1, w2);

        Assert.Equal(2, extended);
    }

    private static PdfRectangle[] Line(double y) => [new PdfRectangle(0, y + 10, 50, y)];

    [Fact]
    public void HighlightPaths_ReusesUnchangedLines_ForwardAndBackward()
    {
        PdfRectangle[] a = Line(0), b = Line(20), c = Line(40);
        Caly.Core.Utilities.IRef<PageInteractiveLayerControl.HighlightPaths>? field = null;

        PageInteractiveLayerControl.HighlightPaths.Replace(ref field, [a, b]);
        var first = field!.Item;
        var pathA = first[0];
        var pathB = first[1];

        // Forward drag: a line added at the end
        PageInteractiveLayerControl.HighlightPaths.Replace(ref field, [a, b, c]);
        Assert.Same(pathA, field!.Item[0]);
        Assert.Same(pathB, field.Item[1]);
        Assert.NotEqual(IntPtr.Zero, pathA.Handle); // the old set was released, the shared paths were not

        // Backward drag: a line added at the start
        var pathC = field.Item[2];
        PageInteractiveLayerControl.HighlightPaths.Replace(ref field, [Line(-20), a, b, c]);
        Assert.Same(pathA, field!.Item[1]);
        Assert.Same(pathC, field.Item[3]);

        // A changed line gets a new path
        PageInteractiveLayerControl.HighlightPaths.Replace(ref field, [Line(-20), a, b, Line(41)]);
        Assert.NotSame(pathC, field!.Item[3]);
        Assert.Equal(IntPtr.Zero, pathC.Handle); // no one holds it anymore: disposed

        PageInteractiveLayerControl.HighlightPaths.Replace(ref field, null);
        Assert.Null(field);
        Assert.Equal(IntPtr.Zero, pathA.Handle);
    }
}
