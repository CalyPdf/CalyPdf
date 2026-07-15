// Copyright (c) 2025 BobLd
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
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace Caly.Core.Utilities;

internal static class Helpers
{
    // https://stackoverflow.com/questions/14488796/does-net-provide-an-easy-way-convert-bytes-to-kb-mb-gb-etc
    private static readonly string[] SizeSuffixes = ["bytes", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB"];

    /// <summary>
    /// Format byte count, e.g. 15.8 MB.
    /// </summary>
    public static string FormatSizeBytes(long byteCount, int decimalPlaces = 1)
    {
        if (decimalPlaces < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(decimalPlaces), "Must be positive.");
        }

        if (byteCount < 0)
        {
            return "-" + FormatSizeBytes(-byteCount, decimalPlaces);
        }

        if (byteCount == 0)
        {
            return string.Format("{0:n" + decimalPlaces + "} bytes", 0);
        }

        // mag is 0 for bytes, 1 for KB, 2, for MB, etc.
        int mag = (int)Math.Log(byteCount, 1024);

        // 1L << (mag * 10) == 2 ^ (10 * mag) 
        // [i.e. the number of bytes in the unit corresponding to mag]
        decimal adjustedSize = (decimal)byteCount / (1L << (mag * 10));

        // make adjustment when the value is large enough that
        // it would round up to 1000 or more
        if (Math.Round(adjustedSize, decimalPlaces) >= 1000)
        {
            mag += 1;
            adjustedSize /= 1024;
        }

        return string.Format("{0:n" + decimalPlaces + "} {1}", adjustedSize, SizeSuffixes[mag]);
    }

    #region GC request
    private static readonly Lock GcLock = new();
    private static readonly Timer GcDebounceTimer = new(GcTimerCallback);
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(10);
    private static long _gcFirstRequestTimestamp = -1;
    private static bool _gcScheduledMaxWait;
    
    public static void RequestGcCollect()
    {
        lock (GcLock)
        {
            var now = Stopwatch.GetTimestamp();
            if (_gcFirstRequestTimestamp == -1)
            {
                _gcFirstRequestTimestamp = now;
            }

            var elapsed = Stopwatch.GetElapsedTime(_gcFirstRequestTimestamp, now);
            if (elapsed >= MaxWait)
            {
                if (_gcScheduledMaxWait)
                {
                    return;
                }

                // Worst case will run at MaxWait + DebounceWindow
                _gcScheduledMaxWait = true;
                GcDebounceTimer.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan);
            }
            else if (!_gcScheduledMaxWait)
            {
                GcDebounceTimer.Change(DebounceWindow, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private static void GcTimerCallback(object? state)
    {
        // Timer.Change doesn't cancel a callback that has already fired and is queued.
        // A request landing in that gap re-arms the timer, but the queued callback still runs,
        // so occasionally get one collect slightly early plus one extra collect ~2s later.
        lock (GcLock)
        {
            _gcScheduledMaxWait = false;
            _gcFirstRequestTimestamp = -1;
        }

        System.Diagnostics.Debug.WriteLine($"-> RequestGcCollect({DateTime.UtcNow})");
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }
    #endregion

    public static string? SanitiseFileName(string? fileName, char? substitute = '_')
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return fileName;
        }

        char[] invalidChars = Path.GetInvalidFileNameChars();

        if (fileName.IndexOfAny(invalidChars) == -1)
        {
            return fileName;
        }

        if (substitute.HasValue && substitute.Value != '_')
        {
            // If the substitute character is not '_', we need to check if it's valid and not in the invalid chars list.
            if (Array.IndexOf(invalidChars, substitute.Value) != -1)
            {
                throw new ArgumentException($"Substitute character '{substitute.Value}' is invalid for file names.", nameof(substitute));
            }
        }

        var builder = new StringBuilder(fileName.Length);
        foreach (char c in fileName)
        {
            if (Array.IndexOf(invalidChars, c) == -1)
            {
                builder.Append(c);
            }
            else if (substitute.HasValue)
            {
                builder.Append(substitute.Value);
            }
        }

        return builder.ToString();
    }
}
