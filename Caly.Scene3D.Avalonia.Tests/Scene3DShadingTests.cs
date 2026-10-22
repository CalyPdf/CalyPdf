using System.Numerics;
using Caly.Scene3D.Avalonia.Internal;

namespace Caly.Scene3D.Avalonia.Tests;

/// <summary>A viewer's changes to lights and materials scale what the scene says; as authored they change nothing.</summary>
public class Scene3DShadingTests
{
    private static readonly Vector3 Colour = new(0.2f, 0.4f, 0.8f);

    [Theory]
    [InlineData(0f)]
    [InlineData(0.08f)]
    [InlineData(1f)]
    public void AsAuthoredChangesNothing(float reflectivity)
    {
        Scene3DShading shading = Scene3DShading.AsAuthored;

        Assert.Equal(Colour, shading.LightColor(Colour));
        Assert.Equal(Colour, shading.Ambient(Colour));
        Assert.Equal(Colour, shading.Diffuse(Colour));
        Assert.Equal(Colour, shading.Specular(Colour));
        Assert.Equal(Highlight.Exponent(reflectivity), shading.Exponent(reflectivity));
    }

    [Fact]
    public void EachFactorScalesItsOwnValue()
    {
        var shading = new Scene3DShading { LightIntensity = 2, AddedAmbient = 0.25f, SpecularIntensity = 3, HighlightSharpness = 0.5f, DiffuseIntensity = 0.5f };

        Assert.Equal(Colour * 2, shading.LightColor(Colour));
        Assert.Equal(Colour + new Vector3(0.25f), shading.Ambient(Colour));
        Assert.Equal(Colour * 3, shading.Specular(Colour));
        Assert.Equal(Colour * 0.5f, shading.Diffuse(Colour));
        Assert.Equal(16f, shading.Exponent(1f));    // 32 at reflectivity 1, halved
    }

    [Fact]
    public void TheHighlightNeverLosesItsExponent()
    {
        // An exponent of 0 makes every surface as bright as its highlight (and 0 to the 0 is undefined).
        var shading = new Scene3DShading { HighlightSharpness = 0 };

        Assert.True(shading.Exponent(0) > 0);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(-1f)]
    public void AFactorThatIsNotANumberOrIsNegativeLeavesTheSceneAsItIs(float factor)
    {
        var shading = new Scene3DShading { LightIntensity = factor, AddedAmbient = factor, SpecularIntensity = factor, HighlightSharpness = factor, DiffuseIntensity = factor };

        Assert.Equal(Colour, shading.LightColor(Colour));
        Assert.Equal(Colour, shading.Ambient(Colour));
        Assert.Equal(Colour, shading.Specular(Colour));
        Assert.Equal(Colour, shading.Diffuse(Colour));
        Assert.Equal(Highlight.Exponent(0.5f), shading.Exponent(0.5f));
    }

    [Fact]
    public void AddedAmbientIsAtMostWhite()
    {
        Assert.Equal(Vector3.One, new Scene3DShading { AddedAmbient = 5 }.Ambient(Vector3.Zero));
    }
}
