using System.Numerics;
using Scene3D;

namespace Caly.Avalonia.Scene3D.Internal;

/// <summary>One light as the shader takes it, in camera space.</summary>
internal struct ShaderLight
{
    /// <summary>W = 0: the unit direction towards an infinitely distant light. W = 1: the light's position.</summary>
    public Vector4 Position;

    public Vector3 Color;

    /// <summary>Constant, linear and quadratic attenuation with distance; (1, 0, 0) for none.</summary>
    public Vector3 Attenuation;

    /// <summary>Unit direction a spot light shines in.</summary>
    public Vector3 SpotDirection;

    /// <summary>Cosine of half a spot light's cone; −1 for a light that shines every way.</summary>
    public float SpotCosine;
}

/// <summary>
/// The lights of each lighting scheme. The fixed schemes are those of ISO 32000-2, Table 321, with its
/// colours and directions; <see cref="SceneLighting.Artwork"/> takes the lights authored in the scene.
/// </summary>
internal static class LightRigs
{
    public const int MaxLights = 8;

    /// <summary>A fixed light: its colour and the direction towards it in world space, or none for a light attached to the camera.</summary>
    private readonly record struct RigLight(Vector3 Color, Vector3? Direction);

    private static readonly Vector3 Back = new(-2f, -1.5f, -0.5f), Fill = new(2f, 1.1f, -2.5f), Key = new(0f, 0f, 2f);

    private static readonly RigLight[] White = [new(new(0.38f, 0.38f, 0.45f), Back), new(new(0.6f, 0.6f, 0.67f), Fill), new(new(0.5f, 0.5f, 0.57f), new Vector3(-0.5f, 0f, 2f))];
    private static readonly RigLight[] Day = [new(new(0.5f), Back), new(new(0.8f, 0.8f, 0.9f), Fill), new(new(0.9f), new Vector3(0.02f, 0.01f, 2f))];
    private static readonly RigLight[] Night = [new(new(1f, 0.75f, 0.39f), Back), new(new(0.31f, 0.47f, 0.55f), Fill), new(new(0.5f, 0.5f, 1f), Key)];
    private static readonly RigLight[] Hard = [new(new(0.5f), new Vector3(-1.5f)), new(new(0.8f, 0.8f, 0.9f), new Vector3(1.5f, 1.5f, -1.5f)), new(new(0.9f), new Vector3(-0.5f, 0f, 2f))];
    private static readonly RigLight[] Primary = [new(new(1f, 0.2f, 0.5f), Back), new(new(0.2f, 1f, 0.5f), Fill), new(new(0f, 0f, 1f), Key)];
    private static readonly RigLight[] Blue = [new(new(0.4f, 0.4f, 0.7f), Back), new(new(0.75f, 0.75f, 0.95f), Fill), new(new(0.7f, 0.7f, 0.95f), Key)];
    private static readonly RigLight[] Red = [new(new(0.8f, 0.3f, 0.4f), Back), new(new(0.95f, 0.5f, 0.7f), Fill), new(new(0.95f, 0.4f, 0.5f), Key)];

    private static readonly RigLight[] Cube =
    [
        new(new(0.4f), new Vector3(1f, 0.01f, 0.01f)), new(new(0.4f), new Vector3(0.01f, 1f, 0.01f)), new(new(0.4f), new Vector3(0.01f, 0.01f, 1f)),
        new(new(0.4f), new Vector3(-1f, 0.01f, 0.01f)), new(new(0.4f), new Vector3(0.01f, -1f, 0.01f)), new(new(0.4f), new Vector3(0.01f, 0.01f, -1f))
    ];

    private static readonly RigLight[] Cad = [new(new(0.72f, 0.72f, 0.81f), null), new(new(0.2f), Back), new(new(0.32f), Fill), new(new(0.36f), new Vector3(0.04f, 0.01f, 2f))];
    private static readonly RigLight[] Headlamp = [new(new(0.8f, 0.8f, 0.9f), null)];

    private static readonly Vector3 HardAmbient = new(0.5f), HeadlampAmbient = new(0.1f);

