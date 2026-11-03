using System.Numerics;
using Caly.Scene3D.Avalonia.Internal;

namespace Caly.Scene3D.Avalonia;

/// <summary>
/// What a viewer changes in how a <see cref="Scene3DControl"/> lights and shades its scene, on top of what
/// the scene and its view say. <see cref="AsAuthored"/> changes nothing; each factor scales the scene's
/// own values, so parts keep their differences.
/// </summary>
public sealed record Scene3DShading
{
    /// <summary>The scene as its file has it.</summary>
    public static Scene3DShading AsAuthored { get; } = new();

    /// <summary>Scales the colour of every light: of the view's lighting scheme, or the scene's own.</summary>
    public float LightIntensity { get; init; } = 1;

    /// <summary>Grey light added to the ambient light, 0 to 1: lifts the sides the lights do not reach.</summary>
    public float AddedAmbient { get; init; }

    /// <summary>Scales every material's specular colour: how bright the highlights are.</summary>
    public float SpecularIntensity { get; init; } = 1;

    /// <summary>Multiplies the highlight's exponent: above 1 a tighter highlight, below 1 a broader one.</summary>
    public float HighlightSharpness { get; init; } = 1;

    /// <summary>Scales every material's diffuse colour: how much of its own colour a surface shows.</summary>
    public float DiffuseIntensity { get; init; } = 1;

    internal Vector3 LightColor(Vector3 authored) => authored * Factor(LightIntensity, 1);

    internal Vector3 Ambient(Vector3 authored) => authored + new Vector3(Math.Min(Factor(AddedAmbient, 0), 1));

    internal Vector3 Diffuse(Vector3 authored) => authored * Factor(DiffuseIntensity, 1);

    internal Vector3 Specular(Vector3 authored) => authored * Factor(SpecularIntensity, 1);

    /// <summary>The highlight's exponent for a material's reflectivity; never 0, where the highlight is undefined.</summary>
    internal float Exponent(float reflectivity) => Math.Max(0.05f, Highlight.Exponent(reflectivity) * Factor(HighlightSharpness, 1));

    // A factor that is not a number or is negative leaves the scene as it is.
    private static float Factor(float value, float fallback) => float.IsFinite(value) && value >= 0 ? value : fallback;
}
