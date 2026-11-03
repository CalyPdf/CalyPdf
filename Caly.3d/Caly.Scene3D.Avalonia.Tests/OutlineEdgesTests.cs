using System.Numerics;
using Caly.Scene3D.Avalonia.Internal;
using Caly.Rendering.Scene3D;

namespace Caly.Scene3D.Avalonia.Tests;

/// <summary>
/// The edges that can be silhouette edges (SolidOutline): which edges a mesh has, what each carries for the
/// shader, and the rule that decides which are drawn.
/// </summary>
public class OutlineEdgesTests
{
    private readonly record struct EdgeInfo(Vector3 A, Vector3 B, Vector3 NormalA, Vector3 NormalB, bool Smooth);

    private static Scene Mesh(float[] positions, uint[] indices, float[]? normals = null, SceneTopology topology = SceneTopology.Triangles)
    {
        using var builder = new SceneBuilder();
        builder.AddMaterial(new SceneMaterial { Diffuse = Vector3.One, Opacity = 1, Flags = SceneMaterialFlags.Lit, Name = builder.AddName("") });
        builder.BeginMesh(builder.AddName("mesh"), normals is null ? SceneMeshFlags.None : SceneMeshFlags.HasNormals, topology);
        SceneVertexSpans v = builder.AllocateVertices(positions.Length / 3);
        positions.CopyTo(v.Positions);
        normals?.CopyTo(v.Normals);
        indices.CopyTo(builder.AllocateIndices(indices.Length));
        builder.AddPart(indices.Length, 0);
        int mesh = builder.EndMesh();
        builder.AddNode(TestScenes.Node(builder.AddName("node"), -1, Matrix4x4.Identity, mesh));
        return builder.Build();
    }

    private static List<EdgeInfo> Edges(Scene scene)
    {
        OutlineEdges edges = OutlineEdges.Build(scene);
        int[] words = edges.Vertices.ToArray();
        var list = new List<EdgeInfo>();
        for (int v = edges.First(0); v < edges.First(0) + edges.Count(0); v += 2)
        {
            int at = v * OutlineEdges.WordsPerVertex;
            Vector3 Position(int offset) => new(
                BitConverter.Int32BitsToSingle(words[offset]), BitConverter.Int32BitsToSingle(words[offset + 1]), BitConverter.Int32BitsToSingle(words[offset + 2]));

            (Vector3 normalA, int w) = OutlineEdges.Unpack(words[at + 6]);
            Vector3 normalB = OutlineEdges.Unpack(words[at + 7]).Vector;
            Assert.Equal((Position(at) + Position(at + OutlineEdges.WordsPerVertex)) * 0.5f, Position(at + 3));   // the midpoint
            Assert.Equal(words[at + 6], words[at + OutlineEdges.WordsPerVertex + 6]);                            // both ends alike
            list.Add(new EdgeInfo(Position(at), Position(at + OutlineEdges.WordsPerVertex), normalA, normalB, w == OutlineEdges.SmoothFlag));
        }

        return list;
    }

    private static readonly float[] UnitSquare = [0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0];

    [Fact]
    public void AnOpenSquareHasItsFourSidesAndNotItsDiagonal()
    {
        using Scene scene = Mesh(UnitSquare, [0, 1, 2, 0, 2, 3]);

        List<EdgeInfo> edges = Edges(scene);

        Assert.Equal(4, edges.Count);
        Assert.DoesNotContain(edges, e => (e.A, e.B) is (var a, var b) && a.X != b.X && a.Y != b.Y);
        // A rim is always drawn: its two normals face opposite ways.
        Assert.All(edges, e => Assert.True(OutlineEdges.IsSilhouette(e.NormalA, e.NormalB, e.Smooth, Vector3.UnitZ, OutlineEdges.CreaseCosine(45))));
    }

    [Fact]
    public void VerticesRepeatedForEachFaceAreOneVertex()
    {
        // Each triangle with vertices of its own, as CAD tessellations give faces: the diagonal is still shared.
        using Scene scene = Mesh([0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 0, 0, 1, 1, 0, 0, 1, 0], [0, 1, 2, 3, 4, 5]);

        Assert.Equal(4, Edges(scene).Count);
    }

    [Fact]
    public void FacesWoundTheOtherWayStillMeetInOnePlane()
    {
        // The second triangle wound backwards: its normal is turned back, so the diagonal stays an edge in a plane.
        using Scene scene = Mesh(UnitSquare, [0, 1, 2, 0, 3, 2]);

        Assert.Equal(4, Edges(scene).Count);
    }

    /// <summary>
    /// Two faces folded 30 degrees along their shared edge (y = 0): the first in z = 0 facing +Z, the second
    /// leaning back below it, its normal (0, −0.5, 0.866). With <paramref name="smooth"/> the faces share the
    /// edge's vertices and one normal there, as a smooth surface cut in facets does; without, each face has
    /// vertices of its own at the edge with its own normal, as a CAD tessellation gives a real crease.
    /// </summary>
    private static Scene Fold(bool smooth)
    {
        float c = MathF.Cos(MathF.PI / 6), s = MathF.Sin(MathF.PI / 6);
        var leaning = new Vector3(0, -s, c);
        float[] positions = [0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0, 1, -c, -s, 0, -c, -s, 0, 0, 0, 1, 0, 0];
        float[] normals = new float[8 * 3];
        for (int i = 0; i < 8; i++)
        {
            Vector3 n = i < 4 || smooth ? Vector3.UnitZ : leaning;
            normals[i * 3] = n.X; normals[i * 3 + 1] = n.Y; normals[i * 3 + 2] = n.Z;
        }

        // The second face goes along the shared edge the other way (1 to 0), as faces wound alike do.
        uint[] indices = smooth ? [0, 1, 2, 0, 2, 3, 1, 0, 5, 1, 5, 4] : [0, 1, 2, 0, 2, 3, 7, 6, 5, 7, 5, 4];
        return Mesh(positions, indices, normals);
    }

