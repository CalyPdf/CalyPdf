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

    private static long _drawCount;
    private static long _drawTicks;
    private static long _maxDrawTicks;
    private static long _tilesDrawn;

    private static long _peakGpuCacheBytes;
    private static int _peakGpuCacheCount;

    private static string? _backend;
    private static int _exitHookInstalled;

    // 3D views: time from a view being built to its first frame with a model, and frame times.
    private static long _view3DShown;
    private static long _view3DShownTicks;
    private static long _view3DMaxShownTicks;
    private static long _view3DFrames;
    private static double _view3DFrameMs;
    private static double _view3DMaxFrameMs;
    private static readonly object View3DLock = new();

    /// <summary>
    /// Records one completed tile draw operation. <paramref name="ticks"/> is a
    /// <see cref="System.Diagnostics.Stopwatch"/> tick count.
    /// </summary>
    public static void RecordDraw(long ticks, int tiles)
    {
        Interlocked.Increment(ref _drawCount);
        Interlocked.Add(ref _drawTicks, ticks);
        Interlocked.Add(ref _tilesDrawn, tiles);

        long observed = Interlocked.Read(ref _maxDrawTicks);
        while (ticks > observed)
        {
            long actual = Interlocked.CompareExchange(ref _maxDrawTicks, ticks, observed);
            if (actual == observed)
            {
                break;
            }

            observed = actual;
        }
    }

    /// <summary>
    /// Records the size of Skia's GPU resource cache - the textures tiles are uploaded into. Must be
    /// called on the render thread, where the <see cref="GRContext"/> is current.
    /// </summary>
    public static void RecordGpuCache(GRContext? grContext)
    {
        if (grContext is null)
        {
            return;
        }

        grContext.GetResourceCacheUsage(out int count, out long bytes);
        UpdateMax(ref _peakGpuCacheBytes, bytes);

        int observedCount = Volatile.Read(ref _peakGpuCacheCount);
        while (count > observedCount)
        {
            int actual = Interlocked.CompareExchange(ref _peakGpuCacheCount, count, observedCount);
            if (actual == observedCount)
            {
                break;
            }

            observedCount = actual;
        }
    }

    /// <summary>
    /// Records how long a 3D view took from being put on its page to drawing its model.
    /// <paramref name="ticks"/> is a <see cref="System.Diagnostics.Stopwatch"/> tick count.
    /// </summary>
    public static void RecordView3DShown(long ticks)
    {
        Interlocked.Increment(ref _view3DShown);
        Interlocked.Add(ref _view3DShownTicks, ticks);
        UpdateMax(ref _view3DMaxShownTicks, ticks);
        EnsureExitHook();
    }

    /// <summary>
    /// Records one frame of a 3D view, in milliseconds as the 3D control measures it.
    /// </summary>
    public static void RecordView3DFrame(double milliseconds)
    {
        lock (View3DLock)
        {
            _view3DFrames++;
            _view3DFrameMs += milliseconds;
            _view3DMaxFrameMs = Math.Max(_view3DMaxFrameMs, milliseconds);
        }
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

    private static void EnsureExitHook()
    {
        if (Interlocked.Exchange(ref _exitHookInstalled, 1) == 0)
        {
            AppDomain.CurrentDomain.ProcessExit += static (_, _) => Dump();
        }
    }

    /// <summary>
    /// Writes the accumulated summary next to the crash logs. Safe to call more than once.
    /// </summary>
    public static void Dump()
    {
        long count = Interlocked.Read(ref _drawCount);
        long shown3D = Interlocked.Read(ref _view3DShown);
        if (count == 0 && shown3D == 0)
        {
            return;
        }

        // The averages below divide by this; the count itself is printed as it is (0 in a session of
        // 3D views only).
        long divisor = Math.Max(count, 1);

        long ticks = Interlocked.Read(ref _drawTicks);
        long maxTicks = Interlocked.Read(ref _maxDrawTicks);
        long tiles = Interlocked.Read(ref _tilesDrawn);

        double toMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        var sb = new StringBuilder();
        sb.AppendLine("Caly render timings");
        sb.AppendLine($"Backend        : {_backend ?? "unknown"}");
        sb.AppendLine($"Draw ops       : {count}");
        sb.AppendLine($"Tiles drawn    : {tiles} (avg {tiles / (double)divisor:F1} per op)");
        sb.AppendLine($"Total draw time: {ticks * toMs:F1} ms");
        sb.AppendLine($"Mean draw time : {ticks * toMs / divisor:F3} ms");
        sb.AppendLine($"Max draw time  : {maxTicks * toMs:F3} ms");
        sb.AppendLine($"Peak working set: {Environment.WorkingSet / (1024.0 * 1024.0):F0} MB");
        sb.AppendLine($"Peak GPU cache : {Interlocked.Read(ref _peakGpuCacheBytes) / (1024.0 * 1024.0):F1} MB, {Volatile.Read(ref _peakGpuCacheCount)} resources");

        if (shown3D > 0)
        {
            lock (View3DLock)
            {
                sb.AppendLine($"3D views shown : {shown3D} (view built to model drawn: mean {Interlocked.Read(ref _view3DShownTicks) * toMs / shown3D:F0} ms, max {Interlocked.Read(ref _view3DMaxShownTicks) * toMs:F0} ms)");
                sb.AppendLine($"3D frames      : {_view3DFrames} (mean {(_view3DFrames > 0 ? _view3DFrameMs / _view3DFrames : 0):F2} ms, max {_view3DMaxFrameMs:F2} ms)");
            }
        }

        try
        {
            Directory.CreateDirectory(Debug.LogDirectory);
            string path = Path.Combine(Debug.LogDirectory,
                $"render_timings_{DateTime.UtcNow:yyyyMMdd_HHmmss}.txt");
            File.WriteAllText(path, sb.ToString());
        }
        catch (Exception e)
        {
            Debug.WriteExceptionToFile(e);
        }
    }
}
