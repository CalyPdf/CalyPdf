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

using System.Diagnostics;
using Caly.Core.Services;

namespace Caly.Tests;

/// <summary>
/// Exercises <see cref="AudioPlaybackService"/> without relying on a real audio device (CI/sandbox have
/// none). The clip is never audible here; the assertions verify the service is robust and hygienic -
/// it never throws and always cleans up its temp files, whether playback succeeds or fails to start.
/// Audibility itself is verified manually in the running app.
/// </summary>
public class AudioPlaybackServiceTests
{
    private const string TempPattern = "caly_audio_*.wav";

    private static int TempClipCount() =>
        Directory.EnumerateFiles(Path.GetTempPath(), TempPattern).Count();

    private static bool WaitFor(Func<bool> condition, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(50);
        }

        return condition();
    }

    private static byte[] CreateWav(double seconds)
    {
        const int rate = 44100;
        int samples = (int)(rate * seconds);
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int dataLen = samples * 2;
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + dataLen);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);   // PCM
        w.Write((short)1);   // mono
        w.Write(rate);
        w.Write(rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8.ToArray());
        w.Write(dataLen);
        for (int i = 0; i < samples; i++)
        {
            w.Write((short)(Math.Sin(2 * Math.PI * 440 * i / rate) * 12000));
        }

        w.Flush();
        return ms.ToArray();
    }

    [Fact]
    public void Play_with_empty_data_is_a_noop()
    {
        int baseline = TempClipCount();

        using var service = new AudioPlaybackService();
        service.Play(ReadOnlyMemory<byte>.Empty, ".wav");

        Assert.Equal(baseline, TempClipCount());
    }

    [Fact]
    public void Play_then_cleans_up_its_temp_file()
    {
        int baseline = TempClipCount();

        using var service = new AudioPlaybackService();
        service.Play(CreateWav(1.0), ".wav");

        // Whether the device plays the clip or playback fails to start (no audio device), the service
        // must not leave its temp file behind.
        Assert.True(WaitFor(() => TempClipCount() <= baseline, 8000),
            "Expected the temp clip file to be cleaned up.");
    }

    [Fact]
    public void Stop_cleans_up_and_does_not_throw()
    {
        int baseline = TempClipCount();

        using var service = new AudioPlaybackService();
        service.Play(CreateWav(5.0), ".wav");
        service.Stop();
        service.Stop(); // extra Stop must be harmless

        Assert.True(WaitFor(() => TempClipCount() <= baseline, 5000),
            "Expected Stop() to clean up the temp clip file.");
    }

    [Fact]
    public void Dispose_is_idempotent_and_cleans_up()
    {
        int baseline = TempClipCount();

        var service = new AudioPlaybackService();
        service.Play(CreateWav(5.0), ".wav");

        service.Dispose();
        service.Dispose(); // must not throw

        Assert.True(WaitFor(() => TempClipCount() <= baseline, 5000),
            "Expected Dispose() to clean up the temp clip file.");
    }
}
