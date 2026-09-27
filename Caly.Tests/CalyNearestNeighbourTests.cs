using Caly.Pdf.Layout;
using Caly.Pdf.Models;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace Caly.Tests;

/// <summary>
/// Ported from PdfPig's KdTreeTests / NearestNeighbourWordExtractorTests: the k-d tree must never
/// return the pivot as its own neighbour, and word letters are ordered by following the
/// nearest-neighbour links rather than by content-stream order.
/// </summary>
public class CalyNearestNeighbourTests
{
    private sealed class Segment
    {
        public Segment(PdfPoint start, PdfPoint end)
        {
            Start = start;
            End = end;
        }

        public PdfPoint Start { get; }

        public PdfPoint End { get; }
    }

    [Fact]
    public void FindNearestNeighbourNeverReturnsPivot()
    {
        // Pivot point (End) differs from the tree point (Start), so the pivot's own
        // tree node is at a non-zero distance and must still be excluded.
        var random = new Random(42);
        var segments = new List<Segment>();
        for (int i = 0; i < 500; i++)
        {
            var start = new PdfPoint(random.NextDouble() * 100, random.NextDouble() * 100);
            var end = new PdfPoint(start.X + random.NextDouble() * 5, start.Y);
            segments.Add(new Segment(start, end));
        }

        var kdTree = new CalyKdTree<Segment>(segments, s => s.Start);

        for (int i = 0; i < segments.Count; i++)
        {
            var pivot = segments[i];
            kdTree.FindNearestNeighbour(pivot, s => s.End, CalyDistances.Euclidean, out int index, out float distance);

            float expectedDistance = float.PositiveInfinity;
            for (int j = 0; j < segments.Count; j++)
            {
                if (j != i)
                {
                    expectedDistance = MathF.Min(expectedDistance, CalyDistances.Euclidean(segments[j].Start, pivot.End));
                }
            }

            Assert.NotEqual(i, index);
            Assert.Equal(expectedDistance, distance);
        }
    }

    private static PdfLetter CreateLetter(string value, double x, double width)
    {
        return CreateLetter(value, new PdfPoint(x, 0), width, 0);
    }

    private static PdfLetter CreateLetter(string value, PdfPoint start, double width, double angleDeg)
    {
        double rad = angleDeg * Math.PI / 180.0;
        var direction = new PdfPoint(Math.Cos(rad), Math.Sin(rad));
        var normal = new PdfPoint(-direction.Y, direction.X);
        var end = new PdfPoint(start.X + width * direction.X, start.Y + width * direction.Y);
        var bbox = new PdfRectangle(
            new PdfPoint(start.X + 7 * normal.X, start.Y + 7 * normal.Y),
            new PdfPoint(end.X + 7 * normal.X, end.Y + 7 * normal.Y),
            start,
            end);
        return new PdfLetter(value, bbox, 10f, 0);
    }

    private static List<PdfLetter> CreateWord(string text, double angleDeg)
    {
        var letters = new List<PdfLetter>();
        double rad = angleDeg * Math.PI / 180.0;
        for (int i = 0; i < text.Length; i++)
        {
            var start = new PdfPoint(100 + i * 5 * Math.Cos(rad), 100 + i * 5 * Math.Sin(rad));
            letters.Add(CreateLetter(text[i].ToString(), start, 5, angleDeg));
        }
        return letters;
    }

    private static PdfWord[] GetWords(IReadOnlyList<PdfLetter> letters, CalyNNWordExtractor? extractor = null)
    {
        return (extractor ?? CalyNNWordExtractor.Instance).GetWords(letters, CancellationToken.None).ToArray();
    }

