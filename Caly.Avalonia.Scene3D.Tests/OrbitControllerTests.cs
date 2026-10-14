using System.Numerics;
using Caly.Avalonia.Scene3D.Internal;
using Caly.Rendering.Scene3D;

namespace Caly.Avalonia.Scene3D.Tests;

public class OrbitControllerTests
{
    private static readonly Vector2 Size = new(800, 600);
    private static readonly SceneViewProjection Perspective = new(SceneProjection.Perspective, 40, SceneViewBinding.Height, 1, null, null);
    private static readonly SceneViewProjection Orthographic = new(SceneProjection.Orthographic, 0, SceneViewBinding.Height, 0.05f, null, null);

    private static CameraPose Start()
    {
        Assert.True(ViewCamera.TryPoseFromMatrix(TestScenes.LookFrom(new Vector3(3, -9, 4), new Vector3(1, 0, 1), Vector3.UnitZ), out CameraPose pose));
        pose.OrbitDistance = Vector3.Distance(new Vector3(3, -9, 4), new Vector3(1, 0, 1));
        return pose;
    }

    private static void AssertClose(Vector3 expected, Vector3 actual, float tolerance = 1e-3f)
    {
        Assert.True(Vector3.Distance(expected, actual) <= tolerance, $"expected {expected}, got {actual}");
    }

    /// <summary>Where a world point is on screen, in pixels from the top-left corner.</summary>
    private static Vector2 Pixel(Vector3 world, CameraPose pose, SceneViewProjection projection)
    {
        Assert.True(ViewCamera.TryProjectionMatrix(projection, Size, ViewRegion.Full, projection.Kind == SceneProjection.Perspective ? 0.1f : -1000, 1000, out Matrix4x4 p));
        Vector4 clip = Vector4.Transform(new Vector4(world, 1), ViewCamera.ViewMatrix(pose) * p);
        return new Vector2((clip.X / clip.W + 1) / 2 * Size.X, (1 - clip.Y / clip.W) / 2 * Size.Y);
    }

    [Theory]
    [InlineData(120, 0)]
    [InlineData(0, -75)]
    [InlineData(-333, 210)]
    [InlineData(5000, 5000)]
    public void RotatingTurnsAboutTheOrbitPoint(float dx, float dy)
    {
        CameraPose start = Start();

        CameraPose turned = OrbitController.Rotate(start, dx, dy, Size.Y);

        // The orbit point stays put, at the same distance, in the centre of the view.
        AssertClose(start.OrbitPoint, turned.OrbitPoint);
        Assert.Equal(start.OrbitDistance, turned.OrbitDistance);
        Assert.Equal(start.OrbitDistance, Vector3.Distance(turned.Position, start.OrbitPoint), 1e-3f);
        Assert.True(Vector2.Distance(Size / 2, Pixel(start.OrbitPoint, turned, Perspective)) < 0.05f);
        Assert.Equal(1f, turned.Orientation.Length(), 1e-5f);
        Assert.NotEqual(start.Position, turned.Position);
    }

    /// <summary>The model follows the pointer: dragging right brings its left side round, so the camera goes left.</summary>
    [Fact]
    public void RotationFollowsThePointer()
    {
        CameraPose start = Start();

        CameraPose right = OrbitController.Rotate(start, 50, 0, Size.Y);
        Assert.True(Vector3.Dot(right.Position - start.Position, start.Right) < 0);
        Assert.Equal(0f, Vector3.Dot(right.Position - start.Position, start.Up), 1e-4f);

        CameraPose down = OrbitController.Rotate(start, 0, 50, Size.Y);
        Assert.True(Vector3.Dot(down.Position - start.Position, start.Up) > 0);
        Assert.Equal(0f, Vector3.Dot(down.Position - start.Position, start.Right), 1e-4f);

        // Dragging the height of the view is half a turn.
        CameraPose half = OrbitController.Rotate(start, Size.Y, 0, Size.Y);
        AssertClose(start.OrbitPoint + (start.OrbitPoint - start.Position), half.Position);
        AssertClose(-start.Forward, half.Forward);
    }

