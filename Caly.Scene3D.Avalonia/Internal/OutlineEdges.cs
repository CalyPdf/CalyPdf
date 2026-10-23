using System.Numerics;
using System.Runtime.InteropServices;
using Caly.Rendering.Scene3D;

namespace Caly.Scene3D.Avalonia.Internal;

/// <summary>
/// The edges of a scene's meshes that can be silhouette edges (ISO 32000-2, 13.6.4.4), for the render
/// modes that draw outlines. Which of them is drawn depends on the view, so each edge carries what the
/// shader decides it from: the normals of its two faces, its midpoint, and whether it can be a crease;
/// see <see cref="IsSilhouette"/>.
/// <para>
/// Vertices are welded by position first: CAD tessellations repeat a vertex for each face that meets at it
/// (each with that face's normal), and without welding every face would be closed off by edges. An edge
/// across which the mesh's own vertex normals carry on unchanged lies on a smooth surface: it is drawn
/// where the surface turns away from the viewer, never as a crease (a thin tube cut in a few facets would
/// otherwise be all edges). An edge with one face (the rim of an open sheet) or more than two is always
/// drawn; an edge between two faces in one plane never is, and is left out. Meshes of line segments or
/// points, and skinned meshes (their faces move), have no edges.
/// </para>
/// </summary>
internal sealed class OutlineEdges
{
    /// <summary>
    /// Per vertex, in 32-bit words: position and the edge's midpoint (three floats each, as their bits),
    /// then the normals of its two faces, each packed as OpenGL's signed normalised 2_10_10_10 (see
    /// <see cref="Pack"/>).
    /// </summary>
    public const int WordsPerVertex = 8;

    /// <summary>Normal A's W when the edge lies on a smooth surface and is never a crease.</summary>
    public const int SmoothFlag = 1;

    // Faces whose normals are this close are in one plane: their edge is never a crease, and a view
    // cannot see one of them from the front and the other from the back.
    private const float CoplanarCosine = 0.99999f;

    // Vertex normals this close are one normal carried across an edge.
    private const float SameNormalCosine = 0.9999f;

    private readonly int[] _vertices;
    private readonly int[] _first, _count;

    private OutlineEdges(int[] vertices, int[] first, int[] count)
    {
        _vertices = vertices;
        _first = first;
        _count = count;
    }

    /// <summary>All edge vertices, <see cref="WordsPerVertex"/> words each, two per edge.</summary>
    public ReadOnlySpan<int> Vertices => _vertices;

    /// <summary>Number of edge vertices.</summary>
    public int VertexCount => _vertices.Length / WordsPerVertex;

    /// <summary>The first edge vertex of a mesh.</summary>
    public int First(int mesh) => (uint)mesh < (uint)_first.Length ? _first[mesh] : 0;

    /// <summary>Number of edge vertices of a mesh: twice its edges; 0 for a mesh without.</summary>
    public int Count(int mesh) => (uint)mesh < (uint)_count.Length ? _count[mesh] : 0;

    /// <summary>
    /// Whether an edge is drawn: its faces turn one towards the viewer and the other away, or (unless it
    /// lies on a smooth surface) meet at the crease angle or more. Normals and the direction to the viewer
    /// in one space; the faces' normals as <see cref="Build"/> gives them. The shader decides the same way.
    /// </summary>
    public static bool IsSilhouette(Vector3 normalA, Vector3 normalB, bool smooth, Vector3 toViewer, float creaseCosine)
    {
        bool towardsA = Vector3.Dot(normalA, toViewer) > 0, towardsB = Vector3.Dot(normalB, toViewer) > 0;
        return towardsA != towardsB || (!smooth && Vector3.Dot(normalA, normalB) <= creaseCosine);
    }

    /// <summary>
    /// A unit vector and a two-bit W as OpenGL's <c>GL_INT_2_10_10_10_REV</c>, normalised: X, Y and Z in ten
    /// bits each from the lowest, as signed fractions of 511, then W (0 or 1).
    /// </summary>
    public static int Pack(Vector3 v, int w = 0)
    {
        static int Ten(float c) => (int)MathF.Round(Math.Clamp(float.IsFinite(c) ? c : 0, -1f, 1f) * 511f) & 0x3FF;
        return Ten(v.X) | Ten(v.Y) << 10 | Ten(v.Z) << 20 | (w & 3) << 30;
    }

    /// <summary>What the GPU reads from <see cref="Pack"/>: the vector and W.</summary>
    public static (Vector3 Vector, int W) Unpack(int packed)
    {
        static float Ten(int bits) => Math.Max(-1f, ((bits << 22) >> 22) / 511f);
        return (new Vector3(Ten(packed), Ten(packed >> 10), Ten(packed >> 20)), packed >> 30);
    }

