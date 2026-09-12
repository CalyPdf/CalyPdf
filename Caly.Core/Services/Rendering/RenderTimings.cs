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

using System;
using System.IO;
using System.Text;
using System.Threading;
using Caly.Core.Services;

namespace Caly.Core.Services.Rendering;

/// <summary>
/// Opt-in render-path instrumentation, enabled with <c>CALY_RENDER_TIMING=1</c>. Used to compare the
/// GPU and software rendering back-ends (see <c>docs/superpowers/plans/2026-09-11-gpu-rendering.md</c>).
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
    public static readonly bool IsEnabled =
        Environment.GetEnvironmentVariable("CALY_RENDER_TIMING") == "1";

    private static long _drawCount;
    private static long _drawTicks;
    private static long _maxDrawTicks;
    private static long _tilesDrawn;

    private static string? _backend;
    private static int _exitHookInstalled;

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
    /// Records which Skia back-end the render thread actually got. This is the only reliable way to
    /// confirm a requested GPU mode was honoured rather than silently falling back to software.
    /// </summary>
    public static void RecordBackend(string backend)
    {
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
        if (count == 0)
        {
            return;
        }

        long ticks = Interlocked.Read(ref _drawTicks);
        long maxTicks = Interlocked.Read(ref _maxDrawTicks);
        long tiles = Interlocked.Read(ref _tilesDrawn);

        double toMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        var sb = new StringBuilder();
        sb.AppendLine("Caly render timings");
        sb.AppendLine($"Backend        : {_backend ?? "unknown"}");
        sb.AppendLine($"Draw ops       : {count}");
        sb.AppendLine($"Tiles drawn    : {tiles} (avg {tiles / (double)count:F1} per op)");
        sb.AppendLine($"Total draw time: {ticks * toMs:F1} ms");
        sb.AppendLine($"Mean draw time : {ticks * toMs / count:F3} ms");
        sb.AppendLine($"Max draw time  : {maxTicks * toMs:F3} ms");
        sb.AppendLine($"Peak working set: {Environment.WorkingSet / (1024.0 * 1024.0):F0} MB");

        try
        {
            Directory.CreateDirectory(JsonSettingsService.LogFilePath);
            string path = Path.Combine(JsonSettingsService.LogFilePath,
                $"render_timings_{DateTime.UtcNow:yyyyMMdd_HHmmss}.txt");
            File.WriteAllText(path, sb.ToString());
        }
        catch (Exception e)
        {
            Debug.WriteExceptionToFile(e);
        }
    }
}
