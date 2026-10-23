using System.Numerics;
using Caly.Scene3D.Avalonia.Internal;
using Caly.Rendering.Scene3D;

namespace Caly.Scene3D.Avalonia.Tests;

/// <summary>What each render mode draws (ISO 32000-2, Table 319), and every edge of a mesh for the wireframes.</summary>
public class RenderModeTests
{
    [Theory]
    [InlineData(SceneRenderMode.Solid, SurfacePass.Lit, false, MarkPass.None, MarkPass.None, false, false)]
    [InlineData(SceneRenderMode.SolidWireframe, SurfacePass.Lit, false, MarkPass.Flat, MarkPass.None, false, false)]
    [InlineData(SceneRenderMode.Transparent, SurfacePass.Transparent, false, MarkPass.None, MarkPass.None, false, false)]
    [InlineData(SceneRenderMode.TransparentWireframe, SurfacePass.Transparent, false, MarkPass.Flat, MarkPass.None, false, false)]
    [InlineData(SceneRenderMode.BoundingBox, SurfacePass.None, false, MarkPass.None, MarkPass.None, true, false)]
    [InlineData(SceneRenderMode.TransparentBoundingBox, SurfacePass.None, false, MarkPass.None, MarkPass.None, false, true)]
    [InlineData(SceneRenderMode.TransparentBoundingBoxOutline, SurfacePass.None, false, MarkPass.None, MarkPass.None, true, true)]
    [InlineData(SceneRenderMode.Wireframe, SurfacePass.None, false, MarkPass.Flat, MarkPass.None, false, false)]
    [InlineData(SceneRenderMode.ShadedWireframe, SurfacePass.None, false, MarkPass.Shaded, MarkPass.None, false, false)]
    [InlineData(SceneRenderMode.HiddenWireframe, SurfacePass.DepthOnly, false, MarkPass.Flat, MarkPass.None, false, false)]
    [InlineData(SceneRenderMode.Vertices, SurfacePass.None, false, MarkPass.None, MarkPass.Flat, false, false)]
    [InlineData(SceneRenderMode.ShadedVertices, SurfacePass.None, false, MarkPass.None, MarkPass.Shaded, false, false)]
    [InlineData(SceneRenderMode.Illustration, SurfacePass.Flat, true, MarkPass.None, MarkPass.None, false, false)]
    [InlineData(SceneRenderMode.SolidOutline, SurfacePass.Lit, true, MarkPass.None, MarkPass.None, false, false)]
    [InlineData(SceneRenderMode.ShadedIllustration, SurfacePass.Lit, true, MarkPass.None, MarkPass.None, false, false)]
    internal void EveryModeHasItsPasses(SceneRenderMode mode, SurfacePass surfaces, bool silhouettes, MarkPass edges, MarkPass points, bool boxEdges, bool boxFaces)
    {
        RenderModePlan plan = RenderModePlan.For(mode);

        Assert.Equal((surfaces, silhouettes, edges, points, boxEdges, boxFaces), (plan.Surfaces, plan.Silhouettes, plan.Edges, plan.Points, plan.BoxEdges, plan.BoxFaces));
        Assert.Equal(mode == SceneRenderMode.ShadedIllustration ? RenderModePlan.IllustrationEmissive : 0, plan.AddedEmissive);
        Assert.Equal(silhouettes || edges != MarkPass.None, Scene3DControl.DrawsEdges(mode));
    }

    [Fact]
    public void EveryModeIsCovered()
    {
        Assert.Equal(15, Enum.GetValues<SceneRenderMode>().Length);
    }

    private static Scene Squares()
    {
        // Two parts of one mesh: a square of two triangles (sharing their diagonal), and one triangle that
        // shares an edge with the square but belongs to another part.
        using var builder = new SceneBuilder();
        builder.AddMaterial(new SceneMaterial { Diffuse = Vector3.One, Opacity = 1, Flags = SceneMaterialFlags.Lit, Name = builder.AddName("") });
        builder.BeginMesh(builder.AddName("mesh"), SceneMeshFlags.None);
        new float[] { 0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0, 2, 0, 0 }.CopyTo(builder.AllocateVertices(5).Positions);
        new uint[] { 0, 1, 2, 0, 2, 3, 1, 4, 2 }.CopyTo(builder.AllocateIndices(9));
        builder.AddPart(6, 0);
        builder.AddPart(3, 1);
        int mesh = builder.EndMesh();
        builder.AddNode(TestScenes.Node(builder.AddName("node"), -1, Matrix4x4.Identity, mesh));
        return builder.Build();
    }

    [Fact]
    public void EveryEdgeOfAPartIsListedOnce()
    {
        using Scene scene = Squares();

        WireEdges wires = WireEdges.Build(scene);

        // The square: four sides and the diagonal, the shared diagonal once.
        Assert.Equal(10, wires.Count(0));
        HashSet<(uint, uint)> square = Pairs(wires, 0);
        Assert.Equal(5, square.Count);
        Assert.Contains((0u, 2u), square);

        // The triangle: its three edges, the one it shares with the square too (it is drawn in its own colour).
        Assert.Equal(6, wires.Count(1));
        Assert.Contains((1u, 2u), Pairs(wires, 1));
    }

    [Fact]
    public void LinesAndPointsHaveNoListedEdges()
    {
        using var builder = new SceneBuilder();
        builder.BeginMesh(builder.AddName("lines"), SceneMeshFlags.None, SceneTopology.Lines);
        new float[] { 0, 0, 0, 1, 0, 0 }.CopyTo(builder.AllocateVertices(2).Positions);
        new uint[] { 0, 1 }.CopyTo(builder.AllocateIndices(2));
        builder.AddPart(2, 0);
        builder.EndMesh();
        using Scene scene = builder.Build();

        Assert.True(WireEdges.Build(scene).Indices.IsEmpty);
    }

    private static HashSet<(uint, uint)> Pairs(WireEdges wires, int part)
    {
        var pairs = new HashSet<(uint, uint)>();
        ReadOnlySpan<uint> indices = wires.Indices.Slice(wires.First(part), wires.Count(part));
        for (int i = 0; i < indices.Length; i += 2)
        {
            pairs.Add((Math.Min(indices[i], indices[i + 1]), Math.Max(indices[i], indices[i + 1])));
        }

        return pairs;
    }
}
