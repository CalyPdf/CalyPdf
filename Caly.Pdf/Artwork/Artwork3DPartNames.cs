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
using UglyToad.PdfPig.Artwork;

namespace Caly.Pdf.Artwork;

/// <summary>What the status bar says of the selected part of a 3D model.</summary>
public static class Artwork3DPartNames
{
    /// <summary>What PRC calls a product occurrence that has no name.</summary>
    private const string PrcUnnamedOccurrence = "node";

    /// <summary>
    /// <c>3D part: &lt;name&gt;</c> for a node of that name. A PRC node's name ends in ".&lt;identifier&gt;"
    /// (the PRC entity it stands for), which is left out; a name of nothing is "Unnamed part".
    /// </summary>
    public static string Status(string? nodeName, Artwork3DFormat format) => Text(DisplayName(nodeName, format));

    /// <summary>
    /// <see cref="Status(string?, Artwork3DFormat)"/> for a node of <paramref name="scene"/>, named after the
    /// nearest node from it up that has a name: PRC writers name parts, and the items of geometry in them
    /// carry only an identifier.
    /// </summary>
    public static string Status(Scene scene, int node, Artwork3DFormat format)
    {
        // Bounded by the node count, in case a malformed scene's parents loop.
        for (int steps = 0; (uint)node < (uint)scene.Nodes.Length && steps < scene.Nodes.Length; steps++)
        {
            if (DisplayName(scene.GetName(scene.Nodes[node].Name), format) is { } name)
            {
                return Text(name);
            }

            node = scene.Nodes[node].Parent;
        }

        return Text(null);
    }

    private static string Text(string? name) => "3D part: " + (name ?? "Unnamed part");

    /// <summary>The name a node is shown by, or null when it has none.</summary>
    private static string? DisplayName(string? nodeName, Artwork3DFormat format)
    {
        string name = nodeName ?? string.Empty;
        if (format == Artwork3DFormat.Prc)
        {
            ReadOnlySpan<char> identifier = PrcNodeNames.IdentifierOf(name);
            if (!identifier.IsEmpty)
            {
                name = name[..^(identifier.Length + 1)];
            }

            if (name == PrcUnnamedOccurrence)
            {
                name = string.Empty;
            }
        }

        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
}
