using System.Numerics;
using Caly.Rendering.Scene3D;

namespace Caly.Scene3D.Avalonia.Internal;

/// <summary>
/// Whether the transparency of a texture is something a material shows. Writers pack the charts of several
/// surfaces into one picture and leave the rest of it empty (a mostly transparent fill): a mesh whose
/// coordinates stay on the charts never shows it, and blending such a mesh takes it out of the opaque pass,
/// where a translucent overlay drawn later (a shadow layer) lands on top of it wherever the overlay is nearer
/// than its own twin of the surface.
/// </summary>
internal sealed class TextureCoverage
{
    /// <summary>The cells per side of the grid that records where the surfaces of a material sample its texture.</summary>
    private const int Cells = 256;

    /// <summary>The share of samples that may be transparent before the surface counts as transparent: a fringe of a chart, not a hole.</summary>
    private const int TransparentSamplesPerHundred = 1;

    /// <summary>Triangles looked at per part, at most (an even stride over the rest).</summary>
    private const int MaxTriangles = 100_000;

    private readonly ulong[] _cells = new ulong[Cells * Cells / 64];

    private TextureCoverage()
    {
    }

    /// <summary>
    /// Records where the surfaces of <paramref name="scene"/> that use <paramref name="material"/> sample the
    /// texture of <paramref name="layer"/> (its matrix and repeat flags apply to their coordinates): the
    /// corners, the middles of the sides and the middle of each triangle. Needs the geometry of the scene,
    /// which the control gives up after uploading it. Null when it cannot be told: no mesh uses the material,
    /// or one has no texture coordinates.
    /// </summary>
    public static TextureCoverage? Of(Scene scene, int material, int layer)
    {
        ReadOnlySpan<SceneNode> nodes = scene.Nodes;
        ReadOnlySpan<int> bindings = scene.Bindings;
        ReadOnlySpan<SceneMesh> meshes = scene.Meshes;
        ReadOnlySpan<SceneMeshPart> parts = scene.Parts;
        ReadOnlySpan<uint> indices = scene.Indices;
        ReadOnlySpan<float> coordinates = scene.TexCoords;
        if ((uint)layer >= (uint)scene.TextureLayers.Length)
        {
            return null;
        }

        SceneTextureLayer textureLayer = scene.TextureLayers[layer];
        var coverage = new TextureCoverage();
        bool used = false;
        var visited = new HashSet<(int Mesh, int Binding)>();
        foreach (SceneNode node in nodes)
        {
            if (node.Mesh < 0 || !visited.Add((node.Mesh, node.FirstBinding)))
            {
                continue;
            }

            SceneMesh mesh = meshes[node.Mesh];
            if (mesh.Topology != SceneTopology.Triangles)
            {
                continue;
            }

            for (int p = 0; p < mesh.PartCount; p++)
            {
                SceneMeshPart part = parts[mesh.FirstPart + p];
                if ((uint)part.ShadingId >= (uint)node.BindingCount || bindings[node.FirstBinding + part.ShadingId] != material)
                {
                    continue;
                }

                used = true;
                if (mesh.TexCoordStart < 0 || (long)(mesh.TexCoordStart + mesh.VertexCount) * 2 > coordinates.Length || (long)part.IndexStart + part.IndexCount > indices.Length)
                {
                    return null;
                }

                int triangles = part.IndexCount / 3;
                int stride = Math.Max(1, triangles / MaxTriangles);
                ReadOnlySpan<float> uv = coordinates.Slice(mesh.TexCoordStart * 2, mesh.VertexCount * 2);
                for (int t = 0; t < triangles; t += stride)
                {
                    int at = part.IndexStart + (t * 3);
                    uint a = indices[at], b = indices[at + 1], c = indices[at + 2];
                    if (a >= (uint)mesh.VertexCount || b >= (uint)mesh.VertexCount || c >= (uint)mesh.VertexCount)
                    {
                        continue;
                    }

                    var pa = new Vector2(uv[(int)a * 2], uv[((int)a * 2) + 1]);
                    var pb = new Vector2(uv[(int)b * 2], uv[((int)b * 2) + 1]);
                    var pc = new Vector2(uv[(int)c * 2], uv[((int)c * 2) + 1]);
                    coverage.Mark(pa, textureLayer);
                    coverage.Mark(pb, textureLayer);
                    coverage.Mark(pc, textureLayer);
                    coverage.Mark((pa + pb) * 0.5f, textureLayer);
                    coverage.Mark((pb + pc) * 0.5f, textureLayer);
                    coverage.Mark((pc + pa) * 0.5f, textureLayer);
                    coverage.Mark((pa + pb + pc) / 3f, textureLayer);
                }
            }
        }

        return used ? coverage : null;
    }

    /// <summary>
    /// Whether the surfaces reach texels of <paramref name="pixels"/> that are not opaque, in more than the
    /// share of a fringe of the places they sample.
    /// </summary>
    public bool ShowsTransparencyOf(DecodedTexture pixels)
    {
        ReadOnlySpan<byte> texels = pixels.Pixels;
        if (texels.IsEmpty)
        {
            return true;
        }

        long samples = 0, transparent = 0;
        for (int cell = 0; cell < Cells * Cells; cell++)
        {
            if ((_cells[cell >> 6] & (1UL << (cell & 63))) == 0)
            {
                continue;
            }

            // The texel at the middle of the cell.
            int x = Math.Min(pixels.Width - 1, (((cell % Cells) * 2) + 1) * pixels.Width / (Cells * 2));
            int y = Math.Min(pixels.Height - 1, (((cell / Cells) * 2) + 1) * pixels.Height / (Cells * 2));
            samples++;
            if (texels[(((y * pixels.Width) + x) * 4) + 3] != 255)
            {
                transparent++;
            }
        }

        return transparent * 100 > samples * TransparentSamplesPerHundred;
    }

    private void Mark(Vector2 point, in SceneTextureLayer layer)
    {
        point = Vector2.Transform(point, layer.Transform);
        int x = Math.Min(Cells - 1, (int)(Wrap(point.X, (layer.Repeat & 1) != 0) * Cells));
        // Texture coordinates run up from the bottom of the picture; its rows are stored from the top.
        int y = Math.Min(Cells - 1, (int)((1f - Wrap(point.Y, (layer.Repeat & 2) != 0)) * Cells));
        int cell = (y * Cells) + x;
        _cells[cell >> 6] |= 1UL << (cell & 63);
    }

    private static float Wrap(float value, bool repeat)
    {
        if (!float.IsFinite(value))
        {
            return 0f;
        }

        value = repeat ? value - MathF.Floor(value) : Math.Clamp(value, 0f, 1f);
        return value >= 1f ? 0.999999f : value;
    }
}
