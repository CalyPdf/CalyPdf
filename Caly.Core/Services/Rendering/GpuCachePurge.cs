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
using System.Threading;

namespace Caly.Core.Services.Rendering;

/// <summary>
/// A request, made when a document has closed, to empty Skia's GPU resource cache of the textures nobody
/// uses any more. The cache only shrinks under pressure, so without this the textures of closed
/// documents sit in memory until something else needs the room.
/// </summary>
/// <remarks>
/// The <c>GRContext</c> belongs to the render thread, so the request is only a flag: whoever has a
/// lease on it during a frame (<c>GpuCachePurgeHost</c>) takes the flag with <see cref="TryConsume"/>
/// and purges. Requests made before that frame coalesce into one purge. The GPU cache is process-wide
/// (one compositor, one <c>GRContext</c>), so one consume serves every window.
/// </remarks>
internal static class GpuCachePurge
{
    private static int _pending;

    /// <summary>
    /// Raised, on the requesting thread, after every <see cref="Request"/>, so a host can schedule a frame.
    /// </summary>
    public static event Action? PurgeRequested;

    public static void Request()
    {
        Interlocked.Exchange(ref _pending, 1);
        PurgeRequested?.Invoke();
    }

    /// <summary>
    /// True once for each batch of requests: takes the flag and clears it.
    /// </summary>
    public static bool TryConsume() => Interlocked.Exchange(ref _pending, 0) == 1;
}
