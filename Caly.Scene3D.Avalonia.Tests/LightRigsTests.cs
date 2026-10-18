using System.Numerics;
using Caly.Scene3D.Avalonia.Internal;
using Caly.Rendering.Scene3D;

namespace Caly.Scene3D.Avalonia.Tests;

public class LightRigsTests
{
    private static readonly Matrix4x4 FromFront = Matrix4x4.CreateLookAt(new Vector3(0, -10, 0), Vector3.Zero, Vector3.UnitZ);

    private static (ShaderLight[] Lights, Vector3 Ambient) Build(SceneLighting scheme, Scene scene, Matrix4x4 view)
    {
        var lights = new ShaderLight[LightRigs.MaxLights];
        int count = LightRigs.Build(scheme, scene, view, lights, out Vector3 ambient);
        return (lights[..count], ambient);
    }

    private static void AssertClose(Vector3 expected, Vector3 actual)
    {
        Assert.True(Vector3.Distance(expected, actual) < 1e-5f, $"expected {expected}, got {actual}");
    }

    [Theory]
    [InlineData(SceneLighting.None, 0)]
    [InlineData(SceneLighting.White, 3)]
    [InlineData(SceneLighting.Day, 3)]
    [InlineData(SceneLighting.Night, 3)]
    [InlineData(SceneLighting.Hard, 3)]
    [InlineData(SceneLighting.Primary, 3)]
    [InlineData(SceneLighting.Blue, 3)]
    [InlineData(SceneLighting.Red, 3)]
    [InlineData(SceneLighting.Cube, 6)]
    [InlineData(SceneLighting.Cad, 4)]
    [InlineData(SceneLighting.Headlamp, 1)]
    public void EveryFixedSchemeHasItsLights(SceneLighting scheme, int expected)
    {
        using Scene scene = TestScenes.Box();
        (ShaderLight[] lights, Vector3 ambient) = Build(scheme, scene, FromFront);

        Assert.Equal(expected, lights.Length);
        foreach (ShaderLight light in lights)
        {
            // All infinite, unit direction, no attenuation, not a spot.
            Assert.Equal(0f, light.Position.W);
            Assert.Equal(1f, new Vector3(light.Position.X, light.Position.Y, light.Position.Z).Length(), 1e-5f);
            Assert.Equal(new Vector3(1, 0, 0), light.Attenuation);
            Assert.Equal(-1f, light.SpotCosine);
            Assert.True(light.Color.X >= 0 && light.Color.Y >= 0 && light.Color.Z >= 0 && light.Color.Length() > 0);
        }

        // ISO 32000-2, Table 321: only Hard and Headlamp have an ambient term.
        Vector3 expectedAmbient = scheme switch { SceneLighting.Hard => new Vector3(0.5f), SceneLighting.Headlamp => new Vector3(0.1f), _ => Vector3.Zero };
        Assert.Equal(expectedAmbient, ambient);
    }

    [Fact]
    public void TheTableValuesAreUsed()
    {
        using Scene scene = TestScenes.Box();

        // With the identity as the view, camera space is world space.
        (ShaderLight[] day, _) = Build(SceneLighting.Day, scene, Matrix4x4.Identity);
        Assert.Equal(new Vector3(0.5f), day[0].Color);
        Assert.Equal(new Vector3(0.8f, 0.8f, 0.9f), day[1].Color);
        Assert.Equal(new Vector3(0.9f), day[2].Color);
        AssertClose(Vector3.Normalize(new Vector3(-2, -1.5f, -0.5f)), new Vector3(day[0].Position.X, day[0].Position.Y, day[0].Position.Z));
        AssertClose(Vector3.Normalize(new Vector3(2, 1.1f, -2.5f)), new Vector3(day[1].Position.X, day[1].Position.Y, day[1].Position.Z));
        AssertClose(Vector3.Normalize(new Vector3(0.02f, 0.01f, 2)), new Vector3(day[2].Position.X, day[2].Position.Y, day[2].Position.Z));

        (ShaderLight[] cad, _) = Build(SceneLighting.Cad, scene, Matrix4x4.Identity);
        Assert.Equal(new Vector3(0.72f, 0.72f, 0.81f), cad[0].Color);
        Assert.Equal(new Vector3(0.36f), cad[3].Color);
    }

    [Fact]
    public void WorldLightsTurnWithTheSceneAndTheCameraLightDoesNot()
    {
        using Scene scene = TestScenes.Box();
        Matrix4x4 fromSide = Matrix4x4.CreateLookAt(new Vector3(10, 0, 0), Vector3.Zero, Vector3.UnitZ);

        (ShaderLight[] front, _) = Build(SceneLighting.Cad, scene, FromFront);
        (ShaderLight[] side, _) = Build(SceneLighting.Cad, scene, fromSide);

        // The light attached to the camera comes from behind the viewer in both.
        Assert.Equal(new Vector4(0, 0, 1, 0), front[0].Position);
        Assert.Equal(new Vector4(0, 0, 1, 0), side[0].Position);

        // The others are fixed in the world: the same world direction, different camera-space directions.
        Assert.NotEqual(front[3].Position, side[3].Position);
        Matrix4x4.Invert(FromFront, out Matrix4x4 frontToWorld);
        Matrix4x4.Invert(fromSide, out Matrix4x4 sideToWorld);
        AssertClose(
            Vector3.TransformNormal(new Vector3(front[3].Position.X, front[3].Position.Y, front[3].Position.Z), frontToWorld),
            Vector3.TransformNormal(new Vector3(side[3].Position.X, side[3].Position.Y, side[3].Position.Z), sideToWorld));
    }

