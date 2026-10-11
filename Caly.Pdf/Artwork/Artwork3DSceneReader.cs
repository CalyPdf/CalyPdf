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
using Scene3D.Prc;
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
    NoGeometry,

    /// <summary>
    /// The data was read, but parts of the model are stored in a form the reader does not turn into
    /// geometry yet (PRC's compressed tessellation): shown, the model would have holes.
    /// </summary>
    NotYetSupported,

    /// <summary>The data was read, and describes the model as exact geometry only, without a mesh to draw.</summary>
    NoDisplayMesh
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
        if (format != Artwork3DFormat.U3D && format != Artwork3DFormat.Prc)
        {
            return Artwork3DReadResult.UnsupportedFormat;
        }

        Scene read;
        try
        {
            read = format == Artwork3DFormat.Prc
                ? PrcReader.Read(payload, null, cancellationToken)
                : U3DReader.Read(payload, null, cancellationToken);
        }
        catch (Scene3DException e)
        {
            detail = e.Message;
            return Artwork3DReadResult.Malformed;
        }

        Artwork3DReadResult result = Artwork3DReadResult.Ok;
        if (format == Artwork3DFormat.Prc)
        {
            // A model is shown whole or not at all: half an assembly misleads more than a poster.
            if (!PrcReader.IsComplete(read))
            {
                result = Artwork3DReadResult.NotYetSupported;
            }
            else if (read.Indices.IsEmpty)
            {
                result = Artwork3DReadResult.NoDisplayMesh;
            }
        }
        else if (read.Indices.IsEmpty)
        {
            // Files whose meshes exist only as progressive updates decode to a tree of empty nodes.
            result = Artwork3DReadResult.NoGeometry;
        }

        if (result != Artwork3DReadResult.Ok)
        {
            read.Dispose();
            return result;
        }

        scene = read;
        return Artwork3DReadResult.Ok;
    }

    /// <summary>
    /// The views of the artwork with the nodes they name called as the scene calls them. A view shows,
    /// hides and moves parts of the model by the names of their nodes; for PRC a name ends in an
    /// identifier of the part, and the text before it may differ between the viewer that wrote the view
    /// and the reader that built the scene. U3D names are the file's own and need nothing.
    /// </summary>
    /// <returns><paramref name="views"/> itself when nothing had to change.</returns>
    public static IReadOnlyList<SceneView> ResolveViews(Artwork3DFormat format, Scene scene, IReadOnlyList<SceneView> views)
    {
        if (format != Artwork3DFormat.Prc)
        {
            return views;
        }

        SceneView[]? resolved = null;
        for (int i = 0; i < views.Count; i++)
        {
            SceneView view = PrcNodeNames.Resolve(scene, views[i]);
            if (!ReferenceEquals(view, views[i]))
            {
                resolved ??= [.. views];
                resolved[i] = view;
            }
        }

        return resolved ?? views;
    }
}
