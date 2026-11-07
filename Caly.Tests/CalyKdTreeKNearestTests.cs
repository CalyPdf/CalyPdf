using Caly.Pdf.Layout;
using System.Security.Cryptography;
using System.Text;
using UglyToad.PdfPig.Core;

namespace Caly.Tests;

/// <summary>
/// The k-nearest-neighbour query of <see cref="CalyKdTree"/> returns every point at one of the k smallest
/// distinct distances (so more than k when points are equidistant), nearest first, ties in the order the search
/// visited them. The text layout (Docstrum, clustering) relies on that: clustering takes the first neighbour that
/// passes its filters, so the order of equidistant neighbours matters. These tests pin it against a brute force,
/// against a recorded result, and for how little a query allocates.
/// </summary>
public class CalyKdTreeKNearestTests
{
    private static List<PdfPoint> RandomPoints(int seed, int count)
    {
        var random = new Random(seed);
        var seen = new HashSet<(double, double)>();
        var points = new List<PdfPoint>();
        while (points.Count < count)
        {
            double x = Math.Round(random.NextDouble() * 100, 3);
            double y = Math.Round(random.NextDouble() * 100, 3);
            if (seen.Add((x, y)))
            {
                points.Add(new PdfPoint(x, y));
            }
        }

        return points;
    }

    /// <summary>A unit grid: nearly every distance is shared by several points.</summary>
    private static List<PdfPoint> GridPoints(int side)
    {
        var points = new List<PdfPoint>();
        for (int x = 0; x < side; x++)
        {
            for (int y = 0; y < side; y++)
            {
                points.Add(new PdfPoint(x, y));
            }
        }

        return points;
    }

    /// <summary>Like the bottom-left corners of words: lines of constant y with irregular gaps in x.</summary>
    private static List<PdfPoint> TextLikePoints(int seed, int lines, int wordsPerLine)
    {
        var random = new Random(seed);
        var points = new List<PdfPoint>();
        for (int line = 0; line < lines; line++)
        {
            double x = 50;
            for (int word = 0; word < wordsPerLine; word++)
            {
                points.Add(new PdfPoint(Math.Round(x, 2), 700 - line * 14));
                x += 15 + Math.Round(random.NextDouble() * 40, 2);
            }
        }

        return points;
    }

    private static IEnumerable<(string Name, List<PdfPoint> Points, int[] Ks)> DataSets()
    {
        yield return ("random", RandomPoints(7, 300), [1, 2, 5]);
        yield return ("random-500", RandomPoints(21, 500), [2]);
        yield return ("grid", GridPoints(15), [1, 2, 3, 6]);
        yield return ("text", TextLikePoints(3, 30, 12), [1, 2, 4]);
    }

    /// <summary>
    /// Every point at one of the k smallest distinct distances, whichever order the search finds them in. The search used
    /// to cut off the far side of a split while fewer than k distinct distances were held, using the largest distance
    /// held so far as the limit: it returned a wrong neighbour for about one query in six on text-like and random points
    /// (567 of 3,380 here, e.g. 3.40 where the nearest was 0.53).
    /// </summary>
    [Fact]
    public void FindNearestNeighbours_ReturnsEveryPointAtTheKSmallestDistinctDistances()
    {
        foreach (var (name, points, ks) in DataSets())
        {
            var tree = new CalyKdTree(points);

            foreach (int k in ks)
            {
                for (int i = 0; i < points.Count; i++)
                {
                    PdfPoint pivot = points[i];

                    var expected = Enumerable.Range(0, points.Count)
                        .Where(j => j != i)
                        .Select(j => (Index: j, Distance: CalyDistances.Euclidean(points[j], pivot)))
                        .ToList();
                    float limit = expected.Select(e => e.Distance).Distinct().OrderBy(d => d).Take(k).Last();
                    var expectedSet = expected.Where(e => e.Distance <= limit).OrderBy(e => e.Distance).ThenBy(e => e.Index).ToList();

                    var actual = tree.FindNearestNeighbours(pivot, k, CalyDistances.Euclidean)
                        .Select(r => (Index: r.Item2, Distance: r.Item3))
                        .OrderBy(e => e.Distance).ThenBy(e => e.Index).ToList();

                    Assert.True(expectedSet.SequenceEqual(actual), $"{name}, k={k}, pivot {i}: wrong neighbours");
                }
            }
        }
    }

    /// <summary>What must hold whatever the search order: no pivot, no duplicate, nearest first, each distance the real one.</summary>
    [Fact]
    public void FindNearestNeighbours_ReturnsOrderedDistinctNeighboursWithTheirRealDistances()
    {
        foreach (var (name, points, ks) in DataSets())
        {
            var tree = new CalyKdTree(points);

            foreach (int k in ks)
            {
                for (int i = 0; i < points.Count; i++)
                {
                    PdfPoint pivot = points[i];
                    var result = tree.FindNearestNeighbours(pivot, k, CalyDistances.Euclidean);
                    string where = $"{name}, k={k}, pivot {i}";

                    Assert.NotEmpty(result);
                    Assert.DoesNotContain(result, r => r.Item2 == i);
                    Assert.Equal(result.Count, result.Select(r => r.Item2).Distinct().Count());
                    Assert.True(result.Select(r => r.Item3).Distinct().Count() <= k, where);

                    for (int r = 0; r < result.Count; r++)
                    {
                        Assert.Equal(CalyDistances.Euclidean(points[result[r].Item2], pivot), result[r].Item3);
                        if (r > 0)
                        {
                            Assert.True(result[r].Item3 >= result[r - 1].Item3, where);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// The exact order, ties included. Recorded from the corrected search; checked once against a copy of the original
    /// search, which gave the identical sequence for every query it answered correctly (2,813 of 3,380 on these data
    /// sets), so equidistant neighbours come out in the order the layout code was already getting.
    /// </summary>
    [Fact]
    public void FindNearestNeighbours_KeepsTheRecordedOrder()
    {
        var sb = new StringBuilder();
        foreach (var (name, points, ks) in DataSets())
        {
            var tree = new CalyKdTree(points);
            foreach (int k in ks)
            {
                for (int i = 0; i < points.Count; i++)
                {
                    sb.Append(name).Append(' ').Append(k).Append(' ').Append(i).Append(':');
                    foreach (var (_, index, distance) in tree.FindNearestNeighbours(points[i], k, CalyDistances.Euclidean))
                    {
                        sb.Append(' ').Append(index).Append('/').Append(BitConverter.SingleToInt32Bits(distance));
                    }

                    sb.AppendLine();
                }
            }
        }

        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));

        Assert.Equal("A5F1A4EA149677C2C9DE19AD694867C96D79B9DBD9945987637F3A2649C670E8", hash);
    }

    [Fact]
    public void FindNearestNeighbours_AllocatesLittlePerQuery()
    {
        var points = RandomPoints(11, 400);
        var tree = new CalyKdTree(points);

        // Warm up: JIT, and the static delegates
        for (int i = 0; i < 50; i++)
        {
            tree.FindNearestNeighbours(points[i], 2, CalyDistances.Euclidean);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int round = 0; round < 5; round++)
        {
            for (int i = 0; i < points.Count; i++)
            {
                tree.FindNearestNeighbours(points[i], 2, CalyDistances.Euclidean);
            }
        }

        double perQuery = (GC.GetAllocatedBytesForCurrentThread() - before) / (5.0 * points.Count);

        // The original implementation allocated about 3,000 bytes per k = 2 query
        Assert.True(perQuery < 400, $"a k=2 query allocated {perQuery:F0} bytes");
    }
}
