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

using Scene3D;
using Scene3D.U3D;
using UglyToad.PdfPig.Artwork;

namespace Caly.Pdf.Artwork;

/// <summary>How reading a scene out of 3D data went.</summary>
public enum Artwork3DReadResult : byte
{
    Ok,

    /// <summary>No reader for this format.</summary>
    UnsupportedFormat,

    /// <summary>The data is not what its format says, or exceeds the reader's limits.</summary>
    Malformed,

    /// <summary>The data was read, and holds nothing that can be drawn.</summary>
    NoGeometry
}

/// <summary>
/// Turns the data of 3D artwork into a <see cref="Scene"/>: the one place that knows which reader reads
/// which format.
/// </summary>
public static class Artwork3DSceneReader
{
    /// <summary>
    /// Reads a scene. Never throws for bad data; only cancellation is thrown.
    /// </summary>
    /// <param name="scene">The scene when the result is <see cref="Artwork3DReadResult.Ok"/>; the caller owns and disposes it.</param>
    /// <param name="detail">For <see cref="Artwork3DReadResult.Malformed"/>, what was wrong.</param>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public static Artwork3DReadResult TryRead(Artwork3DFormat format, ReadOnlyMemory<byte> payload,
        out Scene? scene, out string? detail, CancellationToken cancellationToken = default)
    {
        scene = null;
        detail = null;
        if (format != Artwork3DFormat.U3D)
        {
            return Artwork3DReadResult.UnsupportedFormat;
        }

        Scene read;
        try
        {
            read = U3DReader.Read(payload, null, cancellationToken);
        }
        catch (Scene3DException e)
        {
            detail = e.Message;
            return Artwork3DReadResult.Malformed;
        }

        // Files whose meshes exist only as progressive updates decode to a tree of empty nodes.
        if (read.Indices.IsEmpty)
        {
            read.Dispose();
            return Artwork3DReadResult.NoGeometry;
        }

        scene = read;
        return Artwork3DReadResult.Ok;
    }
}
