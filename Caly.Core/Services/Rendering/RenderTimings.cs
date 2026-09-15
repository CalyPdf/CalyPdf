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

using Avalonia.Skia;
using Caly.Core.Models;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Caly.Core.Services.Rendering;

/// <summary>
/// Opt-in render-path instrumentation, enabled with <c>"Debug": { "LogRenderTimings": true }</c> in the
/// settings file. Used to compare the GPU and software rendering back-ends (see
/// <c>docs/superpowers/plans/2026-09-11-gpu-rendering.md</c>).
/// </summary>
/// <remarks>
/// Samples are accumulated into interlocked counters and written out once at process exit. Nothing is
/// written from the render thread itself: a synchronous file write there would perturb the very
/// measurement being taken, and the draw path is the hot path under test.
/// </remarks>
public static class RenderTimings
{
    /// <summary>
    /// Read once so the disabled case costs a single static field read on the render thread.
    /// </summary>
    /// <remarks>
    /// Initialised on first use - the render thread's first draw - so toggling the setting takes
    /// effect at the next launch, not immediately. The file is read directly rather than through
    /// <c>ISettingsService</c> so this works on every platform head without the service having to
    /// exist yet, at the cost of one small file read per process.
    /// </remarks>
    public static readonly bool IsEnabled =
        JsonSettingsService.TryReadBooleanSetting(nameof(CalySettings.Debug),
            nameof(CalySettings.CalySettingsDebug.LogRenderTimings), out bool enabled)
        && enabled;

    private const int SampleIntervalMs = 250;

    private static long _drawCount;
    private static long _drawTicks;
    private static long _maxDrawTicks;
    private static long _tilesDrawn;

    private static long _tileCacheBytes;
    private static long _peakTileCacheBytes;

    private static long _gpuCacheBytes;
    private static long _peakGpuCacheBytes;
    private static int _gpuCacheCount;
    private static int _peakGpuCacheCount;
    private static long _gpuCacheLimit;

    private static string? _backend;
    private static int _exitHookInstalled;

    private static readonly List<MemorySample> _samples = new();
    private static volatile bool _stopSampler;
    private static Thread? _sampler;

    private readonly record struct MemorySample(
        long ElapsedMs,
        long WorkingSet,
        long PrivateBytes,
        long ManagedHeap,
        long TileCacheBytes,
        long GpuCacheBytes,
        int GpuCacheCount,
        long DrawCount);

    /// <summary>
    /// Records one completed tile draw operation. <paramref name="ticks"/> is a
    /// <see cref="System.Diagnostics.Stopwatch"/> tick count.
    /// </summary>
    public static void RecordDraw(long ticks, int tiles)
    {
        Interlocked.Increment(ref _drawCount);
        Interlocked.Add(ref _drawTicks, ticks);
        Interlocked.Add(ref _tilesDrawn, tiles);
        UpdateMax(ref _maxDrawTicks, ticks);
    }

    /// <summary>
    /// Records the size of Skia's GPU resource cache - the textures tiles were uploaded into. Must be
    /// called on the render thread, where the context is current.
    /// </summary>
    public static void RecordGpuCache(GRContext? grContext)
    {
        if (grContext is null)
        {
            return;
        }

        grContext.GetResourceCacheUsage(out int count, out long bytes);

        Volatile.Write(ref _gpuCacheBytes, bytes);
        Volatile.Write(ref _gpuCacheCount, count);
        UpdateMax(ref _peakGpuCacheBytes, bytes);
        UpdateMax(ref _peakGpuCacheCount, count);

        if (Volatile.Read(ref _gpuCacheLimit) == 0)
        {
            Volatile.Write(ref _gpuCacheLimit, grContext.GetResourceCacheLimit());
        }
    }

    /// <summary>
    /// Tracks host-side tile memory across every <see cref="TileCache"/> in the process.
    /// </summary>
    public static void AddTileCacheBytes(long delta)
    {
        if (!IsEnabled || delta == 0)
        {
            return;
        }

        UpdateMax(ref _peakTileCacheBytes, Interlocked.Add(ref _tileCacheBytes, delta));
    }

    /// <summary>
    /// Records which Skia back-end the render thread actually got. This is the only reliable way to
    /// confirm a requested GPU mode was honoured rather than silently falling back to software.
    /// </summary>
    public static void RecordBackend(ISkiaSharpApiLease? lease)
    {
        string backend = "Software";
        if (lease?.GrContext is not null)
        {
            // GrContext is null under the software renderer, so this is what actually proves
            // whether a requested GPU mode was honoured or silently fell back.
            backend = $"GPU ({lease!.GrContext.Backend})";
        }

        Interlocked.CompareExchange(ref _backend, backend, null);
        EnsureExitHook();
    }

    private static void UpdateMax(ref long target, long value)
    {
        long observed = Interlocked.Read(ref target);
        while (value > observed)
        {
            long actual = Interlocked.CompareExchange(ref target, value, observed);
            if (actual == observed)
            {
                break;
            }

            observed = actual;
        }
    }

    private static void UpdateMax(ref int target, int value)
    {
        int observed = Volatile.Read(ref target);
        while (value > observed)
        {
            int actual = Interlocked.CompareExchange(ref target, value, observed);
            if (actual == observed)
            {
                break;
            }

            observed = actual;
        }
    }

