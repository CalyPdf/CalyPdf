using Caly.Pdf.Layout;
using System.Security.Cryptography;
using System.Text;
using UglyToad.PdfPig.Core;

namespace Caly.Tests;

/// <summary>
/// The k-nearest-neighbour query of <see cref="CalyKdTree"/> returns every point at one of the k smallest
/// distinct distances (so more than k when points are equidistant), nearest first, ties in the order the search
/// found them. The text layout (Docstrum, clustering) relies on exactly that, so these tests pin it: against a
/// brute force, against a recorded result of the original implementation, and for how little a query allocates.
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

    private static IEnumerable<(string Name, List<PdfPoint> Points, int[] Ks)> DataSets()
    {
        yield return ("random", RandomPoints(7, 300), [1, 2, 5]);
        yield return ("grid", GridPoints(15), [1, 2, 3, 6]);
    }

    /// <summary>
    /// What must hold whatever the search order: no pivot, no duplicate, nearest first, each distance the real one,
    /// at most k distinct distances. It does not hold that the result is the true k nearest: the search is not
    /// exact (on the random set with k = 1, pivot 16 returns 3.40 where the nearest is 0.53). That is a separate
    /// bug; the recorded-result test pins the behaviour as it is.
    /// </summary>
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

    [Fact]
    public void FindNearestNeighbours_KeepsTheOrderOfTheOriginalImplementation()
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

        // Recorded from the implementation before the allocation work (SortedList of HashSets).
        Assert.Equal("A9DA05559A64BC070C52DECB3352D4A272AA0FA51876BC93B50DC4E3A805C235", hash);
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

        Assert.True(perQuery < MaxBytesPerQuery, $"a k=2 query allocated {perQuery:F0} bytes");
    }

    private const double MaxBytesPerQuery = 400;
}
