// Prints a before/after table from one or two results folders written by run-memory-scenario.{ps1,sh}.
//
//   dotnet run compare-memory.cs -- <results folder> [<results folder> ...]
//   dotnet run compare-memory.cs -- --self-test
//
// Sizes are MiB (1024 * 1024 bytes). With two folders a third column group shows second minus first.
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

if (args.Contains("--self-test"))
{
    return SelfTest.Run();
}

string[] folders = [.. args.Where(a => !a.StartsWith("--", StringComparison.Ordinal))];
if (folders.Length == 0)
{
    Console.Error.WriteLine("usage: dotnet run compare-memory.cs -- <results folder> [<results folder> ...]");
    return 2;
}

Console.WriteLine(Report.Build([.. folders.Select(RunData.Load)]));
return 0;

internal sealed record Sample(long UtcMs, long WorkingSet, long PrivateBytes, long GcHeap, long Skia, long Gpu, long Tile);

internal sealed record RunData(string Name, IReadOnlyList<(long UtcMs, string Label)> Steps, IReadOnlyList<Sample> Samples, string? RenderTimings)
{
    public static RunData Load(string folder)
    {
        var steps = File.ReadLines(Path.Combine(folder, "steps.csv")).Skip(1).Where(l => l.Length > 0)
            .Select(l => l.Split(','))
            .Select(p => (long.Parse(p[0], CultureInfo.InvariantCulture), p[1]))
            .ToList();

        var samples = File.ReadLines(Path.Combine(folder, "memory.csv")).Skip(1).Where(l => l.Length > 0)
            .Select(l => l.Split(','))
            .Select(p =>
            {
                long V(int i) => long.Parse(p[i], CultureInfo.InvariantCulture);
                // columns: utc_ms,label,working_set,private_bytes,gc_heap,gc_fragmented,gc_committed,
                // gc_total_allocated,skia_resource_cache,skia_font_cache,gpu_cache,tile_cache
                return new Sample(V(0), V(2), V(3), V(4), V(8) + V(9), V(10), V(11));
            })
            .ToList();

        string timings = Path.Combine(folder, "render_timings.txt");
        return new RunData(Path.GetFileName(Path.GetFullPath(folder)), steps, samples, File.Exists(timings) ? File.ReadAllText(timings) : null);
    }
}

internal static class Compare
{
    private const double Mb = 1024.0 * 1024.0;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The last sample taken at or before <paramref name="utcMs"/>, or null if none.</summary>
    public static Sample? PickSample(IReadOnlyList<Sample> samples, long utcMs)
    {
        Sample? best = null;
        foreach (Sample s in samples)
        {
            if (s.UtcMs <= utcMs && (best is null || s.UtcMs >= best.UtcMs))
            {
                best = s;
            }
        }

        return best;
    }

    /// <summary>Tiles per draw op from a render timings dump, or null when absent.</summary>
    public static double? TilesPerOp(string? renderTimings)
    {
        if (renderTimings is null)
        {
            return null;
        }

        // RenderTimings prints with the machine's culture, so accept a decimal comma as well as a point.
        Match m = Regex.Match(renderTimings, @"avg\s+([0-9]+(?:[.,][0-9]+)?)\s+per op");
        return m.Success ? double.Parse(m.Groups[1].Value.Replace(',', '.'), Inv) : null;
    }

    /// <summary>A warning when tiles per draw op is below 3 (the run measured blank page area), else null.</summary>
    public static string? TilesWarning(string? renderTimings) =>
        TilesPerOp(renderTimings) is { } tpo and < 3
            ? $"WARNING: low tiles per draw op ({tpo.ToString("F1", Inv)} < 3): this run measured blank page area, not content"
            : null;

    /// <summary>A LEAK? line when working set after the last close cycle exceeds the first by more than 10 %, else null.</summary>
    public static string? LeakLine(IReadOnlyDictionary<string, Sample> atMark)
    {
        var closed = atMark
            .Where(kv => kv.Key.StartsWith("D-closed-", StringComparison.Ordinal) && int.TryParse(kv.Key["D-closed-".Length..], out _))
            .OrderBy(kv => int.Parse(kv.Key["D-closed-".Length..], Inv))
            .ToList();
        if (closed.Count < 2)
        {
            return null;
        }

        var (firstLabel, first) = (closed[0].Key, closed[0].Value);
        var (lastLabel, last) = (closed[^1].Key, closed[^1].Value);
        if (last.WorkingSet <= first.WorkingSet * 1.10)
        {
            return null;
        }

        return $"LEAK? working set after close grew from {first.WorkingSet / Mb:F1} MB ({firstLabel}) to {last.WorkingSet / Mb:F1} MB ({lastLabel}): {Percent(first.WorkingSet, last.WorkingSet)}";
    }

