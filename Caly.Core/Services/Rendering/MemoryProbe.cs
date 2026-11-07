// Copyright (c) BobLd
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using Caly.Core.Models;
using SkiaSharp;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Caly.Core.Services.Rendering;

/// <summary>
/// One row of the memory log. Every size is in bytes.
/// </summary>
internal readonly record struct MemorySample(
    long UtcMs,
    string Label,
    long WorkingSet,
    long PrivateBytes,
    long GcHeapSize,
    long GcFragmented,
    long GcCommitted,
    long GcTotalAllocated,
    long SkiaResourceCache,
    long SkiaFontCache,
    long GpuCache,
    long TileCache);

/// <summary>
/// Opt-in memory log, enabled with <c>"Debug": { "LogMemory": true }</c> in the settings file. Writes
/// one CSV row every half second, and one on demand through <see cref="Mark"/>, to
/// <c>memory_{yyyyMMdd_HHmmss}.csv</c> in the logs folder. It exists so a scripted session can be
/// compared before and after a change; nothing in Caly reads it.
/// </summary>
/// <remarks>
/// Like <see cref="RenderTimings"/> the flag is read straight from the settings JSON and latched at
/// first use, so it applies from the next launch. When it is off no timer, file or
/// <see cref="Process"/> object exists. A failure to write stops the probe quietly: a diagnostic
/// must never take the application down.
/// </remarks>
public static class MemoryProbe
{
    public static readonly bool IsEnabled =
        JsonSettingsService.TryReadBooleanSetting(nameof(CalySettings.Debug),
            nameof(CalySettings.CalySettingsDebug.LogMemory), out bool enabled)
        && enabled;

    internal const string Header =
        "utc_ms,label,working_set,private_bytes,gc_heap,gc_fragmented,gc_committed,gc_total_allocated,skia_resource_cache,skia_font_cache,gpu_cache,tile_cache";

    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(500);

    private static readonly object Lock = new();
    private static StreamWriter? _writer;
    private static Timer? _timer;
    private static Process? _process;
    private static long _gpuCacheBytes;
    private static int _exitHookInstalled;

    /// <summary>
    /// Starts logging when <c>LogMemory</c> is on; does nothing otherwise.
    /// </summary>
    public static void StartIfEnabled()
    {
        if (IsEnabled)
        {
            TryStart(Debug.LogDirectory);
        }
    }

    /// <summary>
    /// Opens the log in <paramref name="directory"/> and starts sampling. Returns false, leaving the
    /// probe stopped, if the folder or file cannot be written.
    /// </summary>
    internal static bool TryStart(string directory)
    {
        lock (Lock)
        {
            if (_writer is not null)
            {
                return true;
            }

            try
            {
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, $"memory_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv");

                _writer = new StreamWriter(
                    new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read),
                    new UTF8Encoding(false))
                {
                    AutoFlush = true
                };
                _writer.WriteLine(Header);

                _process = Process.GetCurrentProcess();
                _timer = new Timer(static _ => Tick(), null, TimeSpan.Zero, SampleInterval);

                if (Interlocked.Exchange(ref _exitHookInstalled, 1) == 0)
                {
                    AppDomain.CurrentDomain.ProcessExit += static (_, _) => Stop();
                }

                return true;
            }
            catch (Exception)
            {
                StopLocked();
                return false;
            }
        }
    }

    /// <summary>
    /// Writes one row now, tagged with <paramref name="label"/>. Does nothing when the probe is not running.
    /// </summary>
    public static void Mark(string label)
    {
        if (Volatile.Read(ref _writer) is null)
        {
            return;
        }

        WriteSample(label);
    }

    /// <summary>
    /// Stops sampling and closes the log. Safe to call at any time, more than once.
    /// </summary>
    internal static void Stop()
    {
        lock (Lock)
        {
            StopLocked();
        }
    }

    /// <summary>
    /// Records the size of Skia's GPU resource cache. Must be called on the render thread, where the
    /// <see cref="GRContext"/> is current.
    /// </summary>
    public static void RecordGpuCache(GRContext? grContext)
    {
        if (grContext is null)
        {
            return;
        }

        grContext.GetResourceCacheUsage(out _, out long bytes);
        Volatile.Write(ref _gpuCacheBytes, bytes);
    }

    internal static string FormatLine(in MemorySample s)
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        return string.Create(c,
            $"{s.UtcMs},{s.Label},{s.WorkingSet},{s.PrivateBytes},{s.GcHeapSize},{s.GcFragmented},{s.GcCommitted},{s.GcTotalAllocated},{s.SkiaResourceCache},{s.SkiaFontCache},{s.GpuCache},{s.TileCache}");
    }

    private static void Tick() => WriteSample("sample");

    private static void WriteSample(string label)
    {
        lock (Lock)
        {
            if (_writer is null || _process is null)
            {
                return;
            }

            try
            {
                GCMemoryInfo gc = GC.GetGCMemoryInfo(GCKind.Any);
                _process.Refresh();

                var sample = new MemorySample(
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    label,
                    Environment.WorkingSet,
                    _process.PrivateMemorySize64,
                    gc.HeapSizeBytes,
                    gc.FragmentedBytes,
                    gc.TotalCommittedBytes,
                    GC.GetTotalAllocatedBytes(),
                    SKGraphics.GetResourceCacheTotalBytesUsed(),
                    SKGraphics.GetFontCacheUsed(),
                    Volatile.Read(ref _gpuCacheBytes),
                    TileCache.TotalMemoryBytes);

                _writer.WriteLine(FormatLine(sample));
            }
            catch (Exception e)
            {
                StopLocked();
                Debug.WriteExceptionToFile(e);
            }
        }
    }

    private static void StopLocked()
    {
        _timer?.Dispose();
        _timer = null;

        _writer?.Dispose();
        _writer = null;

        _process?.Dispose();
        _process = null;
    }
}
