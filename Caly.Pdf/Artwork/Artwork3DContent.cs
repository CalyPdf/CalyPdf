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
using UglyToad.PdfPig.Artwork;

namespace Caly.Pdf.Artwork;

/// <summary>
/// What a viewer needs of one piece of 3D artwork, read out of the document in one go: nothing in here
/// refers back to the document.
/// </summary>
public sealed class Artwork3DContent
{
    /// <summary>
    /// The 3D data, decoded from its stream. Memory of its own, held by nothing else: a scene read from
    /// it may keep slices of it (texture images).
    /// </summary>
    public required ReadOnlyMemory<byte> Payload { get; init; }

    public required Artwork3DFormat Format { get; init; }

    /// <summary>The authored views, in presentation order.</summary>
    public required IReadOnlyList<SceneView> Views { get; init; }

    /// <summary>The name to show for each of <see cref="Views"/>.</summary>
    public required IReadOnlyList<string> ViewNames { get; init; }

    /// <summary>
    /// The view shown initially, as an index into <see cref="Views"/>; -1 when the document authored no
    /// view at all, and the whole model is to be shown.
    /// </summary>
    public required int DefaultViewIndex { get; init; }
}

/// <summary>
/// The document could not give the data of a piece of 3D artwork (a broken stream, an unreadable filter).
/// </summary>
public sealed class Artwork3DContentException : Exception
{
    public Artwork3DContentException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