    private static EdgeInfo FoldEdge(Scene scene) => Assert.Single(Edges(scene), e => e.A.Y == 0 && e.B.Y == 0);

    // Seen from the front both faces turn towards the viewer; seen from above the fold one turns away.
    private static readonly Vector3 Front = Vector3.Normalize(new Vector3(0, -0.2f, 1));
    private static readonly Vector3 Between = Vector3.Normalize(new Vector3(0, 1, 0.1f));

    [Fact]
    public void AnEdgeBetweenFacesAtAnAngleIsACreaseWhereTheCreaseValueSaysSo()
    {
        using Scene scene = Fold(smooth: false);
        EdgeInfo fold = FoldEdge(scene);

        Assert.False(fold.Smooth);
        Assert.InRange(MathF.Acos(Vector3.Dot(fold.NormalA, fold.NormalB)) * 180 / MathF.PI, 29, 31);
        Assert.True(OutlineEdges.IsSilhouette(fold.NormalA, fold.NormalB, fold.Smooth, Front, OutlineEdges.CreaseCosine(10)));
        Assert.False(OutlineEdges.IsSilhouette(fold.NormalA, fold.NormalB, fold.Smooth, Front, OutlineEdges.CreaseCosine(45)));
    }

    [Fact]
    public void AnEdgeOnASmoothSurfaceIsNoCreaseButCanStillBeASilhouette()
    {
        using Scene scene = Fold(smooth: true);
        EdgeInfo fold = FoldEdge(scene);

        Assert.True(fold.Smooth);
        Assert.False(OutlineEdges.IsSilhouette(fold.NormalA, fold.NormalB, fold.Smooth, Front, OutlineEdges.CreaseCosine(10)));
        Assert.True(OutlineEdges.IsSilhouette(fold.NormalA, fold.NormalB, fold.Smooth, Between, OutlineEdges.CreaseCosine(10)));
    }

    [Fact]
    public void AMeshWithoutNormalsHasNoSmoothEdges()
    {
        using Scene faceted = Mesh([0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0, 1, -0.866f, -0.5f, 0, -0.866f, -0.5f], [0, 1, 2, 0, 2, 3, 1, 0, 5, 1, 5, 4]);

        Assert.False(FoldEdge(faceted).Smooth);
    }

    [Fact]
    public void LinesPointsAndMeshesWithoutGeometryHaveNoEdges()
    {
        using Scene lines = Mesh([0, 0, 0, 1, 0, 0], [0, 1], topology: SceneTopology.Lines);
        using Scene points = Mesh([0, 0, 0, 1, 0, 0], [0, 1], topology: SceneTopology.Points);
        using Scene flat = Mesh([0, 0, 0, 1, 0, 0, 2, 0, 0], [0, 1, 2]);    // a triangle of no area

        Assert.Equal(0, OutlineEdges.Build(lines).VertexCount);
        Assert.Equal(0, OutlineEdges.Build(points).VertexCount);
        Assert.Equal(0, OutlineEdges.Build(flat).VertexCount);
    }

    [Fact]
    public void AMeshOfManyTrianglesIsQuick()
    {
        // A grid of 200 x 200 squares (80 000 triangles), every square bent a little: all inner edges kept.
        const int n = 200;
        var positions = new float[(n + 1) * (n + 1) * 3];
        for (int y = 0; y <= n; y++)
        {
            for (int x = 0; x <= n; x++)
            {
                int at = (y * (n + 1) + x) * 3;
                positions[at] = x;
                positions[at + 1] = y;
                positions[at + 2] = (x + y) % 2 * 0.3f;
            }
        }

        var indices = new uint[n * n * 6];
        for (int y = 0, i = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                uint a = (uint)(y * (n + 1) + x), b = a + 1, c = a + n + 1, d = c + 1;
                indices[i++] = a; indices[i++] = b; indices[i++] = d;
                indices[i++] = a; indices[i++] = d; indices[i++] = c;
            }
        }

        using Scene scene = Mesh(positions, indices);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        OutlineEdges edges = OutlineEdges.Build(scene);
        watch.Stop();

        Assert.True(edges.VertexCount > 0);
        Assert.True(watch.ElapsedMilliseconds < 2000, $"{watch.ElapsedMilliseconds} ms");
    }

    [Theory]
    [InlineData(1, 0, 0, 0)]
    [InlineData(0, -1, 0, 1)]
    [InlineData(0.6f, 0, -0.8f, 1)]
    public void ANormalPackedForTheGpuComesBackClose(float x, float y, float z, int w)
    {
        (Vector3 vector, int back) = OutlineEdges.Unpack(OutlineEdges.Pack(new Vector3(x, y, z), w));

        Assert.Equal(w, back);
        Assert.True(Vector3.Distance(new Vector3(x, y, z), vector) < 0.004f);
    }

    [Theory]
    [InlineData(45f, 0.7071f)]
    [InlineData(10f, 0.9848f)]
    [InlineData(float.NaN, 0.7071f)]
    [InlineData(-5f, 1f)]
    public void TheCreaseAngleIsComparedThroughItsCosine(float degrees, float cosine)
    {
        Assert.Equal(cosine, OutlineEdges.CreaseCosine(degrees), 3);
    }
}
