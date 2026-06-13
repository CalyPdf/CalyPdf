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
using System.Runtime.Versioning;
using System.Threading;
using Caly.Core.Services.Interfaces;
using NAudio.Wave;

namespace Caly.Core.Services;

/// <summary>
/// Plays short audio clips, such as the embedded sounds of a PDF rendition action. One clip plays at a
/// time; starting a new clip stops the previous one.
/// <para>
/// None of the playback facilities used here accept an in-memory encoded stream, so each clip is first
/// written to a temporary file, then played from there and the file deleted when playback ends.
/// </para>
/// <list type="bullet">
///   <item><description><b>Windows</b>: NAudio's <see cref="MediaFoundationReader"/> (Media Foundation)
///   decodes the clip - including MP3 - and a <see cref="WaveOutEvent"/> plays it. Media Foundation is
///   used because the legacy MCI device is unreliable/absent on modern Windows.</description></item>
///   <item><description><b>macOS</b>: shells out to <c>afplay</c>, which ships with the OS.</description></item>
///   <item><description><b>Linux</b>: there is no audio facility in the base OS, so it probes for a
///   common command-line player (<c>ffplay</c>, <c>mpg123</c>, …).</description></item>
/// </list>
/// </summary>
public sealed class AudioPlaybackService : IAudioPlaybackService
{
    private readonly object _lock = new();
    private IAudioBackend? _backend;
    private bool _disposed;

    /// <inheritdoc/>
    public void Play(ReadOnlyMemory<byte> data, string fileExtension)
    {
        if (data.IsEmpty)
        {
            return;
        }

        string tempPath;
        try
        {
            string ext = string.IsNullOrEmpty(fileExtension) ? ".tmp" : fileExtension;
            if (ext[0] != '.')
            {
                ext = "." + ext;
            }

            tempPath = Path.Combine(Path.GetTempPath(), $"caly_audio_{Guid.NewGuid():N}{ext}");
            File.WriteAllBytes(tempPath, data.ToArray());
        }
        catch (Exception ex)
        {
            Debug.WriteExceptionToFile(ex);
            return;
        }

        lock (_lock)
        {
            if (_disposed)
            {
                TryDelete(tempPath);
                return;
            }

            try
            {
                _backend ??= CreateBackend();
                if (_backend is null)
                {
                    TryDelete(tempPath);
                    return;
                }

                _backend.Play(tempPath);
            }
            catch (Exception ex)
            {
                Debug.WriteExceptionToFile(ex);
                TryDelete(tempPath);
            }
        }
    }

    /// <inheritdoc/>
    public void Stop()
    {
        lock (_lock)
        {
            _backend?.Stop();
        }
    }

    public void Dispose()
    {
        IAudioBackend? backend;
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            backend = _backend;
            _backend = null;
        }