    [Fact]
    public void NarrowLetterIsNotItsOwnNeighbour()
    {
        // The narrow 'i' (width 1) is followed by a 1.5 gap, which is below the
        // maximum distance (20% of point size 10 = 2) but above its own width.
        var letters = new List<PdfLetter>();
        double x = 0;
        foreach (var c in "abc")
        {
            letters.Add(CreateLetter(c.ToString(), x, 5));
            x += 5;
        }

        letters.Add(CreateLetter("i", x, 1));
        x += 1 + 1.5;

        foreach (var c in "def")
        {
            letters.Add(CreateLetter(c.ToString(), x, 5));
            x += 5;
        }

        Assert.Equal("abcidef", Assert.Single(GetWords(letters)).Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(90)]
    [InlineData(135)]
    [InlineData(180)]
    [InlineData(270)]
    public void LettersDrawnInReverseOrder(double angleDeg)
    {
        var letters = CreateWord("hello", angleDeg);
        letters.Reverse();

        Assert.Equal("hello", Assert.Single(GetWords(letters)).Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(210)]
    public void LettersDrawnOutOfOrder(double angleDeg)
    {
        var letters = CreateWord("abcdef", angleDeg);
        var shuffled = new[] { letters[2], letters[0], letters[5], letters[1], letters[4], letters[3] };

        Assert.Equal("abcdef", Assert.Single(GetWords(shuffled)).Value);
    }

    [Fact]
    public void MutualNeighboursDoNotLoop()
    {
        // 'a' ends where 'z' starts and 'z' ends where 'a' starts: a 2-cycle.
        var a = CreateLetter("a", new PdfPoint(0, 0), 5, 0);
        var z = CreateLetter("z", new PdfPoint(5, 0), 5, 180);
        var c = CreateLetter("c", new PdfPoint(-5, 0), 5, 0); // c -> a

        var extractor = new CalyNNWordExtractor(new CalyNNWordExtractor.CalyNNWordExtractorOptions()
        {
            GroupByOrientation = false
        });

        // The cycle is cut after its highest-index letter ('z').
        Assert.Equal("caz", Assert.Single(GetWords([a, z, c], extractor)).Value);
    }

    [Fact]
    public void WordsKeepContentStreamOrder()
    {
        var first = CreateWord("first", 0);
        var second = CreateWord("second", 0).Select(l => CreateLetter(l.Value, new PdfPoint(l.StartBaseLine.X, 200), 5, 0)).ToList();
        second.Reverse();

        var words = GetWords(first.Concat(second).ToArray());

        Assert.Equal(["first", "second"], words.Select(w => w.Value));
    }

    [Fact]
    public void WordsGroupedByOrientationKeepContentStreamOrder()
    {
        var rotated = CreateWord("rotated", 90).Select(l => CreateLetter(l.Value, new PdfPoint(500, l.StartBaseLine.Y), 5, 90)).ToList();
        var first = CreateWord("first", 0);
        var second = CreateWord("second", 0).Select(l => CreateLetter(l.Value, new PdfPoint(l.StartBaseLine.X, 200), 5, 0)).ToList();

        var letters = rotated.Take(3).Concat(first).Concat(rotated.Skip(3)).Concat(second).ToArray();

        Assert.Equal(["first", "second", "rotated"], GetWords(letters).Select(w => w.Value));
    }

    [Fact]
    public void WordsGroupedByOrientationAreInDeterministicOrder()
    {
        // Horizontal and Rotate270 words, interleaved in the content stream.
        var letters = new List<PdfLetter>();
        for (int line = 0; line < 50; line++)
        {
            for (int i = 0; i < 20; i++)
            {
                letters.Add(CreateLetter("h", new PdfPoint(i * 5, line * 20), 5, 0));
            }

            for (int i = 0; i < 20; i++)
            {
                letters.Add(CreateLetter("v", new PdfPoint(500 + line * 20, i * 5), 5, 90));
            }
        }

        // Words are returned by orientation (horizontal first).
        var expected = Enumerable.Repeat(TextOrientation.Horizontal, 50)
            .Concat(Enumerable.Repeat(TextOrientation.Rotate270, 50))
            .ToArray();

        for (int run = 0; run < 20; run++)
        {
            Assert.Equal(expected, GetWords(letters).Select(w => w.TextOrientation));
        }
    }
}
