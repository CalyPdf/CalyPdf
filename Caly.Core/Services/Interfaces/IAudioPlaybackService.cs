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

namespace Caly.Core.Services.Interfaces;

/// <summary>
/// Plays short audio clips, such as the embedded sounds of a PDF rendition action.
/// Only one clip plays at a time; starting a new clip stops the previous one.
/// </summary>
public interface IAudioPlaybackService : IDisposable
{
    /// <summary>
    /// Plays the given encoded audio bytes (e.g. the contents of an <c>.mp3</c> file). Any clip
    /// currently playing is stopped first. Playback is asynchronous; this method returns immediately.
    /// </summary>
    /// <param name="data">The encoded audio file bytes.</param>
    /// <param name="fileExtension">
    /// The file extension including the leading dot (e.g. <c>.mp3</c>), used to hint the OS audio
    /// facility at the format. May be empty.
    /// </param>
    void Play(ReadOnlyMemory<byte> data, string fileExtension);

    /// <summary>
    /// Stops the clip currently playing, if any.
    /// </summary>
    void Stop();
}
