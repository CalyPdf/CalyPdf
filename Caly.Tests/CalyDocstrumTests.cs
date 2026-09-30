using Caly.Pdf;
using Caly.Pdf.Layout;
using Caly.Pdf.Models;
using Caly.Pdf.PageFactories;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;

namespace Caly.Tests;

/// <summary>
/// Mirrors PdfPig's tests for its Docstrum page segmenter and k-d tree (PdfPig commit 8320fa86),
/// which <see cref="CalyDocstrum"/> and <see cref="CalyKdTree{T}"/> are single-precision ports of.
/// </summary>
public sealed class CalyDocstrumTests
{
    private static readonly string[] Documents = ["ICML03-081.pdf", "algo.pdf"];

    private sealed class Segment(PdfPoint start, PdfPoint end)
    {
        public PdfPoint Start { get; } = start;

        public PdfPoint End { get; } = end;
    }

    [Fact]
    public void ParallelBuildGivesTheSameTree()
    {
        // Enough points for several levels to be built in parallel, and duplicated
        // coordinates so that the ties are broken by index
        var random = new Random(7);
        var points = new PdfPoint[50_000];
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = new PdfPoint(random.Next(0, 500), random.Next(0, 500));
        }

        var sequential = new CalyKdTree<PdfPoint>(points, p => p);
        var parallel = new CalyKdTree<PdfPoint>(points, p => p, new ParallelOptions { MaxDegreeOfParallelism = -1 });

        var sequentialNodes = new Stack<CalyKdTree<PdfPoint>.CalyKdTreeNode<PdfPoint>?>([sequential.Root]);
        var parallelNodes = new Stack<CalyKdTree<PdfPoint>.CalyKdTreeNode<PdfPoint>?>([parallel.Root]);
        int count = 0;
        while (sequentialNodes.Count > 0)
        {
            var s = sequentialNodes.Pop();
            var p = parallelNodes.Pop();
            if (s is null)
            {
                Assert.Null(p);
                continue;
            }

            Assert.NotNull(p);
            Assert.Equal(s.Index, p.Index);
            Assert.Equal(s.Depth, p.Depth);
            count++;

            sequentialNodes.Push(s.LeftChild);
            sequentialNodes.Push(s.RightChild);
            parallelNodes.Push(p.LeftChild);
            parallelNodes.Push(p.RightChild);
        }

