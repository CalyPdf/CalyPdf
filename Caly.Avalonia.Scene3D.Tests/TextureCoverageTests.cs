using System.Buffers;
using System.Numerics;
using Caly.Avalonia.Scene3D.Internal;
using Caly.Rendering.Scene3D;

namespace Caly.Avalonia.Scene3D.Tests;

/// <summary>
/// Whether the transparency of a texture matters to a material: a texture whose empty texels are only the
/// padding between its charts has an alpha that no surface shows.
/// </summary>
public class TextureCoverageTests
{
    /// <summary>A 2 x 2 texture: the left column opaque, the right column 22% opaque (the padding of an atlas).</summary>
    private static DecodedTexture Atlas()
    {
        byte[] pixels = ArrayPool<byte>.Shared.Rent(16);
        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < 2; x++)
            {
                int at = ((y * 2) + x) * 4;
                pixels[at] = pixels[at + 1] = pixels[at + 2] = 56;
                pixels[at + 3] = (byte)(x == 0 ? 255 : 56);
            }
        }

        return new DecodedTexture(pixels, 2, 2, hasAlpha: true, hasPartialAlpha: true);
    }

    /// <summary>One triangle whose corners have the given texture coordinates, under a material that shows texture 0.</summary>
    private static Scene Triangle(float[] uv, byte repeat = 0, Matrix4x4? transform = null, bool coordinates = true)
    {
        using var builder = new SceneBuilder();
        builder.AddTexture(new SceneTexture("Atlas", 2, 2, SceneTextureChannels.Rgba, []));
        int layer = builder.AddTextureLayer(new SceneTextureLayer { Texture = 0, Intensity = 1, Repeat = repeat, Transform = transform ?? Matrix4x4.Identity });
        int material = builder.AddMaterial(new SceneMaterial { Diffuse = Vector3.One, Opacity = 1, FirstLayer = layer, LayerCount = 1, Name = builder.AddName("M") });
        int binding = builder.AddBinding(material);

        builder.BeginMesh(builder.AddName("Mesh"), coordinates ? SceneMeshFlags.HasTexCoords : SceneMeshFlags.None);
        SceneVertexSpans vertices = builder.AllocateVertices(3);
        vertices.Positions[3] = 1;
        vertices.Positions[7] = 1;
        if (coordinates)
        {
            uv.CopyTo(vertices.TexCoords);
        }

        ((ReadOnlySpan<uint>)[0u, 1, 2]).CopyTo(builder.AllocateIndices(3));
        builder.AddPart(3, 0);
        int mesh = builder.EndMesh();

        SceneNode node = TestScenes.Node(builder.AddName("Node"), mesh: mesh);
        node.FirstBinding = binding;
        node.BindingCount = 1;
        builder.AddNode(node);
        return builder.Build();
    }

    private static bool Seen(Scene scene)
    {
        using DecodedTexture atlas = Atlas();
        TextureCoverage? coverage = TextureCoverage.Of(scene, material: 0, layer: 0);
        return coverage is null || coverage.ShowsTransparencyOf(atlas);
    }

    [Fact]
    public void AMeshThatStaysOnTheOpaqueChartsDoesNotShowTheTransparency()
    {
        using Scene scene = Triangle([0.1f, 0.1f, 0.4f, 0.1f, 0.1f, 0.9f]);

        Assert.False(Seen(scene));
    }

    [Fact]
    public void AMeshThatReachesTheTransparentTexelsShowsIt()
    {
        using Scene scene = Triangle([0.6f, 0.1f, 0.9f, 0.1f, 0.6f, 0.9f]);

        Assert.True(Seen(scene));
    }

    [Fact]
    public void AMeshThatCrossesBothShowsIt()
    {
        using Scene scene = Triangle([0.1f, 0.1f, 0.9f, 0.1f, 0.5f, 0.9f]);

        Assert.True(Seen(scene));
    }

    [Fact]
    public void TheTextureMatrixAndTheRepeatAreAppliedToTheCoordinates()
    {
        // Moved one texture to the right, the opaque charts' coordinates land on the transparent column;
        // repeating, those of the next copy land on the opaque one again.
        float[] opaque = [0.1f, 0.1f, 0.4f, 0.1f, 0.1f, 0.9f];
        Matrix4x4 shift = Matrix4x4.CreateTranslation(0.5f, 0, 0);

        using Scene moved = Triangle(opaque, transform: shift);
        using Scene wrapped = Triangle([1.1f, 0.1f, 1.4f, 0.1f, 1.1f, 0.9f], repeat: 3);

        Assert.True(Seen(moved));
        Assert.False(Seen(wrapped));
    }

    [Fact]
    public void WithoutCoordinatesTheTransparencyIsAssumedToShow()
    {
        using Scene scene = Triangle([], coordinates: false);

        Assert.True(Seen(scene));
    }

    [Fact]
    public void AMaterialNoMeshUsesIsAssumedToShowIt()
    {
        using var builder = new SceneBuilder();
        builder.AddTexture(new SceneTexture("Atlas", 2, 2, SceneTextureChannels.Rgba, []));
        int layer = builder.AddTextureLayer(new SceneTextureLayer { Texture = 0, Intensity = 1, Transform = Matrix4x4.Identity });
        builder.AddMaterial(new SceneMaterial { Diffuse = Vector3.One, Opacity = 1, FirstLayer = layer, LayerCount = 1, Name = builder.AddName("M") });
        using Scene scene = builder.Build();

        Assert.True(Seen(scene));
    }
}
