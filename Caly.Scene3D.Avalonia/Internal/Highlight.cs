namespace Caly.Scene3D.Avalonia.Internal;

/// <summary>How sharp a material's specular highlight is.</summary>
internal static class Highlight
{
    /// <summary>
    /// The exponent applied to the cosine of the angle off the mirror direction, from a material's
    /// <see cref="Caly.Rendering.Scene3D.SceneMaterial.Reflectivity"/> (0 to 1).
    /// <para>
    /// A quarter of OpenGL's range of 128, and never below 1: measured against the pictures the exporters
    /// drew of their own models (Tetra4D, SpinFire), whose highlights are broad. A material of reflectivity
    /// 0.08 (the Saw's handle) has an exponent of about 2.6 there, where 1 + 127 × reflectivity gave 11 and
    /// left a faint spot of the highlight its specular colour of 0.8 asks for.
    /// </para>
    /// </summary>
    public static float Exponent(float reflectivity)
    {
        float unit = float.IsNaN(reflectivity) ? 0 : Math.Clamp(reflectivity, 0f, 1f);
        return Math.Max(1f, 32f * unit);
    }
}
