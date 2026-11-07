using Caly.Core.Services.Rendering;
using System.Globalization;

namespace Caly.Tests;

/// <summary>
/// <see cref="MemoryProbe"/> is a process-wide singleton, so every test that starts or stops it runs
/// in this class, one after the other.
/// </summary>
public class MemoryProbeTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "caly-memory-probe-" + Guid.NewGuid().ToString("N"));

    public MemoryProbeTests()
    {
        Directory.CreateDirectory(_directory);
        MemoryProbe.Stop();
    }

    public void Dispose()
    {
        MemoryProbe.Stop();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: the temp folder is scratch.
        }
    }

    private static MemorySample Sample(long workingSet = 0)
        => new(UtcMs: 1, Label: "x", WorkingSet: workingSet, PrivateBytes: 2, GcHeapSize: 3, GcFragmented: 4,
            GcCommitted: 5, GcTotalAllocated: 6, SkiaResourceCache: 7, SkiaFontCache: 8, GpuCache: 9, TileCache: 10);

    [Fact]
    public void FormatLine_HasSameFieldCountAsHeader()
    {
        string[] line = MemoryProbe.FormatLine(Sample()).Split(',');
        string[] header = MemoryProbe.Header.Split(',');

        Assert.Equal(12, header.Length);
        Assert.Equal(header.Length, line.Length);
    }

    [Fact]
    public void Header_IsExactlyTheColumnsTheDriversRead()
    {
        Assert.Equal(
            "utc_ms,label,working_set,private_bytes,gc_heap,gc_fragmented,gc_committed,gc_total_allocated,skia_resource_cache,skia_font_cache,gpu_cache,tile_cache",
            MemoryProbe.Header);
    }

    [Fact]
    public void FormatLine_UsesInvariantCulture()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");

            string line = MemoryProbe.FormatLine(Sample(workingSet: 1234567));

            Assert.Equal("1,x,1234567,2,3,4,5,6,7,8,9,10", line);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Probe_StopsQuietly_WhenLogFolderUnwritable()
    {
        // A folder cannot be created below a file, whatever the platform or the user's rights.
        string file = Path.Combine(_directory, "not-a-folder");
        File.WriteAllText(file, "x");

        bool started = MemoryProbe.TryStart(Path.Combine(file, "logs"));

        Assert.False(started);
        MemoryProbe.Mark("x"); // must not throw
    }

    [Fact]
    public void Probe_StopsQuietly_WhenAWriteFailsAfterStart()
    {
        string file = Path.Combine(_directory, "not-a-folder");
        File.WriteAllText(file, "x");
        string previousLogDirectory = Caly.Core.Debug.LogDirectory;

        Assert.True(MemoryProbe.TryStart(_directory));
        Thread.Sleep(200); // lets the first timer sample finish; the next is 300 ms away

        // A failed write (disk full, folder gone) is simulated by disposing the log's writer. The crash log
        // cannot be written either: it would go below a file.
        var writer = (StreamWriter)typeof(MemoryProbe)
            .GetField("_writer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null)!;
        Caly.Core.Debug.LogDirectory = Path.Combine(file, "logs");
        try
        {
            writer.Dispose();

            MemoryProbe.Mark("x"); // must not throw: it runs on the GC timer's thread, where an exception ends the process
            MemoryProbe.Mark("y"); // the probe stopped itself, so this is a no-op
        }
        finally
        {
            Caly.Core.Debug.LogDirectory = previousLogDirectory;
        }
    }

    [Fact]
    public void Probe_DoesNothing_WhenDisabled()
    {
        // Not started: Mark is a no-op and creates nothing.
        MemoryProbe.Mark("x");

        Assert.Empty(Directory.GetFileSystemEntries(_directory));
    }

    [Fact]
    public void Probe_WritesHeaderAndMarkedLine_WhenStarted()
    {
        Assert.True(MemoryProbe.TryStart(_directory));

        MemoryProbe.Mark("close-gc");
        MemoryProbe.Stop();

        string file = Assert.Single(Directory.GetFiles(_directory, "memory_*.csv"));
        string[] lines = File.ReadAllLines(file);

        Assert.Equal(MemoryProbe.Header, lines[0]);
        string[] marked = Assert.Single(lines, l => l.Split(',')[1] == "close-gc").Split(',');
        Assert.Equal(12, marked.Length);
        Assert.True(long.Parse(marked[2], CultureInfo.InvariantCulture) > 0, "working set should be non-zero");
    }
}