        backend?.Dispose();
    }

    private static IAudioBackend CreateBackend()
    {
        if (OperatingSystem.IsWindows())
        {
            return new MediaFoundationBackend();
        }

        // macOS and Linux both play via an external process (afplay / ffplay / mpg123 / …).
        return new ProcessAudioBackend();
    }

    private static void TryDelete(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
            // Best effort. A leftover temp file is harmless; the OS clears the temp directory eventually.
        }
    }

    /// <summary>
    /// A platform audio backend. Owns the temp files it is given and deletes them when their playback
    /// ends (naturally, on replacement, or on <see cref="Stop"/>/<see cref="IDisposable.Dispose"/>).
    /// One clip plays at a time.
    /// </summary>
    private interface IAudioBackend : IDisposable
    {
        void Play(string filePath);

        void Stop();
    }

    /// <summary>
    /// Windows backend using NAudio: Media Foundation decodes the clip and <see cref="WaveOutEvent"/>
    /// renders it. Cleanup happens on natural completion (<see cref="IWavePlayer.PlaybackStopped"/>),
    /// on replacement, on <see cref="Stop"/>, or on <see cref="Dispose"/>.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private sealed class MediaFoundationBackend : IAudioBackend
    {
        private readonly Lock _sync = new();
        private IWavePlayer? _output;
        private MediaFoundationReader? _reader;
        private string? _tempPath;
        private bool _disposed;

        public void Play(string filePath)
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    TryDelete(filePath);
                    return;
                }

                StopCurrentNoLock();

                try
                {
                    var reader = new MediaFoundationReader(filePath);
                    var output = new WaveOutEvent();
                    output.PlaybackStopped += OnPlaybackStopped;
                    output.Init(reader);
                    output.Play();

                    _reader = reader;
                    _output = output;
                    _tempPath = filePath;
                }
                catch (Exception ex)
                {
                    Debug.WriteExceptionToFile(ex);
                    CleanUpCurrentNoLock();
                    TryDelete(filePath);
                }
            }
        }

        public void Stop()
        {
            lock (_sync)
            {
                StopCurrentNoLock();
            }
        }

        private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            // Natural completion (or an error mid-playback). Runs on NAudio's playback thread.
            lock (_sync)
            {
                if (!ReferenceEquals(sender, _output))
                {
                    return; // A newer clip has taken over; nothing to do.
                }

                CleanUpCurrentNoLock();
            }
        }

        private void StopCurrentNoLock()
        {
            if (_output is not null)
            {
                // Unsubscribe first so Stop() does not re-enter cleanup via PlaybackStopped.
                _output.PlaybackStopped -= OnPlaybackStopped;
                try
                {
                    _output.Stop();
                }
                catch (Exception ex)
                {
                    Debug.WriteExceptionToFile(ex);
                }
            }

            CleanUpCurrentNoLock();
        }

        private void CleanUpCurrentNoLock()
        {
            try
            {
                _output?.Dispose();
            }
            catch
            {
                // Ignore.
            }

            try
            {
                _reader?.Dispose();
            }
            catch
            {
                // Ignore.
            }

            _output = null;
            _reader = null;

            if (_tempPath is not null)
            {
                TryDelete(_tempPath);
                _tempPath = null;
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                StopCurrentNoLock();
            }
        }
    }

    /// <summary>
    /// macOS / Linux backend that launches an external command-line player and tracks its process.
    /// Killing the process stops playback; its exit deletes the temp file.
    /// </summary>
    private sealed class ProcessAudioBackend : IAudioBackend
    {
        private readonly object _sync = new();
        private Process? _process;
        private string? _tempPath;
        private bool _disposed;

        public void Play(string filePath)
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    TryDelete(filePath);
                    return;
                }

                StopCurrentNoLock();

                foreach ((string exe, string args) in GetCandidatePlayers("\"" + filePath + "\""))
                {
                    Process? process = null;
                    try
                    {
                        process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = exe,
                                Arguments = args,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            },
                            EnableRaisingEvents = true
                        };

                        process.Exited += (_, _) => OnExited(process);

                        if (process.Start())
                        {
                            _process = process;
                            _tempPath = filePath;
                            return;
                        }
                    }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
                    {
                        // Player not installed - try the next candidate.
                        process?.Dispose();
                    }
                }

                // No player available.
                TryDelete(filePath);
            }
        }

        public void Stop()
        {
            lock (_sync)
            {
                StopCurrentNoLock();
            }
        }

        private void OnExited(Process process)
        {
            lock (_sync)
            {
                if (!ReferenceEquals(process, _process))
                {
                    return;
                }

                CleanUpCurrentNoLock();
            }
        }

        private void StopCurrentNoLock()
        {
            if (_process is null)
            {
                return;
            }

            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteExceptionToFile(ex);
            }

            CleanUpCurrentNoLock();
        }

        private void CleanUpCurrentNoLock()
        {
            try
            {
                _process?.Dispose();
            }
            catch
            {
                // Ignore.
            }

            _process = null;

            if (_tempPath is not null)
            {
                TryDelete(_tempPath);
                _tempPath = null;
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                StopCurrentNoLock();
            }
        }

        private static (string Exe, string Args)[] GetCandidatePlayers(string quotedPath)
        {
            if (OperatingSystem.IsMacOS())
            {
                // afplay ships with macOS and plays mp3/m4a/aac/wav natively.
                return [("afplay", quotedPath)];
            }

            // Linux: no player ships in the base OS, so probe common ones in order of preference.
            // ffplay/mpg123 decode mp3; paplay/aplay are PCM/WAV only and act as last resorts.
            return
            [
                ("ffplay", "-nodisp -autoexit -loglevel quiet " + quotedPath),
                ("mpg123", "-q " + quotedPath),
                ("paplay", quotedPath),
                ("aplay", quotedPath)
            ];
        }
    }
}
