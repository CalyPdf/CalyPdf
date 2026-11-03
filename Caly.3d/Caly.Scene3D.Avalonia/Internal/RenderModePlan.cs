using Caly.Rendering.Scene3D;

namespace Caly.Scene3D.Avalonia.Internal;

/// <summary>How the surfaces of a scene are drawn in a render mode.</summary>
internal enum SurfacePass
{
    /// <summary>Not at all.</summary>
    None,

    /// <summary>Lit and textured, as the materials say.</summary>
    Lit,

    /// <summary>Lit and textured, with the view's added transparency, blended.</summary>
    Transparent,

    /// <summary>Unlit, all in the view's face colour.</summary>
    Flat,

    /// <summary>Into depth only, so that what is behind them is hidden.</summary>
    DepthOnly
}

/// <summary>How edges or points are coloured in a render mode.</summary>
internal enum MarkPass
{
    /// <summary>Not drawn.</summary>
    None,

    /// <summary>In the view's edge colour.</summary>
    Flat,

    /// <summary>In the colour of the surface they belong to, lit.</summary>
    Shaded
}

/// <summary>
/// What a render mode (ISO 32000-2, Table 319) draws, pass by pass: surfaces, silhouette edges, every edge,
/// vertices, the boxes of the nodes.
/// </summary>
/// <param name="Surfaces">How the surfaces are drawn.</param>
/// <param name="Silhouettes">Whether silhouette edges (<see cref="OutlineEdges"/>) are drawn over them.</param>
/// <param name="Edges">How every edge of every triangle (<see cref="WireEdges"/>) is drawn.</param>
/// <param name="Points">How vertices are drawn.</param>
/// <param name="BoxEdges">Whether the edges of each node's bounding box are drawn, in the edge colour.</param>
/// <param name="BoxFaces">Whether the faces of each node's bounding box are drawn, in the face colour, with the added transparency.</param>
/// <param name="AddedEmissive">A share of each surface's own colour added to its light, so that no part is left dark.</param>
internal readonly record struct RenderModePlan(
    SurfacePass Surfaces, bool Silhouettes, MarkPass Edges, MarkPass Points, bool BoxEdges, bool BoxFaces, float AddedEmissive)
{
    /// <summary>
    /// The emissive term of ShadedIllustration, which the standard describes ("to remove poorly lit areas")
    /// without a number: a third of the surface's colour lifts the side away from the lights to about where
    /// the headlamp's ambient term leaves the lit side of a dark part.
    /// </summary>
    public const float IllustrationEmissive = 0.3f;

    /// <summary>Whether the mode needs edges found from the geometry: silhouettes or every edge.</summary>
    public bool NeedsEdges => Silhouettes || Edges != MarkPass.None;

    public static RenderModePlan For(SceneRenderMode mode) => mode switch
    {
        SceneRenderMode.SolidWireframe => new(SurfacePass.Lit, false, MarkPass.Flat, MarkPass.None, false, false, 0),
        SceneRenderMode.Transparent => new(SurfacePass.Transparent, false, MarkPass.None, MarkPass.None, false, false, 0),
        SceneRenderMode.TransparentWireframe => new(SurfacePass.Transparent, false, MarkPass.Flat, MarkPass.None, false, false, 0),
        SceneRenderMode.BoundingBox => new(SurfacePass.None, false, MarkPass.None, MarkPass.None, true, false, 0),
        SceneRenderMode.TransparentBoundingBox => new(SurfacePass.None, false, MarkPass.None, MarkPass.None, false, true, 0),
        SceneRenderMode.TransparentBoundingBoxOutline => new(SurfacePass.None, false, MarkPass.None, MarkPass.None, true, true, 0),
        SceneRenderMode.Wireframe => new(SurfacePass.None, false, MarkPass.Flat, MarkPass.None, false, false, 0),
        SceneRenderMode.ShadedWireframe => new(SurfacePass.None, false, MarkPass.Shaded, MarkPass.None, false, false, 0),
        SceneRenderMode.HiddenWireframe => new(SurfacePass.DepthOnly, false, MarkPass.Flat, MarkPass.None, false, false, 0),
        SceneRenderMode.Vertices => new(SurfacePass.None, false, MarkPass.None, MarkPass.Flat, false, false, 0),
        SceneRenderMode.ShadedVertices => new(SurfacePass.None, false, MarkPass.None, MarkPass.Shaded, false, false, 0),
        SceneRenderMode.Illustration => new(SurfacePass.Flat, true, MarkPass.None, MarkPass.None, false, false, 0),
        SceneRenderMode.SolidOutline => new(SurfacePass.Lit, true, MarkPass.None, MarkPass.None, false, false, 0),
        SceneRenderMode.ShadedIllustration => new(SurfacePass.Lit, true, MarkPass.None, MarkPass.None, false, false, IllustrationEmissive),
        _ => new(SurfacePass.Lit, false, MarkPass.None, MarkPass.None, false, false, 0)
    };
}