    /// <summary>The cosine the crease angle (in degrees) is compared through; an angle that is not a number is 45.</summary>
    public static float CreaseCosine(float creaseAngle)
    {
        float degrees = float.IsFinite(creaseAngle) ? Math.Clamp(creaseAngle, 0f, 180f) : 45f;
        return MathF.Cos(degrees * MathF.PI / 180f);
    }

    /// <summary>
    /// Finds the edges of every mesh of a scene that still holds its geometry. Reads the scene only.
    /// </summary>
    public static OutlineEdges Build(Scene scene, CancellationToken cancellation = default)
    {
        ReadOnlySpan<SceneMesh> meshes = scene.Meshes;
        var first = new int[meshes.Length];
        var count = new int[meshes.Length];
        var vertices = new List<int>();
        var builder = new MeshEdges();

        for (int m = 0; m < meshes.Length; m++)
        {
            cancellation.ThrowIfCancellationRequested();
            SceneMesh mesh = meshes[m];
            first[m] = vertices.Count / WordsPerVertex;
            if (mesh.Topology != SceneTopology.Triangles || mesh.Skin >= 0 || mesh.VertexCount <= 0 ||
                (long)(mesh.VertexStart + mesh.VertexCount) * 3 > scene.Positions.Length)
            {
                continue;
            }

            ReadOnlySpan<float> normals = mesh.NormalStart >= 0 && (long)(mesh.NormalStart + mesh.VertexCount) * 3 <= scene.Normals.Length
                ? scene.Normals.Slice(mesh.NormalStart * 3, mesh.VertexCount * 3)
                : default;
            builder.Collect(scene, mesh, normals);
            count[m] = builder.Emit(scene.Positions.Slice(mesh.VertexStart * 3, mesh.VertexCount * 3), vertices);
        }

        return new OutlineEdges(vertices.ToArray(), first, count);
    }

    /// <summary>The edges of one mesh; reused from mesh to mesh.</summary>
    private sealed class MeshEdges
    {
        private struct Edge
        {
            public int A, B;            // vertices of the mesh at its ends, as its first face has them
            public Vector3 NormalA, NormalB;
            public bool Forward;        // the welded ends in increasing order as its first face goes round
            public bool Smooth;
            public int Faces;
        }

        private readonly Dictionary<Vector3, int> _weld = new();
        private readonly Dictionary<long, int> _edgeOf = new(EdgeKeyComparer.Instance);
        private int[] _welded = [];
        private Edge[] _edges = [];
        private int _edgeCount;

        public void Collect(Scene scene, in SceneMesh mesh, ReadOnlySpan<float> normals)
        {
            _weld.Clear();
            _edgeOf.Clear();
            _edgeCount = 0;
            _weld.EnsureCapacity(mesh.VertexCount);
            if (_welded.Length < mesh.VertexCount)
            {
                _welded = new int[mesh.VertexCount];
            }

            ReadOnlySpan<float> positions = scene.Positions.Slice(mesh.VertexStart * 3, mesh.VertexCount * 3);
            for (int v = 0; v < mesh.VertexCount; v++)
            {
                Vector3 p = Read(positions, v);
                if (!_weld.TryGetValue(p, out int id))
                {
                    id = _weld.Count;
                    _weld.Add(p, id);
                }

                _welded[v] = id;
            }

            ReadOnlySpan<SceneMeshPart> parts = scene.Parts;
            ReadOnlySpan<uint> indices = scene.Indices;

            // About one and a half edges to a triangle in a closed surface: sized once, never grown.
            long triangleCount = 0;
            for (int p = mesh.FirstPart; p < mesh.FirstPart + mesh.PartCount && (uint)p < (uint)parts.Length; p++)
            {
                triangleCount += Math.Max(0, parts[p].IndexCount) / 3;
            }

            int expectedEdges = (int)Math.Min(triangleCount * 3 / 2 + 16, int.MaxValue / 2);
            _edgeOf.EnsureCapacity(expectedEdges);
            if (_edges.Length < expectedEdges)
            {
                _edges = new Edge[expectedEdges];
            }

            for (int p = mesh.FirstPart; p < mesh.FirstPart + mesh.PartCount && (uint)p < (uint)parts.Length; p++)
            {
                SceneMeshPart part = parts[p];
                if (part.IndexStart < 0 || part.IndexCount < 0 || (long)part.IndexStart + part.IndexCount > indices.Length)
                {
                    continue;
                }

                ReadOnlySpan<uint> triangles = indices.Slice(part.IndexStart, part.IndexCount - part.IndexCount % 3);
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    uint a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                    if (a >= (uint)mesh.VertexCount || b >= (uint)mesh.VertexCount || c >= (uint)mesh.VertexCount)
                    {
                        continue;
                    }

                    int wa = _welded[a], wb = _welded[b], wc = _welded[c];
                    if (wa == wb || wb == wc || wc == wa)
                    {
                        continue;
                    }

                    Vector3 pa = Read(positions, (int)a);
                    Vector3 normal = Vector3.Cross(Read(positions, (int)b) - pa, Read(positions, (int)c) - pa);
                    float length = normal.Length();
                    if (!(length > 0) || !float.IsFinite(length))
                    {
                        continue;
                    }

                    normal /= length;
                    Add((int)a, (int)b, wa, wb, normal, normals);
                    Add((int)b, (int)c, wb, wc, normal, normals);
                    Add((int)c, (int)a, wc, wa, normal, normals);
                }
            }
        }