    /// <summary>
    /// Fills <paramref name="lights"/> (room for <see cref="MaxLights"/>) for a scheme and returns how many
    /// there are. <paramref name="view"/> is the world-to-camera matrix.
    /// </summary>
    public static int Build(SceneLighting scheme, Scene scene, in Matrix4x4 view, Span<ShaderLight> lights, out Vector3 ambient)
    {
        ambient = Vector3.Zero;
        switch (scheme)
        {
            case SceneLighting.None:
                return 0;
            case SceneLighting.White:
                return Fixed(White, view, lights);
            case SceneLighting.Day:
                return Fixed(Day, view, lights);
            case SceneLighting.Night:
                return Fixed(Night, view, lights);
            case SceneLighting.Hard:
                ambient = HardAmbient;
                return Fixed(Hard, view, lights);
            case SceneLighting.Primary:
                return Fixed(Primary, view, lights);
            case SceneLighting.Blue:
                return Fixed(Blue, view, lights);
            case SceneLighting.Red:
                return Fixed(Red, view, lights);
            case SceneLighting.Cube:
                return Fixed(Cube, view, lights);
            case SceneLighting.Cad:
                return Fixed(Cad, view, lights);
            case SceneLighting.Headlamp:
                ambient = HeadlampAmbient;
                return Fixed(Headlamp, view, lights);
            default:
                return FromScene(scene, view, lights, out ambient);
        }
    }

    private static int Fixed(RigLight[] rig, in Matrix4x4 view, Span<ShaderLight> lights)
    {
        for (int i = 0; i < rig.Length; i++)
        {
            // A light attached to the camera shines the way the camera looks: it is behind the viewer.
            Vector3 towards = rig[i].Direction is { } world ? Vector3.Normalize(Vector3.TransformNormal(world, view)) : Vector3.UnitZ;
            lights[i] = Infinite(towards, rig[i].Color);
        }

        return rig.Length;
    }

    private static ShaderLight Infinite(Vector3 towards, Vector3 color)
    {
        return new ShaderLight { Position = new Vector4(towards, 0), Color = color, Attenuation = new Vector3(1, 0, 0), SpotDirection = -Vector3.UnitZ, SpotCosine = -1 };
    }

    /// <summary>
    /// The scene's own lights. A light node shines along its −Z axis, like a camera node looks. A scene
    /// that lights nothing from a direction (no lights, or ambient light only) gets the headlamp, so that
    /// a model is never a flat silhouette.
    /// </summary>
    private static int FromScene(Scene scene, in Matrix4x4 view, Span<ShaderLight> lights, out Vector3 ambient)
    {
        ambient = Vector3.Zero;
        int count = 0;
        ReadOnlySpan<SceneNode> nodes = scene.Nodes;
        ReadOnlySpan<SceneLight> sceneLights = scene.Lights;

        for (int i = 0; i < nodes.Length; i++)
        {
            int index = nodes[i].Light;
            if ((uint)index >= (uint)sceneLights.Length || !sceneLights[index].Enabled)
            {
                continue;
            }

            SceneLight light = sceneLights[index];
            Vector3 color = light.Color * light.Intensity;
            if (!IsFinite(color))
            {
                continue;
            }

            if (light.Kind == SceneLightKind.Ambient)
            {
                ambient += color;
                continue;
            }

            if (count == MaxLights)
            {
                continue;
            }

            Matrix4x4 toCamera = scene.GetWorldMatrix(i) * view;
            Vector3 axis = Vector3.TransformNormal(Vector3.UnitZ, toCamera);
            Vector3 position = toCamera.Translation;
            if (!(axis.LengthSquared() > 0) || !IsFinite(axis) || !IsFinite(position))
            {
                continue;
            }

            axis = Vector3.Normalize(axis);
            if (light.Kind == SceneLightKind.Directional)
            {
                lights[count++] = Infinite(axis, color);
                continue;
            }

            var attenuation = new Vector3(light.AttenuationConstant, light.AttenuationLinear, light.AttenuationQuadratic);
            if (!IsFinite(attenuation) || attenuation.X < 0 || attenuation.Y < 0 || attenuation.Z < 0 || attenuation == Vector3.Zero)
            {
                attenuation = new Vector3(1, 0, 0);
            }

            // A spot's angle is the whole cone.
            float spotCosine = -1;
            if (light.Kind == SceneLightKind.Spot && light.SpotAngle > 0 && light.SpotAngle < 360)
            {
                spotCosine = MathF.Cos(light.SpotAngle * MathF.PI / 360);
            }

            lights[count++] = new ShaderLight { Position = new Vector4(position, 1), Color = color, Attenuation = attenuation, SpotDirection = -axis, SpotCosine = spotCosine };
        }

        if (count == 0)
        {
            ambient += HeadlampAmbient;
            return Fixed(Headlamp, view, lights);
        }

        return count;
    }

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