    private static Scene WithLights(params (SceneLight Light, Matrix4x4 Local)[] lights)
    {
        using var builder = new SceneBuilder();
        int name = builder.AddName("Light");
        foreach ((SceneLight light, Matrix4x4 local) in lights)
        {
            builder.AddNode(new SceneNode { Parent = -1, Local = local, Kind = SceneNodeKind.Light, Mesh = -1, Light = builder.AddLight(light), Camera = -1, Name = name, Visible = true });
        }

        return builder.Build();
    }

    [Fact]
    public void ArtworkLightingTakesTheScenesLights()
    {
        var white = new Vector3(1, 1, 1);
        using Scene scene = WithLights(
            (new SceneLight { Kind = SceneLightKind.Ambient, Enabled = true, Color = new Vector3(0.2f, 0.3f, 0.4f), Intensity = 0.5f }, Matrix4x4.Identity),
            (new SceneLight { Kind = SceneLightKind.Directional, Enabled = true, Color = white, Intensity = 2 }, Matrix4x4.Identity),
            (new SceneLight { Kind = SceneLightKind.Point, Enabled = true, Color = white, Intensity = 1, AttenuationConstant = 1, AttenuationLinear = 0.5f, AttenuationQuadratic = 0.25f }, Matrix4x4.CreateTranslation(1, 2, 3)),
            (new SceneLight { Kind = SceneLightKind.Spot, Enabled = true, Color = white, Intensity = 1, AttenuationConstant = 1, SpotAngle = 90 }, Matrix4x4.CreateTranslation(0, 0, 5)),
            (new SceneLight { Kind = SceneLightKind.Directional, Enabled = false, Color = white, Intensity = 1 }, Matrix4x4.Identity));

        (ShaderLight[] lights, Vector3 ambient) = Build(SceneLighting.Artwork, scene, Matrix4x4.Identity);

        AssertClose(new Vector3(0.1f, 0.15f, 0.2f), ambient);
        Assert.Equal(3, lights.Length);

        // A directional light shines along its node's −Z: the light is towards +Z.
        Assert.Equal(new Vector4(0, 0, 1, 0), lights[0].Position);
        Assert.Equal(new Vector3(2, 2, 2), lights[0].Color);

        Assert.Equal(new Vector4(1, 2, 3, 1), lights[1].Position);
        Assert.Equal(new Vector3(1, 0.5f, 0.25f), lights[1].Attenuation);
        Assert.Equal(-1f, lights[1].SpotCosine);

        Assert.Equal(new Vector4(0, 0, 5, 1), lights[2].Position);
        AssertClose(-Vector3.UnitZ, lights[2].SpotDirection);
        Assert.Equal(MathF.Cos(MathF.PI / 4), lights[2].SpotCosine, 1e-6f);
    }

    [Fact]
    public void ASceneThatLightsNothingFromADirectionGetsTheHeadlamp()
    {
        using Scene none = TestScenes.Box();
        (ShaderLight[] lights, Vector3 ambient) = Build(SceneLighting.Artwork, none, FromFront);
        Assert.Equal(new Vector4(0, 0, 1, 0), Assert.Single(lights).Position);
        Assert.Equal(new Vector3(0.1f), ambient);

        // Ambient light alone would draw every model as a flat silhouette.
        using Scene ambientOnly = WithLights((new SceneLight { Kind = SceneLightKind.Ambient, Enabled = true, Color = new Vector3(0.75f), Intensity = 1 }, Matrix4x4.Identity));
        (lights, ambient) = Build(SceneLighting.Artwork, ambientOnly, FromFront);
        Assert.Single(lights);
        AssertClose(new Vector3(0.85f), ambient);
    }

    [Fact]
    public void LightsBeyondTheShadersEightAndBrokenLightsAreLeftOut()
    {
        var lights = new List<(SceneLight, Matrix4x4)>();
        for (int i = 0; i < 12; i++)
        {
            lights.Add((new SceneLight { Kind = SceneLightKind.Point, Enabled = true, Color = Vector3.One, Intensity = 1, AttenuationConstant = 1 }, Matrix4x4.CreateTranslation(i, 0, 0)));
        }

        using Scene many = WithLights(lights.ToArray());
        Assert.Equal(LightRigs.MaxLights, Build(SceneLighting.Artwork, many, Matrix4x4.Identity).Lights.Length);

        using Scene broken = WithLights(
            (new SceneLight { Kind = SceneLightKind.Point, Enabled = true, Color = new Vector3(float.NaN, 1, 1), Intensity = 1, AttenuationConstant = 1 }, Matrix4x4.Identity),
            (new SceneLight { Kind = SceneLightKind.Point, Enabled = true, Color = Vector3.One, Intensity = 1 }, Matrix4x4.Identity));
        ShaderLight only = Assert.Single(Build(SceneLighting.Artwork, broken, Matrix4x4.Identity).Lights);

        // No attenuation factors at all would divide by zero: no attenuation instead.
        Assert.Equal(new Vector3(1, 0, 0), only.Attenuation);
    }
}