        Assert.Equal(points.Length, count);
    }

    [Fact]
    public void FindNearestNeighboursMatchesBruteForce()
    {
        var random = new Random(3);
        for (int run = 0; run < 100; run++)
        {
            int count = random.Next(3, 300);
            var segments = new List<Segment>();
            for (int i = 0; i < count; i++)
            {
                var start = new PdfPoint(random.NextDouble() * 100, random.NextDouble() * 100);
                var end = new PdfPoint(start.X + random.NextDouble() * 5, start.Y);
                segments.Add(new Segment(start, end));
            }

            var kdTree = new CalyKdTree<Segment>(segments, s => s.Start);

            for (int k = 1; k <= 3; k++)
            {
                for (int i = 0; i < segments.Count; i++)
                {
                    var pivot = segments[i];
                    var neighbours = kdTree.FindNearestNeighbours(pivot, k, s => s.End, CalyDistances.Euclidean);

                    // The k smallest distances (excluding the pivot), with all the elements at these distances
                    var distances = segments.Select((s, j) => (Index: j, Distance: CalyDistances.Euclidean(s.Start, pivot.End)))
                        .Where(x => x.Index != i)
                        .ToList();
                    var kSmallest = distances.Select(x => x.Distance).Distinct().OrderBy(d => d).Take(k).ToList();
                    var expected = distances.Where(x => kSmallest.Contains(x.Distance)).Select(x => x.Index).OrderBy(x => x);

                    Assert.Equal(expected, neighbours.Select(n => n.Item2).OrderBy(x => x));
                }
            }
        }
    }

    [Fact]
    public void AlmostZeroLengthLineIsNotOverlapping()
    {
        // A line shorter than epsilon has no direction, like a line of length zero.
        var tiny = new PdfLine(new PdfPoint(71.0736, 800), new PdfPoint(71.0738, 800));
        var zero = new PdfLine(new PdfPoint(71.0736, 800), new PdfPoint(71.0736, 800));
        var line = new PdfLine(new PdfPoint(65.9473, 704.8632), new PdfPoint(82.4136, 704.8632));

        Assert.False(CalyDocstrum.GetStructuralBlockingParameters(zero, line, 1e-3f, out _, out _, out _));
        Assert.False(CalyDocstrum.GetStructuralBlockingParameters(tiny, line, 1e-3f, out _, out _, out _));
    }

    [Theory]
    [MemberData(nameof(DocumentNames))]
    public void SpacingEstimationIsDeterministic(string documentName)
    {
        var options = new CalyDocstrum.CalyDocstrumOptions();
        using var document = OpenDocument(documentName);

        // The first page with enough words for the search to run in parallel
        var words = Enumerable.Range(1, document.NumberOfPages)
            .Select(p => GetWords(document, p))
            .First(w => w.Count >= CalyClustering.KNearestParallelThreshold * 4);
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = -1 };

        CalyDocstrum.GetSpacingEstimation(words, options.WithinLineBounds, options.WithinLineBinSize,
            options.BetweenLineBounds, options.BetweenLineBinSize, parallelOptions,
            out float expectedWithinLine, out float expectedBetweenLine);

        for (int run = 0; run < 20; run++)
        {
            CalyDocstrum.GetSpacingEstimation(words, options.WithinLineBounds, options.WithinLineBinSize,
                options.BetweenLineBounds, options.BetweenLineBinSize, parallelOptions,
                out float withinLine, out float betweenLine);

            // Exactly the same values, not just within a tolerance
            Assert.Equal(expectedWithinLine.ToString("R"), withinLine.ToString("R"));
            Assert.Equal(expectedBetweenLine.ToString("R"), betweenLine.ToString("R"));
        }
    }

    /// <summary>
    /// The closest lines are only searched among the lines whose bounding boxes are close enough,
    /// which must give the same result as comparing every pair of lines.
    /// </summary>
    [Theory]
    [MemberData(nameof(DocumentNames))]
    public void ClosestLineIndexesMatchBruteForce(string documentName)
    {
        var options = new CalyDocstrum.CalyDocstrumOptions();
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = -1 };

        using var document = OpenDocument(documentName);
        for (int p = 1; p <= document.NumberOfPages; p++)
        {
            var words = GetWords(document, p);
            if (words.Count == 0)
            {
                continue;
            }

            CalyDocstrum.GetSpacingEstimation(words, options.WithinLineBounds, options.WithinLineBinSize,
                options.BetweenLineBounds, options.BetweenLineBinSize, parallelOptions,
                out float withinLine, out float betweenLine);

            var lines = CalyDocstrum.GetLines(words, options.WithinLineMultiplier * withinLine,
                options.WithinLineBounds, parallelOptions).ToArray();
            float maxBLDistance = options.BetweenLineMultiplier * betweenLine;
            var bounds = options.AngularDifferenceBounds;

            var actual = CalyDocstrum.GetClosestLineIndexes(lines, maxBLDistance, bounds, options.Epsilon, parallelOptions);

            for (int i = 0; i < lines.Length; i++)
            {
                var pivot = new PdfLine(lines[i].BoundingBox.BottomLeft, lines[i].BoundingBox.BottomRight);
                float closestDistance = float.MaxValue;
                int closestIndex = -1;
                for (int j = 0; j < lines.Length; j++)
                {
                    if (j == i)
                    {
                        continue;
                    }

                    var candidate = new PdfLine(lines[j].BoundingBox.TopLeft, lines[j].BoundingBox.TopRight);
                    float distance = CalyDocstrum.PerpendicularOverlappingDistance(in pivot, in candidate, in bounds, options.Epsilon);
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closestIndex = j;
                    }
                }

                int expected = closestIndex != -1 && closestDistance < maxBLDistance ? closestIndex : -1;
                Assert.True(expected == actual[i], $"Page {p}, line {i}: expected {expected}, got {actual[i]}.");
            }
        }
    }

    public static TheoryData<string> DocumentNames() => new(Documents);

    private static PdfDocument OpenDocument(string documentName)
    {
        var document = PdfDocument.Open(Path.Combine(AppContext.BaseDirectory, "Documents", documentName));

        // TextLayerFactory reads the PPI scale back out of this fake indirect object, the way
        // PdfPigDocumentService puts it there.
        document.Advanced.ReplaceIndirectObject(CalyPdfHelper.FakePpiReference, new NumericToken(1));
        document.AddPageFactory<PageTextLayerContent, TextLayerFactory>();
        return document;
    }

    private static IReadOnlyList<PdfWord> GetWords(PdfDocument document, int pageNumber)
    {
        var content = document.GetPageTextLayerContent(pageNumber, CancellationToken.None);
        var letters = CalyDuplicateOverlappingTextProcessor.GetInPlace(content.Letters, CancellationToken.None);

        // Same filter as CalyDocstrum.GetBlocks
        return CalyNNWordExtractor.Instance.GetWords(letters, CancellationToken.None)
            .Where(w => !w.Value.AsSpan().IsEmpty)
            .ToArray();
    }
}
