using System.Numerics;
using Caly.Scene3D.Avalonia.Internal;
using Caly.Rendering.Scene3D;

namespace Caly.Scene3D.Avalonia.Tests;

public class ViewCameraTests
{
    private static readonly Vector2 Wide = new(400, 100);
    private static readonly Vector2 Tall = new(100, 400);

    private static void AssertClose(Vector3 expected, Vector3 actual, float tolerance = 1e-4f)
    {
        Assert.True(Vector3.Distance(expected, actual) <= tolerance, $"expected {expected}, got {actual}");
    }

    private static bool IsFinite(Matrix4x4 m)
    {
        return float.IsFinite(m.M11 + m.M12 + m.M13 + m.M14 + m.M21 + m.M22 + m.M23 + m.M24 + m.M31 + m.M32 + m.M33 + m.M34 + m.M41 + m.M42 + m.M43 + m.M44);
    }

    /// <summary>Normalised device coordinates of a world point: x right, y up, each −1 to 1 inside the view.</summary>
    private static Vector3 Ndc(Vector3 world, Matrix4x4 view, Matrix4x4 projection)
    {
        Vector4 clip = Vector4.Transform(new Vector4(world, 1), view * projection);
        return new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
    }

    private static Matrix4x4 Projection(SceneViewProjection projection, Vector2 size, ViewRegion region, float near = 0.1f, float far = 1000)
    {
        Assert.True(ViewCamera.TryProjectionMatrix(projection, size, region, near, far, out Matrix4x4 matrix));
        return matrix;
    }

    // ---- which camera ----

    [Fact]
    public void ANamedCameraNodeIsPreferredToAMatrix()
    {
        Matrix4x4 node = TestScenes.LookFrom(new Vector3(0, -20, 0), Vector3.Zero, Vector3.UnitZ);
        Matrix4x4 matrix = TestScenes.LookFrom(new Vector3(30, 0, 0), Vector3.Zero, Vector3.UnitZ);
        using Scene scene = TestScenes.Box(TestScenes.LookFrom(new Vector3(9, 9, 9), Vector3.Zero, Vector3.UnitZ), node);

        CameraPose pose = ViewCamera.ResolvePose(scene, new SceneView { CameraNodePath = ["Camera1"], CameraToWorld = matrix }, out _);
        AssertClose(new Vector3(0, -20, 0), pose.Position);
        AssertClose(Vector3.UnitY, pose.Forward);
        AssertClose(Vector3.UnitZ, pose.Up);

        // A path that finds nothing falls back to the matrix.
        pose = ViewCamera.ResolvePose(scene, new SceneView { CameraNodePath = ["Nowhere"], CameraToWorld = matrix }, out _);
        AssertClose(new Vector3(30, 0, 0), pose.Position);
        AssertClose(-Vector3.UnitX, pose.Forward);
    }

    [Fact]
    public void WithoutAViewTheScenesFirstCameraIsUsedWithItsProjection()
    {
        using Scene scene = TestScenes.Box(TestScenes.LookFrom(new Vector3(0, -20, 0), Vector3.Zero, Vector3.UnitZ));

        CameraPose pose = ViewCamera.ResolvePose(scene, null, out SceneViewProjection projection);

        AssertClose(new Vector3(0, -20, 0), pose.Position);
        Assert.Equal((SceneProjection.Perspective, 40f, SceneViewBinding.Height), (projection.Kind, projection.FieldOfView, projection.Binding));

        // The orbit point is where the view axis passes the centre of the scene.
        Assert.Equal(20f, pose.OrbitDistance, 1e-4f);
    }

    [Fact]
    public void AViewsOwnProjectionAndOrbitDistanceWin()
    {
        using Scene scene = TestScenes.Box(TestScenes.LookFrom(new Vector3(0, -20, 0), Vector3.Zero, Vector3.UnitZ));
        var wanted = new SceneViewProjection(SceneProjection.Orthographic, 0, SceneViewBinding.Width, 0.25f, null, null);

        CameraPose pose = ViewCamera.ResolvePose(scene, new SceneView { CameraNodePath = ["Camera0"], Projection = wanted, OrbitDistance = 7 }, out SceneViewProjection projection);

        Assert.Equal(wanted, projection);
        Assert.Equal(7f, pose.OrbitDistance);
    }