    private static void EnsureExitHook()
    {
        if (Interlocked.Exchange(ref _exitHookInstalled, 1) == 0)
        {
            AppDomain.CurrentDomain.ProcessExit += static (_, _) => Dump();

            _sampler = new Thread(SampleMemory)
            {
                IsBackground = true,
                Name = "Caly render memory sampler",
                Priority = ThreadPriority.BelowNormal
            };
            _sampler.Start();
        }
    }

    /// <summary>
    /// Samples process memory on its own thread so the render thread never pays for it.
    /// </summary>
    private static void SampleMemory()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        long start = System.Diagnostics.Stopwatch.GetTimestamp();

        while (!_stopSampler)
        {
            process.Refresh();

            var sample = new MemorySample(
                (long)System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds,
                process.WorkingSet64,
                process.PrivateMemorySize64,
                GC.GetTotalMemory(false),
                Interlocked.Read(ref _tileCacheBytes),
                Interlocked.Read(ref _gpuCacheBytes),
                Volatile.Read(ref _gpuCacheCount),
                Interlocked.Read(ref _drawCount));

            lock (_samples)
            {
                _samples.Add(sample);
            }

            Thread.Sleep(SampleIntervalMs);
        }
    }

    private static string Mb(long bytes) => (bytes / (1024.0 * 1024.0)).ToString("F0", CultureInfo.InvariantCulture);

    /// <summary>
    /// Writes the accumulated summary next to the crash logs. Safe to call more than once.
    /// </summary>
    public static void Dump()
    {
        long count = Interlocked.Read(ref _drawCount);
        if (count == 0)
        {
            return;
        }

        _stopSampler = true;
        _sampler?.Join(SampleIntervalMs * 2);

        MemorySample[] samples;
        lock (_samples)
        {
            samples = _samples.ToArray();
        }

        long peakWs = 0, peakPrivate = 0, peakManaged = 0;
        foreach (var s in samples)
        {
            peakWs = Math.Max(peakWs, s.WorkingSet);
            peakPrivate = Math.Max(peakPrivate, s.PrivateBytes);
            peakManaged = Math.Max(peakManaged, s.ManagedHeap);
        }

        long osPeakWs;
        using (var process = System.Diagnostics.Process.GetCurrentProcess())
        {
            osPeakWs = process.PeakWorkingSet64;
        }

        long ticks = Interlocked.Read(ref _drawTicks);
        long maxTicks = Interlocked.Read(ref _maxDrawTicks);
        long tiles = Interlocked.Read(ref _tilesDrawn);

        double toMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        var sb = new StringBuilder();
        sb.AppendLine("Caly render timings");
        sb.AppendLine($"Backend             : {_backend ?? "unknown"}");
        sb.AppendLine($"Draw ops            : {count}");
        sb.AppendLine($"Tiles drawn         : {tiles} (avg {tiles / (double)count:F1} per op)");
        sb.AppendLine($"Total draw time     : {ticks * toMs:F1} ms");
        sb.AppendLine($"Mean draw time      : {ticks * toMs / count:F3} ms");
        sb.AppendLine($"Max draw time       : {maxTicks * toMs:F3} ms");
        sb.AppendLine($"Peak working set    : {Mb(osPeakWs)} MB (OS peak; sampled {Mb(peakWs)} MB)");
        sb.AppendLine($"Peak private bytes  : {Mb(peakPrivate)} MB (sampled)");
        sb.AppendLine($"Peak managed heap   : {Mb(peakManaged)} MB (sampled)");
        sb.AppendLine($"Peak tile cache     : {Mb(Interlocked.Read(ref _peakTileCacheBytes))} MB (all documents)");
        sb.AppendLine($"Peak GPU cache      : {Mb(Interlocked.Read(ref _peakGpuCacheBytes))} MB, {Volatile.Read(ref _peakGpuCacheCount)} resources (limit {Mb(Volatile.Read(ref _gpuCacheLimit))} MB)");

        try
        {
            Directory.CreateDirectory(JsonSettingsService.LogFilePath);
            string stamp = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}";

            File.WriteAllText(Path.Combine(JsonSettingsService.LogFilePath, $"render_timings_{stamp}.txt"), sb.ToString());

            var csv = new StringBuilder("elapsed_ms,working_set_mb,private_mb,managed_mb,tile_cache_mb,gpu_cache_mb,gpu_cache_count,draw_ops");
            csv.AppendLine();
            foreach (var s in samples)
            {
                csv.Append(s.ElapsedMs).Append(',')
                    .Append(Mb(s.WorkingSet)).Append(',')
                    .Append(Mb(s.PrivateBytes)).Append(',')
                    .Append(Mb(s.ManagedHeap)).Append(',')
                    .Append(Mb(s.TileCacheBytes)).Append(',')
                    .Append(Mb(s.GpuCacheBytes)).Append(',')
                    .Append(s.GpuCacheCount).Append(',')
                    .Append(s.DrawCount).AppendLine();
            }

            File.WriteAllText(Path.Combine(JsonSettingsService.LogFilePath, $"render_memory_{stamp}.csv"), csv.ToString());
        }
        catch (Exception e)
        {
            Debug.WriteExceptionToFile(e);
        }
    }
}