    [Theory]
    [InlineData(SceneProjection.Perspective)]
    [InlineData(SceneProjection.Orthographic)]
    public void PanningMovesTheSceneWithThePointer(SceneProjection kind)
    {
        SceneViewProjection projection = kind == SceneProjection.Perspective ? Perspective : Orthographic;
        CameraPose start = Start();
        Vector3 grabbed = start.OrbitPoint;

        CameraPose panned = OrbitController.Pan(start, projection, 64, -30, Size);

        // What was under the pointer at the orbit distance is still under it.
        Vector2 moved = Pixel(grabbed, panned, projection) - Pixel(grabbed, start, projection);
        Assert.True(Vector2.Distance(new Vector2(64, -30), moved) < 0.05f, $"moved by {moved}");

        // The camera slid in its own plane: same orientation, same distance, new orbit point.
        Assert.Equal(start.Orientation, panned.Orientation);
        Assert.Equal(start.OrbitDistance, panned.OrbitDistance);
        Assert.Equal(0f, Vector3.Dot(panned.Position - start.Position, start.Forward), 1e-4f);
        Assert.NotEqual(grabbed, panned.OrbitPoint);
    }

    [Fact]
    public void ZoomingAPerspectiveViewMovesTowardsTheOrbitPointWithoutReachingIt()
    {
        CameraPose pose = Start();
        SceneViewProjection projection = Perspective;
        Vector3 orbit = pose.OrbitPoint;
        float previous = pose.OrbitDistance;

        // Closer with every step, until a floor where camera and orbit point are still distinct numbers.
        for (int i = 0; i < 400; i++)
        {
            OrbitController.Zoom(ref pose, ref projection, 1);

            Assert.True(i < 50 ? pose.OrbitDistance < previous : pose.OrbitDistance <= previous, $"step {i}: {pose.OrbitDistance} after {previous}");
            Assert.True(pose.OrbitDistance > 0);
            previous = pose.OrbitDistance;
        }

        Assert.True(pose.OrbitDistance < 1e-3f);
        Assert.True(pose.IsFinite);
        Assert.Equal(Perspective, projection);
        AssertClose(orbit, pose.OrbitPoint, 1e-2f);
        Assert.True(Vector3.Dot(orbit - pose.Position, pose.Forward) > 0, "the camera did not pass the orbit point");
    }

    [Fact]
    public void ZoomingAnOrthographicViewScalesIt()
    {
        CameraPose pose = Start();
        CameraPose start = pose;
        SceneViewProjection projection = Orthographic;

        OrbitController.Zoom(ref pose, ref projection, 2);

        Assert.Equal(start.Position, pose.Position);
        Assert.True(projection.OrthographicScale > Orthographic.OrthographicScale);

        OrbitController.Zoom(ref pose, ref projection, -5);
        Assert.True(projection.OrthographicScale < Orthographic.OrthographicScale);
    }

    [Theory]
    [InlineData(SceneProjection.Perspective)]
    [InlineData(SceneProjection.Orthographic)]
    public void ZoomingOutUndoesZoomingIn(SceneProjection kind)
    {
        CameraPose pose = Start();
        CameraPose start = pose;
        SceneViewProjection projection = kind == SceneProjection.Perspective ? Perspective : Orthographic;
        SceneViewProjection startProjection = projection;

        OrbitController.Zoom(ref pose, ref projection, 3.5f);
        OrbitController.Zoom(ref pose, ref projection, -3.5f);

        AssertClose(start.Position, pose.Position);
        Assert.Equal(start.OrbitDistance, pose.OrbitDistance, 1e-3f);
        Assert.Equal(startProjection.OrthographicScale, projection.OrthographicScale, 1e-6f);
    }

    [Fact]
    public void InputThatIsNotANumberChangesNothing()
    {
        CameraPose start = Start();

        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Assert.Equal(start, OrbitController.Rotate(start, bad, 1, Size.Y));
            Assert.Equal(start, OrbitController.Rotate(start, 1, bad, Size.Y));
            Assert.Equal(start, OrbitController.Rotate(start, 1, 1, 0));
            Assert.Equal(start, OrbitController.Pan(start, Perspective, bad, 1, Size));
            Assert.Equal(start, OrbitController.Pan(start, Perspective, 1, 1, new Vector2(0, 0)));

            CameraPose pose = start;
            SceneViewProjection projection = Perspective;
            OrbitController.Zoom(ref pose, ref projection, bad);
            Assert.Equal(start, pose);

            projection = Orthographic;
            OrbitController.Zoom(ref pose, ref projection, bad);
            Assert.Equal(Orthographic, projection);
        }

        // Zooming out for ever stops at something finite.
        CameraPose far = start;
        SceneViewProjection p = Perspective;
        for (int i = 0; i < 20_000; i++)
        {
            OrbitController.Zoom(ref far, ref p, -10);
        }

        Assert.True(far.IsFinite && far.OrbitDistance > 0);
    }
}