    /// <summary>"after-close vs baseline: +N %" from A-baseline and D-closed-1, or null if either is missing.</summary>
    public static string? AfterCloseLine(IReadOnlyDictionary<string, Sample> atMark) =>
        atMark.TryGetValue("A-baseline", out Sample? a) && atMark.TryGetValue("D-closed-1", out Sample? d)
            ? $"after-close vs baseline: {Percent(a.WorkingSet, d.WorkingSet)} ({a.WorkingSet / Mb:F1} -> {d.WorkingSet / Mb:F1} MB working set; goal: within 10 %)"
            : null;

    private static string Percent(long from, long to)
    {
        double p = from == 0 ? 0 : (to - from) * 100.0 / from;
        return $"{(p >= 0 ? "+" : "-")}{Math.Abs(Math.Round(p)).ToString("0", Inv)} %";
    }
}

internal static class Report
{
    private const double Mb = 1024.0 * 1024.0;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly string[] Columns = ["ws", "priv", "heap", "skia", "gpu", "tile"];

    private static double[] Values(Sample s) => [s.WorkingSet / Mb, s.PrivateBytes / Mb, s.GcHeap / Mb, s.Skia / Mb, s.Gpu / Mb, s.Tile / Mb];

    private static Sample Peak(IReadOnlyList<Sample> samples) => new(0,
        samples.Max(s => s.WorkingSet), samples.Max(s => s.PrivateBytes), samples.Max(s => s.GcHeap),
        samples.Max(s => s.Skia), samples.Max(s => s.Gpu), samples.Max(s => s.Tile));

    public static string Build(IReadOnlyList<RunData> runs)
    {
        var sb = new StringBuilder();

        // Per run: the sample in force at each mark, in the order the marks happened.
        var atMark = runs.Select(r => r.Steps
            .Select(s => (s.Label, Sample: Compare.PickSample(r.Samples, s.UtcMs)))
            .Where(x => x.Sample is not null)
            .ToDictionary(x => x.Label, x => x.Sample!)).ToList();
        var labels = runs.SelectMany(r => r.Steps.Select(s => s.Label)).Distinct().ToList();

        var header = new List<string> { "step" };
        for (int i = 0; i < runs.Count; i++)
        {
            header.AddRange(Columns.Select(c => $"r{i + 1} {c}"));
        }

        if (runs.Count == 2)
        {
            header.AddRange(Columns.Select(c => $"diff {c}"));
        }

        sb.AppendLine("MiB: ws = working set, priv = private bytes, heap = GC heap, skia = Skia resource + font caches, gpu = GPU cache, tile = tile caches");
        sb.AppendLine();
        sb.AppendLine("| " + string.Join(" | ", header) + " |");
        sb.AppendLine("|" + string.Join("|", header.Select(_ => "---")) + "|");

        void Row(string label, Func<int, Sample?> pick)
        {
            var cells = new List<string> { label };
            var values = new List<double[]?>();
            for (int i = 0; i < runs.Count; i++)
            {
                Sample? s = pick(i);
                values.Add(s is null ? null : Values(s));
                cells.AddRange(Columns.Select((_, c) => s is null ? "-" : Values(s)[c].ToString("F1", Inv)));
            }

            if (runs.Count == 2)
            {
                cells.AddRange(Columns.Select((_, c) => values[0] is { } a && values[1] is { } b ? (b[c] - a[c]).ToString("+0.0;-0.0;0.0", Inv) : "-"));
            }

            sb.AppendLine("| " + string.Join(" | ", cells) + " |");
        }

        foreach (string label in labels)
        {
            Row(label, i => atMark[i].GetValueOrDefault(label));
        }

        Row("peak (max over run)", i => runs[i].Samples.Count == 0 ? null : Peak(runs[i].Samples));

        sb.AppendLine();
        for (int i = 0; i < runs.Count; i++)
        {
            string id = $"r{i + 1}";
            sb.AppendLine($"{id} = {runs[i].Name}");
            double? tpo = Compare.TilesPerOp(runs[i].RenderTimings);
            sb.AppendLine($"{id}: tiles per draw op: {(tpo is { } t ? t.ToString("F1", Inv) : "n/a (no render_timings.txt)")}");
            foreach (string? line in new[] { Compare.TilesWarning(runs[i].RenderTimings), Compare.AfterCloseLine(atMark[i]), Compare.LeakLine(atMark[i]) })
            {
                if (line is not null)
                {
                    sb.AppendLine($"{id}: {line}");
                }
            }
        }

        return sb.ToString();
    }
}