        private void Add(int a, int b, int weldedA, int weldedB, Vector3 normal, ReadOnlySpan<float> normals)
        {
            bool forward = weldedA < weldedB;
            long key = forward ? ((long)weldedA << 32) | (uint)weldedB : ((long)weldedB << 32) | (uint)weldedA;
            ref int slot = ref CollectionsMarshal.GetValueRefOrAddDefault(_edgeOf, key, out bool exists);
            if (!exists)
            {
                if (_edgeCount == _edges.Length)
                {
                    Array.Resize(ref _edges, Math.Max(1024, _edges.Length * 2));
                }

                slot = _edgeCount;
                _edges[_edgeCount++] = new Edge { A = a, B = b, NormalA = normal, Forward = forward, Faces = 1 };
                return;
            }

            ref Edge edge = ref _edges[slot];
            if (edge.Faces == 1)
            {
                // Faces wound the same way go along their shared edge in opposite directions. Along it the
                // same way, one of them is wound the other way round, and its normal is turned back to agree.
                edge.NormalB = forward == edge.Forward ? -normal : normal;

                // The same vertex normals at both ends on both sides: one smooth surface.
                bool aIsFirst = _welded[edge.A] == weldedA;
                edge.Smooth = !normals.IsEmpty &&
                    SameNormal(normals, edge.A, aIsFirst ? a : b) && SameNormal(normals, edge.B, aIsFirst ? b : a);
            }

            edge.Faces++;
        }

        private static bool SameNormal(ReadOnlySpan<float> normals, int first, int second)
        {
            if (first == second)
            {
                return true;
            }

            Vector3 n1 = Read(normals, first), n2 = Read(normals, second);
            float lengths = n1.Length() * n2.Length();
            return lengths > 0 && Vector3.Dot(n1, n2) >= SameNormalCosine * lengths;
        }

        /// <summary>Appends the edges that can be drawn; returns how many vertices that is.</summary>
        public int Emit(ReadOnlySpan<float> positions, List<int> vertices)
        {
            int before = vertices.Count;
            for (int i = 0; i < _edgeCount; i++)
            {
                Edge edge = _edges[i];
                Vector3 normalB;
                bool smooth = false;
                if (edge.Faces == 2)
                {
                    if (Vector3.Dot(edge.NormalA, edge.NormalB) >= CoplanarCosine)
                    {
                        continue;
                    }

                    normalB = edge.NormalB;
                    smooth = edge.Smooth;
                }
                else
                {
                    // One face, or more than two: always drawn, as faces that turn opposite ways are.
                    normalB = -edge.NormalA;
                }

                Vector3 a = Read(positions, edge.A), b = Read(positions, edge.B);
                Vector3 middle = (a + b) * 0.5f;
                int packedA = Pack(edge.NormalA, smooth ? SmoothFlag : 0), packedB = Pack(normalB);
                Append(vertices, a, middle, packedA, packedB);
                Append(vertices, b, middle, packedA, packedB);
            }

            return (vertices.Count - before) / WordsPerVertex;
        }

        private static void Append(List<int> vertices, Vector3 position, Vector3 middle, int normalA, int normalB)
        {
            vertices.Add(BitConverter.SingleToInt32Bits(position.X));
            vertices.Add(BitConverter.SingleToInt32Bits(position.Y));
            vertices.Add(BitConverter.SingleToInt32Bits(position.Z));
            vertices.Add(BitConverter.SingleToInt32Bits(middle.X));
            vertices.Add(BitConverter.SingleToInt32Bits(middle.Y));
            vertices.Add(BitConverter.SingleToInt32Bits(middle.Z));
            vertices.Add(normalA);
            vertices.Add(normalB);
        }

        private static Vector3 Read(ReadOnlySpan<float> values, int vertex)
        {
            return new Vector3(values[vertex * 3], values[vertex * 3 + 1], values[vertex * 3 + 2]);
        }
    }
}

/// <summary>
/// An edge's key is its two ends (vertex numbers) in the halves of a long; the long's own hash code, the halves
/// XORed, gives every edge whose ends differ in the same bits one bucket. The bits are mixed instead.
/// </summary>
internal sealed class EdgeKeyComparer : IEqualityComparer<long>
{
    public static readonly EdgeKeyComparer Instance = new();

    public bool Equals(long x, long y) => x == y;

    public int GetHashCode(long key) => (int)(((ulong)key * 0x9E3779B97F4A7C15UL) >> 32);
}
