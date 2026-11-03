using Caly.Rendering.Scene3D;

namespace Caly.Scene3D.Avalonia.Internal;

/// <summary>
/// Every edge of every triangle of a scene, for the wireframe render modes: per part of each triangle mesh,
/// pairs of the mesh's own vertex numbers, each edge once (two triangles that share an edge draw it once).
/// Drawn with the part's mesh and material, as lines. Parts of line segments or points have none: those
/// are drawn as they are.
/// </summary>
internal sealed class WireEdges
{
    private readonly uint[] _indices;
    private readonly int[] _first, _count;

    private WireEdges(uint[] indices, int[] first, int[] count)
    {
        _indices = indices;
        _first = first;
        _count = count;
    }

    /// <summary>All edges, two vertex numbers each, part after part.</summary>
    public ReadOnlySpan<uint> Indices => _indices;

    /// <summary>Where a part's edges begin in <see cref="Indices"/>.</summary>
    public int First(int part) => (uint)part < (uint)_first.Length ? _first[part] : 0;

    /// <summary>Number of indices of a part's edges: twice its edges.</summary>
    public int Count(int part) => (uint)part < (uint)_count.Length ? _count[part] : 0;

    /// <summary>Lists the edges of every triangle mesh of a scene that still holds its geometry. Reads the scene only.</summary>
    public static WireEdges Build(Scene scene, CancellationToken cancellation = default)
    {
        ReadOnlySpan<SceneMesh> meshes = scene.Meshes;
        ReadOnlySpan<SceneMeshPart> parts = scene.Parts;
        ReadOnlySpan<uint> indices = scene.Indices;
        var first = new int[parts.Length];
        var count = new int[parts.Length];
        var list = new List<uint>();
        var seen = new HashSet<long>(EdgeKeyComparer.Instance);

        foreach (SceneMesh mesh in meshes)
        {
            cancellation.ThrowIfCancellationRequested();
            if (mesh.Topology != SceneTopology.Triangles || mesh.VertexCount <= 0)
            {
                continue;
            }

            for (int p = mesh.FirstPart; p < mesh.FirstPart + mesh.PartCount && (uint)p < (uint)parts.Length; p++)
            {
                SceneMeshPart part = parts[p];
                first[p] = list.Count;
                if (part.IndexStart < 0 || part.IndexCount < 3 || (long)part.IndexStart + part.IndexCount > indices.Length)
                {
                    continue;
                }

                ReadOnlySpan<uint> triangles = indices.Slice(part.IndexStart, part.IndexCount - part.IndexCount % 3);
                seen.Clear();
                seen.EnsureCapacity(triangles.Length);
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    uint a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                    if (a >= (uint)mesh.VertexCount || b >= (uint)mesh.VertexCount || c >= (uint)mesh.VertexCount)
                    {
                        continue;
                    }

                    Add(a, b);
                    Add(b, c);
                    Add(c, a);
                }

                count[p] = list.Count - first[p];
            }
        }

        return new WireEdges(list.ToArray(), first, count);

        void Add(uint a, uint b)
        {
            if (a == b)
            {
                return;
            }

            long key = a < b ? ((long)a << 32) | b : ((long)b << 32) | a;
            if (seen.Add(key))
            {
                list.Add(a);
                list.Add(b);
            }
        }
    }
}