    [Fact]
    public void WithoutAnyCameraTheWholeSceneIsFramed()
    {
        using Scene scene = TestScenes.Box();

        CameraPose pose = ViewCamera.ResolvePose(scene, null, out SceneViewProjection projection);
        Matrix4x4 view = ViewCamera.ViewMatrix(pose);
        (float near, float far) = ViewCamera.ClipPlanes(pose, projection, scene.ComputeWorldBounds());

        foreach (Vector2 size in new[] { Wide, Tall, new Vector2(300, 300) })
        {
            Matrix4x4 p = Projection(projection, size, ViewRegion.Full, near, far);
            for (int corner = 0; corner < 8; corner++)
            {
                var point = new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -2 : 2, (corner & 4) == 0 ? -3 : 3);
                Vector3 ndc = Ndc(point, view, p);
                Assert.True(MathF.Abs(ndc.X) <= 1 && MathF.Abs(ndc.Y) <= 1 && MathF.Abs(ndc.Z) <= 1, $"corner {point} is outside the view at {size}: {ndc}");
            }
        }

        // Looking at the middle of the box, from above the ground plane, upright.
        AssertClose(Vector3.Zero, pose.OrbitPoint, 1e-3f);
        Assert.True(pose.Position.Z > 0);
        Assert.True(pose.Up.Z > 0.5f);
    }

    /// <summary>Review focus: a camera that is not a camera is passed over.</summary>
    [Fact]
    public void ADegenerateCameraFallsThroughToTheNextSource()
    {
        Matrix4x4 good = TestScenes.LookFrom(new Vector3(0, -20, 0), Vector3.Zero, Vector3.UnitZ);
        Matrix4x4 nan = good;
        nan.M22 = float.NaN;
        Matrix4x4 infinite = good;
        infinite.M41 = float.PositiveInfinity;
        var collapsed = new Matrix4x4(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3, 1);
        var flat = new Matrix4x4(1, 0, 0, 0, 2, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1); // X and Y axes parallel

        using Scene scene = TestScenes.Box(good);
        foreach (Matrix4x4 bad in new[] { nan, infinite, collapsed, flat })
        {
            CameraPose pose = ViewCamera.ResolvePose(scene, new SceneView { CameraToWorld = bad }, out SceneViewProjection projection);

            AssertClose(new Vector3(0, -20, 0), pose.Position);
            Assert.True(pose.IsFinite);
            Assert.True(IsFinite(ViewCamera.ViewMatrix(pose)));
            Assert.True(ViewCamera.IsValid(projection));
        }

        // The same matrices in a camera node: the default framing is all that is left.
        foreach (Matrix4x4 bad in new[] { collapsed, flat })
        {
            using Scene broken = TestScenes.Box(bad);
            CameraPose pose = ViewCamera.ResolvePose(broken, null, out _);
            Assert.True(pose.IsFinite);
            AssertClose(Vector3.Zero, pose.OrbitPoint, 1e-3f);
        }
    }

    [Fact]
    public void AnUnusableProjectionOrOrbitDistanceIsReplaced()
    {
        using Scene scene = TestScenes.Box(TestScenes.LookFrom(new Vector3(0, -20, 0), Vector3.Zero, Vector3.UnitZ));

        foreach (SceneViewProjection bad in new[]
        {
            new SceneViewProjection(SceneProjection.Perspective, 0, SceneViewBinding.Width, 1, null, null),
            new SceneViewProjection(SceneProjection.Perspective, 180, SceneViewBinding.Width, 1, null, null),
            new SceneViewProjection(SceneProjection.Perspective, float.NaN, SceneViewBinding.Width, 1, null, null),
            new SceneViewProjection(SceneProjection.Orthographic, 0, SceneViewBinding.Width, 0, null, null),
            new SceneViewProjection(SceneProjection.Orthographic, 0, SceneViewBinding.Width, float.PositiveInfinity, null, null)
        })
        {
            Assert.False(ViewCamera.IsValid(bad));
            CameraPose pose = ViewCamera.ResolvePose(scene, new SceneView { Projection = bad, OrbitDistance = float.NaN }, out SceneViewProjection projection);

            // The camera node's own projection takes over.
            Assert.Equal(40f, projection.FieldOfView);
            Assert.Equal(20f, pose.OrbitDistance, 1e-4f);
        }

        CameraPose negative = ViewCamera.ResolvePose(scene, new SceneView { OrbitDistance = -3 }, out _);
        Assert.Equal(20f, negative.OrbitDistance, 1e-4f);
    }

    /// <summary>Review focus: nothing to look at is still something to draw (the background).</summary>
    [Fact]
    public void AnEmptySceneGivesFiniteMatrices()
    {
        using Scene scene = TestScenes.Empty();

        CameraPose pose = ViewCamera.ResolvePose(scene, null, out SceneViewProjection projection);
        (float near, float far) = ViewCamera.ClipPlanes(pose, projection, scene.ComputeWorldBounds());

        Assert.True(pose.IsFinite);
        Assert.True(pose.OrbitDistance > 0);
        Assert.True(near > 0 && far > near && float.IsFinite(far));
        Assert.True(IsFinite(ViewCamera.ViewMatrix(pose)));
        Assert.True(IsFinite(Projection(projection, Wide, ViewRegion.Full, near, far)));
    }

    // ---- the view matrix ----

    [Fact]
    public void TheOrbitPointIsAtTheCentreOfTheView()
    {
        using Scene scene = TestScenes.Box();
        CameraPose pose = ViewCamera.ResolvePose(scene, new SceneView { CameraToWorld = TestScenes.LookFrom(new Vector3(3, -8, 5), new Vector3(1, 1, 1), Vector3.UnitZ), OrbitDistance = 4 }, out SceneViewProjection projection);

        Matrix4x4 view = ViewCamera.ViewMatrix(pose);
        Vector3 ndc = Ndc(pose.OrbitPoint, view, Projection(projection, Wide, ViewRegion.Full));

        Assert.Equal(0f, ndc.X, 1e-5f);
        Assert.Equal(0f, ndc.Y, 1e-5f);
        Assert.Equal(4f, Vector3.Distance(pose.Position, pose.OrbitPoint), 1e-4f);

        // In camera space the scene is in front, along −Z.
        Assert.Equal(-4f, Vector3.Transform(pose.OrbitPoint, view).Z, 1e-4f);
    }

    // ---- projections ----

    /// <summary>A camera at the origin looking along −Z; the point 30° off axis, 10 away.</summary>
    private static Vector3 OffAxis(float degreesRight, float degreesUp)
    {
        return new Vector3(10 * MathF.Tan(degreesRight * MathF.PI / 180), 10 * MathF.Tan(degreesUp * MathF.PI / 180), -10);
    }

    [Theory]
    [InlineData(SceneViewBinding.Width, 400, 100, true)]
    [InlineData(SceneViewBinding.Height, 400, 100, false)]
    [InlineData(SceneViewBinding.Min, 400, 100, false)]
    [InlineData(SceneViewBinding.Max, 400, 100, true)]
    [InlineData(SceneViewBinding.Width, 100, 400, true)]
    [InlineData(SceneViewBinding.Height, 100, 400, false)]
    [InlineData(SceneViewBinding.Min, 100, 400, true)]
    [InlineData(SceneViewBinding.Max, 100, 400, false)]
    public void TheFieldOfViewSpansTheExtentItIsBoundTo(SceneViewBinding binding, float width, float height, bool spansWidth)
    {
        var projection = new SceneViewProjection(SceneProjection.Perspective, 60, binding, 1, null, null);
        Matrix4x4 p = Projection(projection, new Vector2(width, height), ViewRegion.Full);

        // Half the field of view off axis is the edge of the bound extent; the other extent follows the aspect ratio.
        Vector3 right = Ndc(OffAxis(30, 0), Matrix4x4.Identity, p);
        Vector3 up = Ndc(OffAxis(0, 30), Matrix4x4.Identity, p);
        if (spansWidth)
        {
            Assert.Equal(1f, right.X, 1e-4f);
            Assert.Equal(width / height, up.Y, 1e-4f);
        }
        else
        {
            Assert.Equal(1f, up.Y, 1e-4f);
            Assert.Equal(height / width, right.X, 1e-4f);
        }
    }

    [Theory]
    [InlineData(SceneViewBinding.Width, true)]
    [InlineData(SceneViewBinding.Height, false)]
    [InlineData(SceneViewBinding.Min, false)]
    [InlineData(SceneViewBinding.Max, true)]
    public void AnOrthographicScaleShowsItsReciprocalAcrossTheBoundExtent(SceneViewBinding binding, bool spansWidth)
    {
        // Scale 0.25: the bound extent shows 4 scene units, 2 either side of the axis.
        var projection = new SceneViewProjection(SceneProjection.Orthographic, 0, binding, 0.25f, null, null);
        Matrix4x4 p = Projection(projection, Wide, ViewRegion.Full, -50, 50);

        Vector3 right = Ndc(new Vector3(2, 0, -10), Matrix4x4.Identity, p);
        Vector3 up = Ndc(new Vector3(0, 2, -30), Matrix4x4.Identity, p);
        if (spansWidth)
        {
            Assert.Equal(1f, right.X, 1e-5f);
            Assert.Equal(4f, up.Y, 1e-5f);
        }
        else
        {
            Assert.Equal(1f, up.Y, 1e-5f);
            Assert.Equal(0.25f, right.X, 1e-5f);
        }

        // No perspective: depth does not move a point on screen.
        Assert.Equal(Ndc(new Vector3(1, 1, -5), Matrix4x4.Identity, p).X, Ndc(new Vector3(1, 1, -40), Matrix4x4.Identity, p).X, 1e-6f);
    }

    [Fact]
    public void DepthRunsFromTheNearPlaneToTheFarPlane()
    {
        var perspective = new SceneViewProjection(SceneProjection.Perspective, 60, SceneViewBinding.Height, 1, null, null);
        Matrix4x4 p = Projection(perspective, Wide, ViewRegion.Full, 2, 50);
        Assert.Equal(-1f, Ndc(new Vector3(0, 0, -2), Matrix4x4.Identity, p).Z, 1e-5f);
        Assert.Equal(1f, Ndc(new Vector3(0, 0, -50), Matrix4x4.Identity, p).Z, 1e-4f);

        var orthographic = new SceneViewProjection(SceneProjection.Orthographic, 0, SceneViewBinding.Height, 1, null, null);
        Matrix4x4 o = Projection(orthographic, Wide, ViewRegion.Full, -5, 50);
        Assert.Equal(-1f, Ndc(new Vector3(0, 0, 5), Matrix4x4.Identity, o).Z, 1e-5f);
        Assert.Equal(1f, Ndc(new Vector3(0, 0, -50), Matrix4x4.Identity, o).Z, 1e-5f);
    }

    // ---- clip planes ----

    [Fact]
    public void AutomaticClipPlanesEncloseTheScene()
    {
        using Scene scene = TestScenes.Box();
        SceneBounds bounds = scene.ComputeWorldBounds();
        var perspective = new SceneViewProjection(SceneProjection.Perspective, 30, SceneViewBinding.Min, 1, null, null);

        // From outside: both planes hug the box.
        CameraPose outside = ViewCamera.ResolvePose(scene, new SceneView { CameraToWorld = TestScenes.LookFrom(new Vector3(0, -20, 0), Vector3.Zero, Vector3.UnitZ) }, out _);
        (float near, float far) = ViewCamera.ClipPlanes(outside, perspective, bounds);
        Assert.InRange(near, 17f, 18f);
        Assert.InRange(far, 22f, 23f);

        // From inside (a walk-through): the near plane stays in front of the camera, close.
        CameraPose inside = ViewCamera.ResolvePose(scene, new SceneView { CameraToWorld = TestScenes.LookFrom(new Vector3(0, 0, 0), new Vector3(0, 1, 0), Vector3.UnitZ) }, out _);
        (near, far) = ViewCamera.ClipPlanes(inside, perspective, bounds);
        Assert.True(near > 0 && near < 0.1f, $"near {near}");
        Assert.True(far >= 2f);

        // Looking away from everything: still a valid pair.
        CameraPose away = ViewCamera.ResolvePose(scene, new SceneView { CameraToWorld = TestScenes.LookFrom(new Vector3(0, -20, 0), new Vector3(0, -40, 0), Vector3.UnitZ) }, out _);
        (near, far) = ViewCamera.ClipPlanes(away, perspective, bounds);
        Assert.True(near > 0 && far > near && float.IsFinite(far));

        // Orthographic: what is behind the camera plane is still seen.
        var orthographic = new SceneViewProjection(SceneProjection.Orthographic, 0, SceneViewBinding.Min, 1, null, null);
        (near, far) = ViewCamera.ClipPlanes(inside, orthographic, bounds);
        Assert.True(near <= -2f && far >= 2f);
    }

    [Fact]
    public void ExplicitClipPlanesAreUsedWhenTheyMakeSense()
    {
        using Scene scene = TestScenes.Box();
        SceneBounds bounds = scene.ComputeWorldBounds();
        CameraPose pose = ViewCamera.ResolvePose(scene, new SceneView { CameraToWorld = TestScenes.LookFrom(new Vector3(0, -20, 0), Vector3.Zero, Vector3.UnitZ) }, out _);

        var both = new SceneViewProjection(SceneProjection.Perspective, 30, SceneViewBinding.Min, 1, 19, 20);
        Assert.Equal((19f, 20f), ViewCamera.ClipPlanes(pose, both, bounds));

        // Near only: the far plane is fitted.
        var nearOnly = new SceneViewProjection(SceneProjection.Perspective, 30, SceneViewBinding.Min, 1, 19, null);
        (float near, float far) = ViewCamera.ClipPlanes(pose, nearOnly, bounds);
        Assert.Equal(19f, near);
        Assert.InRange(far, 22f, 23f);

        // Nonsense is ignored.
        foreach ((float? n, float? f) in new (float?, float?)[] { (-1, 20), (0, 20), (30, 20), (float.NaN, float.NaN) })
        {
            (near, far) = ViewCamera.ClipPlanes(pose, new SceneViewProjection(SceneProjection.Perspective, 30, SceneViewBinding.Min, 1, n, f), bounds);
            Assert.True(near > 0 && far > near && float.IsFinite(far), $"({n}, {f}) gave ({near}, {far})");
        }
    }

    // ---- the visible region ----

    /// <summary>
    /// Drawing a quarter of the view into a quarter-sized surface must put every point exactly where
    /// drawing the whole view would: this is what lets a zoomed page render only what is on screen.
    /// </summary>
    [Theory]
    [InlineData(SceneProjection.Perspective)]
    [InlineData(SceneProjection.Orthographic)]
    public void ARegionOfTheViewProjectsPointsWhereTheWholeViewDoes(SceneProjection kind)
    {
        var projection = kind == SceneProjection.Perspective
            ? new SceneViewProjection(kind, 50, SceneViewBinding.Min, 1, null, null)
            : new SceneViewProjection(kind, 0, SceneViewBinding.Min, 0.1f, null, null);
        var size = new Vector2(800, 600);
        Matrix4x4 whole = Projection(projection, size, ViewRegion.Full, 1, 100);

        var regions = new[]
        {
            new ViewRegion(0, 0, 0.5f, 0.5f), new ViewRegion(0.5f, 0, 1, 0.5f), new ViewRegion(0, 0.5f, 0.5f, 1), new ViewRegion(0.5f, 0.5f, 1, 1),
            new ViewRegion(0.3f, 0.1f, 0.45f, 0.9f)
        };

        var random = new Random(7);
        foreach (ViewRegion region in regions)
        {
            Matrix4x4 part = Projection(projection, size, region, 1, 100);
            for (int i = 0; i < 50; i++)
            {
                var point = new Vector3((float)random.NextDouble() * 8 - 4, (float)random.NextDouble() * 8 - 4, -(float)random.NextDouble() * 60 - 2);

                // Pixel position in the whole view (y down), and in the region's own surface plus its offset.
                Vector3 a = Ndc(point, Matrix4x4.Identity, whole);
                var wholePixel = new Vector2((a.X + 1) / 2 * size.X, (1 - a.Y) / 2 * size.Y);

                Vector3 b = Ndc(point, Matrix4x4.Identity, part);
                var regionSize = new Vector2((region.Right - region.Left) * size.X, (region.Bottom - region.Top) * size.Y);
                var partPixel = new Vector2(region.Left * size.X + (b.X + 1) / 2 * regionSize.X, region.Top * size.Y + (1 - b.Y) / 2 * regionSize.Y);

                Assert.True(Vector2.Distance(wholePixel, partPixel) < 1e-2f, $"{region}: {point} lands at {partPixel}, whole view {wholePixel}");
                Assert.Equal(a.Z, b.Z, 1e-5f);
            }
        }
    }

    /// <summary>Review focus: nothing to draw into is not an error.</summary>
    [Fact]
    public void AViewOrRegionWithoutAreaHasNoProjection()
    {
        var projection = new SceneViewProjection(SceneProjection.Perspective, 30, SceneViewBinding.Min, 1, null, null);

        Assert.False(ViewCamera.TryProjectionMatrix(projection, new Vector2(0, 100), ViewRegion.Full, 1, 10, out _));
        Assert.False(ViewCamera.TryProjectionMatrix(projection, new Vector2(100, 0), ViewRegion.Full, 1, 10, out _));
        Assert.False(ViewCamera.TryProjectionMatrix(projection, new Vector2(float.NaN, 100), ViewRegion.Full, 1, 10, out _));
        Assert.False(ViewCamera.TryProjectionMatrix(projection, Wide, new ViewRegion(0.5f, 0, 0.5f, 1), 1, 10, out _));
        Assert.False(ViewCamera.TryProjectionMatrix(projection, Wide, new ViewRegion(0, 0.7f, 1, 0.2f), 1, 10, out _));
        Assert.False(ViewCamera.TryProjectionMatrix(projection, Wide, new ViewRegion(float.NaN, 0, 1, 1), 1, 10, out _));
        Assert.False(ViewCamera.TryProjectionMatrix(projection, Wide, ViewRegion.Full, 10, 10, out _));
        Assert.False(ViewCamera.TryProjectionMatrix(new SceneViewProjection(SceneProjection.Perspective, 0, SceneViewBinding.Min, 1, null, null), Wide, ViewRegion.Full, 1, 10, out _));

        Assert.True(ViewCamera.TryProjectionMatrix(projection, Wide, ViewRegion.Full, 1, 10, out Matrix4x4 matrix));
        Assert.True(IsFinite(matrix));
    }

    // ---- a camera carried by an animated node ----

    private static Scene Rigged() => TestScenes.RiggedCamera();

    [Fact]
    public void TheWorldMatrixOfANodeIncludesTheAnimationsOfItsAncestors()
    {
        using Scene scene = Rigged();

        Assert.True(ViewCamera.IsAnimatedNode(scene, 2));
        AssertClose(new Vector3(1, -20, 0), ViewCamera.AnimatedWorldMatrix(scene, 2, 0).Translation);
        AssertClose(new Vector3(6, -20, 0), ViewCamera.AnimatedWorldMatrix(scene, 2, 1).Translation);
        // The motion loops: 2.5 s is half a second into the second round.
        AssertClose(new Vector3(3.5f, -20, 0), ViewCamera.AnimatedWorldMatrix(scene, 2, 2.5).Translation);
    }

    [Fact]
    public void ANodeNothingMovesIsNotAnimated()
    {
        using Scene scene = TestScenes.Box(TestScenes.LookFrom(new Vector3(0, -20, 0), Vector3.Zero, Vector3.UnitZ));

        Assert.False(ViewCamera.IsAnimatedNode(scene, 2));
    }

    [Fact]
    public void ThePoseResolvedFromACameraNodeSaysWhichNode()
    {
        using Scene scene = Rigged();

        ViewCamera.ResolvePose(scene, null, out _, out int node);
        Assert.Equal(2, node);

        using Scene plain = TestScenes.Box();
        ViewCamera.ResolvePose(plain, null, out _, out int none);
        Assert.Equal(-1, none);
    }

    [Fact]
    public void ACarriedPoseMovesAndTurnsWithItsNode()
    {
        ViewCamera.TryPoseFromMatrix(TestScenes.LookFrom(new Vector3(0, -20, 0), Vector3.Zero, Vector3.UnitZ), out CameraPose pose);
        pose.OrbitDistance = 20;

        // The node turns a quarter about Z and moves 5 along X.
        CameraPose carried = ViewCamera.Carry(pose, Matrix4x4.CreateRotationZ(MathF.PI / 2) * Matrix4x4.CreateTranslation(5, 0, 0));

        // From (0, -20, 0) the quarter turn takes it to (20, 0, 0), then 5 along X; it looked along +Y, now along -X.
        AssertClose(new Vector3(25, 0, 0), carried.Position);
        AssertClose(new Vector3(-1, 0, 0), carried.Forward);
        AssertClose(Vector3.UnitZ, carried.Up);
        Assert.Equal(20, carried.OrbitDistance);
    }

    [Fact]
    public void CarryingByTheIdentityChangesNothing()
    {
        ViewCamera.TryPoseFromMatrix(TestScenes.LookFrom(new Vector3(3, -20, 2), Vector3.Zero, Vector3.UnitZ), out CameraPose pose);

        CameraPose same = ViewCamera.Carry(pose, Matrix4x4.Identity);

        AssertClose(pose.Position, same.Position);
        AssertClose(pose.Forward, same.Forward);
    }

    [Fact]
    public void ACarryThatCannotBeUsedLeavesThePoseAlone()
    {
        ViewCamera.TryPoseFromMatrix(TestScenes.LookFrom(new Vector3(3, -20, 2), Vector3.Zero, Vector3.UnitZ), out CameraPose pose);

        CameraPose same = ViewCamera.Carry(pose, new Matrix4x4());

        AssertClose(pose.Position, same.Position);
    }
}
