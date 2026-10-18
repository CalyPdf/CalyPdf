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

using Caly.Rendering.Scene3D;
using Caly.Rendering.Scene3D.Prc;
using Caly.Rendering.Scene3D.U3D;
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
    /// The data was read, but some parts of the model could not be decoded (a compressed mesh whose
    /// arrays do not come out exact, exact geometry that could not be read): shown, the model would
    /// have holes, and a model is shown whole or not at all.
    /// </summary>
    Incomplete,

    /// <summary>The data was read, and describes the model as exact geometry only, without a mesh to draw.</summary>
    NoDisplayMesh
}

/// <summary>
/// Turns the data of 3D artwork into a <see cref="Scene"/>: the one place that knows which reader reads
/// which format.
/// </summary>
public static class Artwork3DSceneReader
{
    /// <summary>Whether there is a reader for artwork of this format: U3D and PRC.</summary>
    public static bool CanRead(Artwork3DFormat format) => format is Artwork3DFormat.U3D or Artwork3DFormat.Prc;

    /// <summary>
    /// Reads a scene. Never throws for bad data; only cancellation is thrown.
    /// </summary>
    /// <param name="scene">The scene when the result is <see cref="Artwork3DReadResult.Ok"/>; the caller owns and disposes it.</param>
    /// <param name="detail">
    /// For <see cref="Artwork3DReadResult.Incomplete"/>, which part could not be shown when the reader knows; for <see cref="Artwork3DReadResult.Malformed"/>, what was wrong; for <see cref="Artwork3DReadResult.NoGeometry"/>,
    /// a sentence for the reader of the document saying why there is nothing to draw, when the model's reader knows.
    /// </param>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public static Artwork3DReadResult TryRead(Artwork3DFormat format, ReadOnlyMemory<byte> payload,
        out Scene? scene, out string? detail, CancellationToken cancellationToken = default)
    {
        scene = null;
        detail = null;
        if (!CanRead(format))
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
                result = Artwork3DReadResult.Incomplete;
                foreach (SceneDiagnostic diagnostic in read.Diagnostics)
                {
                    if (diagnostic.Code is PrcReader.BrepDiagnostic or PrcReader.UncompressedBrepDiagnostic)
                    {
                        detail = "Parts of this 3D model are exact geometry that could not be read.";
                        break;
                    }
                }
            }
            else if (read.Indices.IsEmpty)
            {
                result = Artwork3DReadResult.NoDisplayMesh;
            }
        }
        else if (read.Indices.IsEmpty)
        {
            // Nothing to draw: nodes without geometry, or geometry the reader does not read.
            result = Artwork3DReadResult.NoGeometry;
            detail = WhyNoGeometry(read.Diagnostics);
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
    /// Why a U3D model that was read has nothing to draw, from what its reader said it left out; null when
    /// that says nothing.
    /// </summary>
    public static string? WhyNoGeometry(IReadOnlyList<SceneDiagnostic> diagnostics)
    {
        bool extension = false, progressive = false;
        foreach (SceneDiagnostic diagnostic in diagnostics)
        {
            switch (diagnostic.Code)
            {
                // Blocks of a type the file declares itself that the reader does not know. Right
                // Hemisphere's compressed mesh ("RHAdobeMeshResource", of Acrobat 3D and Deep Exploration
                // exports) is decoded and never ends up here: this is any other vendor's extension.
                case "extension-skipped":
                    extension = true;
                    break;
                // Progressive detail that could not be followed, and no base mesh under it.
                case "mesh-progressive-only":
                    progressive = true;
                    break;
            }
        }

        return extension ? "This 3D model stores its meshes in a vendor extension of U3D that is not supported yet."
            : progressive ? "The progressive detail of this 3D model's meshes could not be read."
            : null;
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