internal static class SelfTest
{
    private const long Mb = 1024 * 1024;

    private static Sample S(long utcMs, long workingSetMb = 0) => new(utcMs, workingSetMb * Mb, 0, 0, 0, 0, 0);

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Compare_PicksLastSampleAtOrBeforeMark()
    {
        Sample[] samples = [S(1000, 1), S(2000, 2), S(3000, 3)];

        Check(Compare.PickSample(samples, 2500)?.WorkingSet == 2 * Mb, "mark at 2500 should use the 2000 sample");
        Check(Compare.PickSample(samples, 3000)?.WorkingSet == 3 * Mb, "a sample exactly at the mark counts");
        Check(Compare.PickSample(samples, 500) is null, "a mark before every sample has none");
    }

    private static void Compare_FlagsLowTilesPerDrawOp()
    {
        Check(Compare.TilesWarning("Tiles drawn    : 5 (avg 2.9 per op)") is { } w && w.Contains("WARNING") && w.Contains("blank"),
            "avg 2.9 per op should warn about blank page area");
        Check(Compare.TilesWarning("Tiles drawn    : 3303 (avg 4.6 per op)") is null, "avg 4.6 per op should not warn");
        Check(Compare.TilesPerOp("Tiles drawn    : 3303 (avg 6.4 per op)") == 6.4, "tiles per op should be parsed");
    }

    private static void Compare_FlagsGrowthAcrossCloseCycles()
    {
        var grew = new Dictionary<string, Sample> { ["D-closed-1"] = S(1, 300), ["D-closed-2"] = S(2, 320), ["D-closed-3"] = S(3, 340) };
        var stable = new Dictionary<string, Sample> { ["D-closed-1"] = S(1, 300), ["D-closed-3"] = S(3, 320) };

        Check(Compare.LeakLine(grew) is { } line && line.StartsWith("LEAK?", StringComparison.Ordinal), "300 -> 340 MB (+13 %) should be flagged");
        Check(Compare.LeakLine(stable) is null, "300 -> 320 MB (+7 %) should not be flagged");
    }

    private static void Compare_ReportsAfterCloseVsBaseline()
    {
        var marks = new Dictionary<string, Sample> { ["A-baseline"] = S(1, 200), ["D-closed-1"] = S(2, 230) };

        Check(Compare.AfterCloseLine(marks) is { } line && line.Contains("+15 %"), "200 -> 230 MB should read +15 %");
        Check(Compare.AfterCloseLine(new Dictionary<string, Sample>()) is null, "no marks, no line");
    }

    public static int Run()
    {
        (string Name, Action Test)[] tests =
        [
            (nameof(Compare_PicksLastSampleAtOrBeforeMark), Compare_PicksLastSampleAtOrBeforeMark),
            (nameof(Compare_FlagsLowTilesPerDrawOp), Compare_FlagsLowTilesPerDrawOp),
            (nameof(Compare_FlagsGrowthAcrossCloseCycles), Compare_FlagsGrowthAcrossCloseCycles),
            (nameof(Compare_ReportsAfterCloseVsBaseline), Compare_ReportsAfterCloseVsBaseline),
        ];

        int failed = 0;
        foreach (var (name, test) in tests)
        {
            try
            {
                test();
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception e)
            {
                failed++;
                Console.WriteLine($"FAIL {name}: {e.GetType().Name}: {e.Message}");
            }
        }

        Console.WriteLine(failed == 0 ? $"{tests.Length}/{tests.Length} passed" : $"{failed} of {tests.Length} FAILED");
        return failed == 0 ? 0 : 1;
    }
}
