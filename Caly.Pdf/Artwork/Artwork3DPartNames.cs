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

    private const string Prefix = "3D part: ";
    private const string Unnamed = "Unnamed part";
    private const string Separator = " > ";

    /// <summary>
    /// <c>3D part: &lt;name&gt;</c> for a node of that name. A PRC node's name ends in ".&lt;identifier&gt;"
    /// (the PRC entity it stands for), which is left out; a name of nothing is "Unnamed part".
    /// </summary>
    public static string Status(string? nodeName, Artwork3DFormat format)
    {
        ReadOnlySpan<char> name = DisplayName(nodeName, format);
        return name.IsEmpty ? Prefix + Unnamed : string.Concat(Prefix, name);
    }

    /// <summary>
    /// <see cref="Status(string?, Artwork3DFormat)"/> for a node of <paramref name="scene"/>, named after the
    /// the names of the node and of all its ancestors, root first, joined by " &gt; ": the leaf alone (often
    /// an item of geometry with only an identifier) says little. Nodes without a name are left out.
    /// </summary>
    public static string Status(Scene scene, int node, Artwork3DFormat format)
    {
        // Measure, then fill from the end while walking up.
        int length = 0, count = 0;
        for (int n = node, steps = 0; (uint)n < (uint)scene.Nodes.Length && steps < scene.Nodes.Length; steps++, n = scene.Nodes[n].Parent)
        {
            ReadOnlySpan<char> name = DisplayName(scene.GetName(scene.Nodes[n].Name), format);
            if (!name.IsEmpty)
            {
                length += name.Length;
                count++;
            }
        }

        if (count == 0)
        {
            return Prefix + Unnamed;
        }

        return string.Create(Prefix.Length + length + (count - 1) * Separator.Length, (scene, node, format), static (dest, s) =>
        {
            Prefix.CopyTo(dest);
            int end = dest.Length;
            // Bounded by the node count, in case a malformed scene's parents loop.
            for (int n = s.node, steps = 0; (uint)n < (uint)s.scene.Nodes.Length && steps < s.scene.Nodes.Length; steps++, n = s.scene.Nodes[n].Parent)
            {
                ReadOnlySpan<char> name = DisplayName(s.scene.GetName(s.scene.Nodes[n].Name), s.format);
                if (name.IsEmpty)
                {
                    continue;
                }

                if (end != dest.Length)
                {
                    end -= Separator.Length;
                    Separator.CopyTo(dest[end..]);
                }

                end -= name.Length;
                name.CopyTo(dest[end..]);
            }
        });
    }

    /// <summary>The name a node is shown by, or empty when it has none.</summary>
    private static ReadOnlySpan<char> DisplayName(string? nodeName, Artwork3DFormat format)
    {
        ReadOnlySpan<char> name = nodeName;
        if (format == Artwork3DFormat.Prc)
        {
            ReadOnlySpan<char> identifier = PrcNodeNames.IdentifierOf(nodeName ?? string.Empty);
            if (!identifier.IsEmpty)
            {
                name = name[..^(identifier.Length + 1)];
            }

            if (name.SequenceEqual(PrcUnnamedOccurrence))
            {
                return default;
            }
        }

        return name.IsWhiteSpace() ? default : name;
    }
}
