using System.Numerics;
using Caly.Pdf.Artwork;
using Caly.Rendering.Scene3D;
using UglyToad.PdfPig.Artwork;

namespace Caly.Tests;

/// <summary>What the status bar says of the selected part of a 3D model.</summary>
public class Artwork3DPartNamesTests
{
    [Theory]
    [InlineData("Bolt.1a2b", Artwork3DFormat.Prc, "3D part: Bolt")]
    [InlineData("Bolt", Artwork3DFormat.Prc, "3D part: Bolt")]
    [InlineData("Shaft v2.ab", Artwork3DFormat.Prc, "3D part: Shaft v2")]
    [InlineData("node.ff", Artwork3DFormat.Prc, "3D part: Unnamed part")]
    [InlineData("", Artwork3DFormat.U3D, "3D part: Unnamed part")]
    [InlineData(null, Artwork3DFormat.U3D, "3D part: Unnamed part")]
    [InlineData("   ", Artwork3DFormat.U3D, "3D part: Unnamed part")]
    [InlineData("Part.12", Artwork3DFormat.U3D, "3D part: Part.12")]
    public void TheStatusNamesThePartAsTheReaderKnowsIt(string? nodeName, Artwork3DFormat format, string expected)
    {
        Assert.Equal(expected, Artwork3DPartNames.Status(nodeName, format));
    }

    /// <summary>A chain of nodes, the first the root; the last carries a mesh. Returns the scene and the last node.</summary>
    private static (Scene Scene, int Leaf) Chain(params string[] names)
    {
        using var builder = new SceneBuilder();
        builder.BeginMesh(builder.AddName("mesh"), SceneMeshFlags.None);
        SceneVertexSpans v = builder.AllocateVertices(3);
        new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 }.CopyTo(v.Positions);
        new uint[] { 0, 1, 2 }.CopyTo(builder.AllocateIndices(3));
        builder.AddPart(3, 0);
        int mesh = builder.EndMesh();
        for (int i = 0; i < names.Length; i++)
        {
            builder.AddNode(new SceneNode
            {
                Parent = i - 1, Local = Matrix4x4.Identity, Kind = SceneNodeKind.Model, Mesh = i == names.Length - 1 ? mesh : -1,
                Light = -1, Camera = -1, Name = builder.AddName(names[i]), Visible = true
            });
        }

        return (builder.Build(), names.Length - 1);
    }

    [Fact]
    public void AnUnnamedPrcPieceIsNamedAfterThePartItBelongsTo()
    {
        // As PRC writers store it: the mesh's item has only an identifier, the part above it a name.
        (Scene scene, int leaf) = Chain("Receptacle.40", "Default.38", ".7b5020");
        using (scene)
        {
            Assert.Equal("3D part: Default", Artwork3DPartNames.Status(scene, leaf, Artwork3DFormat.Prc));
        }
    }

    [Fact]
    public void ANamedPieceKeepsItsOwnName()
    {
        (Scene scene, int leaf) = Chain("Assembly.40", "Bolt.12");
        using (scene)
        {
            Assert.Equal("3D part: Bolt", Artwork3DPartNames.Status(scene, leaf, Artwork3DFormat.Prc));
        }
    }

    [Fact]
    public void APieceWithNoNameAboveItIsUnnamed()
    {
        (Scene scene, int leaf) = Chain("node.40", ".12");
        using (scene)
        {
            Assert.Equal("3D part: Unnamed part", Artwork3DPartNames.Status(scene, leaf, Artwork3DFormat.Prc));
        }
    }
}
