using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Caly.Avalonia.Scene3D.Internal;
using Scene3D;
using Vector = Avalonia.Vector;

namespace Caly.Avalonia.Scene3D.Tests;

/// <summary>
/// What the control does without a GPU: its properties, its camera under pointer input, and its status
/// when OpenGL never comes (headless Avalonia stands in for the software renderer). Drawing itself is
/// checked by the viewer's self-test, which needs a real GPU.
/// </summary>
public class Scene3DControlTests
{
    private static (Window Window, Scene3DControl Control) Show(Scene? scene, double width = 400, double height = 300)
    {
        var control = new Scene3DControl { Width = width, Height = height, Scene = scene };
        var window = new Window { Content = control, SizeToContent = SizeToContent.WidthAndHeight };
        window.Show();
        return (window, control);
    }

    [AvaloniaFact]
    public void DefaultsAreAnEmptyControlShowingTheWholeView()
    {
        var control = new Scene3DControl();

        Assert.Null(control.Scene);
        Assert.Null(control.View);
        Assert.Equal(new Rect(0, 0, 1, 1), control.VisibleRegion);
        Assert.Equal(Colors.White, control.DefaultBackground);
        Assert.True(control.ReleaseGeometryAfterUpload);
        Assert.Equal(Scene3DStatus.Empty, control.Status);
        Assert.Null(control.StatusMessage);
        Assert.True(control.Focusable);
        Assert.Equal(0, control.Statistics.FramesRendered);
    }

    [AvaloniaFact]
    public void ASceneWithoutAnimationsHasNoAnimationToPlay()
    {
        using Scene scene = TestScenes.Box();
        (Window window, Scene3DControl control) = Show(scene);

        Assert.False(control.HasAnimation);
        Assert.False(control.IsAnimationPlaying);
        Assert.Equal(0, control.AnimationDuration);

        control.PlayAnimation();
        Assert.False(control.IsAnimationPlaying);
        window.Close();
    }

    [AvaloniaFact]
    public void AnAnimatedSceneThatAsksToStartPlaysAtOnce()
    {
        using Scene scene = TestScenes.AnimatedBox(autoPlay: true);
        (Window window, Scene3DControl control) = Show(scene);

        Assert.True(control.HasAnimation);
        Assert.True(control.IsAnimationPlaying);
        Assert.Equal(2, control.AnimationDuration, 5);
        window.Close();
    }

    [AvaloniaFact]
    public void AnAnimatedSceneThatDoesNotAskWaitsForPlay()
    {
        using Scene scene = TestScenes.AnimatedBox(autoPlay: false);
        (Window window, Scene3DControl control) = Show(scene);

        Assert.True(control.HasAnimation);
        Assert.False(control.IsAnimationPlaying);

        control.PlayAnimation();
        Assert.True(control.IsAnimationPlaying);

        control.PauseAnimation();
        Assert.False(control.IsAnimationPlaying);
        window.Close();
    }

    [AvaloniaFact]
    public void RestartingPlaysFromTheStart()
    {
        using Scene scene = TestScenes.AnimatedBox(autoPlay: false);
        (Window window, Scene3DControl control) = Show(scene);

        control.PlayAnimation();
        control.PauseAnimation();
        control.RestartAnimation();

        Assert.True(control.IsAnimationPlaying);
        Assert.True(control.AnimationTime < 0.5);
        window.Close();
    }

    [AvaloniaFact]
    public void SeekingPutsThePausedAnimationAtThatTime()
    {
        using Scene scene = TestScenes.AnimatedBox(autoPlay: false);
        (Window window, Scene3DControl control) = Show(scene);

        control.SeekAnimation(1.25);

        Assert.False(control.IsAnimationPlaying);
        Assert.Equal(1.25, control.AnimationTime, 6);
        window.Close();
    }

    [AvaloniaFact]
    public void SeekingKeepsAPlayingAnimationPlayingFromThere()
    {
        using Scene scene = TestScenes.AnimatedBox(autoPlay: true);
        (Window window, Scene3DControl control) = Show(scene);

        control.SeekAnimation(30);

        Assert.True(control.IsAnimationPlaying);
        Assert.InRange(control.AnimationTime, 30, 31);
        window.Close();
    }

    [AvaloniaFact]
    public void ANewSceneStartsItsAnimationsAgain()
    {
        using Scene first = TestScenes.AnimatedBox(autoPlay: true);
        using Scene second = TestScenes.AnimatedBox(autoPlay: false);
        (Window window, Scene3DControl control) = Show(first);
        Assert.True(control.IsAnimationPlaying);

        control.Scene = second;

        Assert.True(control.HasAnimation);
        Assert.False(control.IsAnimationPlaying);
        Assert.Equal(0, control.AnimationTime);

        control.Scene = null;
        Assert.False(control.HasAnimation);
        window.Close();
    }

    [AvaloniaFact]
    public void ASceneWaitsForTheGpuAndWithoutOneTheControlSaysSo()
    {
        using Scene scene = TestScenes.Box();
        (Window window, Scene3DControl control) = Show(scene);

        Assert.Equal(Scene3DStatus.Uploading, control.Status);

        // What the timer does half a second after attaching, if OpenGL has not initialised by then.
        control.CheckAvailability();
        Assert.Equal(Scene3DStatus.Unavailable, control.Status);
        Assert.False(string.IsNullOrEmpty(control.StatusMessage));

        // Nothing was uploaded, so the scene still holds its geometry.
        Assert.True(scene.HasGeometry);

        // Still unavailable whatever is set next; empty again without a scene.
        control.View = new SceneView { Name = "Other" };
        Assert.Equal(Scene3DStatus.Unavailable, control.Status);
        control.Scene = null;
        Assert.Equal(Scene3DStatus.Empty, control.Status);
        window.Close();
    }

    [AvaloniaFact]
    public void TheCameraFollowsTheViewAndResets()
    {
        using Scene scene = TestScenes.Box();
        (Window window, Scene3DControl control) = Show(scene);

        // No view: the default framing looks at the middle of the box.
        Assert.True(Vector3.Distance(Vector3.Zero, control.Pose.OrbitPoint) < 1e-3f);

        var view = new SceneView { CameraToWorld = TestScenes.LookFrom(new Vector3(0, -20, 0), Vector3.Zero, Vector3.UnitZ), OrbitDistance = 20 };
        control.View = view;
        Assert.Equal(new Vector3(0, -20, 0), control.Pose.Position);

        control.RotateBy(80, 30);
        control.PanBy(10, 10);
        control.ZoomBy(2);
        Assert.NotEqual(new Vector3(0, -20, 0), control.Pose.Position);

        control.ResetView();
        Assert.Equal(new Vector3(0, -20, 0), control.Pose.Position);
        Assert.Equal(20f, control.Pose.OrbitDistance);
        window.Close();
    }

    [AvaloniaFact]
    public void ThePointerTurnsMovesAndZoomsTheCamera()
    {
        using Scene scene = TestScenes.Box();
        (Window window, Scene3DControl control) = Show(scene);
        var view = new SceneView { CameraToWorld = TestScenes.LookFrom(new Vector3(0, -20, 0), Vector3.Zero, Vector3.UnitZ), OrbitDistance = 20 };
        control.View = view;
        CameraPose start = control.Pose;

        // Left-drag: rotate about the orbit point.
        window.MouseDown(new Point(200, 150), MouseButton.Left);
        window.MouseMove(new Point(260, 150));
        window.MouseUp(new Point(260, 150), MouseButton.Left);
        CameraPose rotated = control.Pose;
        Assert.NotEqual(start.Position, rotated.Position);
        Assert.True(Vector3.Distance(start.OrbitPoint, rotated.OrbitPoint) < 1e-3f);
        Assert.True(control.IsFocused);

        // Nothing moves once the button is up.
        window.MouseMove(new Point(300, 200));
        Assert.Equal(rotated.Position, control.Pose.Position);

        // Ctrl + left-drag, and middle-drag: pan. The orbit point moves; the orientation does not.
        // (Each press is somewhere else: two presses in one place are a double-click.)
        window.MouseDown(new Point(100, 60), MouseButton.Left, RawInputModifiers.Control);
        window.MouseMove(new Point(130, 60), RawInputModifiers.Control);
        window.MouseUp(new Point(130, 60), MouseButton.Left, RawInputModifiers.Control);
        CameraPose panned = control.Pose;
        Assert.Equal(rotated.Orientation, panned.Orientation);
        Assert.NotEqual(rotated.OrbitPoint, panned.OrbitPoint);

        window.MouseDown(new Point(300, 220), MouseButton.Middle);
        window.MouseMove(new Point(300, 250));
        window.MouseUp(new Point(300, 250), MouseButton.Middle);
        Assert.NotEqual(panned.OrbitPoint, control.Pose.OrbitPoint);

        // Right-drag up: closer.
        float before = control.Pose.OrbitDistance;
        window.MouseDown(new Point(50, 250), MouseButton.Right);
        window.MouseMove(new Point(50, 190));
        window.MouseUp(new Point(50, 190), MouseButton.Right);
        Assert.True(control.Pose.OrbitDistance < before);

        // Home: back to the view.
        window.KeyPressQwerty(PhysicalKey.Home, RawInputModifiers.None);
        Assert.Equal(start.Position, control.Pose.Position);

        // Double-click: also back to the view.
        control.RotateBy(40, 0);
        window.MouseDown(new Point(350, 40), MouseButton.Left);
        window.MouseUp(new Point(350, 40), MouseButton.Left);
        window.MouseDown(new Point(350, 40), MouseButton.Left);
        window.MouseUp(new Point(350, 40), MouseButton.Left);
        Assert.Equal(start.Position, control.Pose.Position);
        window.Close();
    }

    [AvaloniaFact]
    public void TheWheelZoomsOnlyWhileTheControlHasFocus()
    {
        using Scene scene = TestScenes.Box();
        var control = new Scene3DControl { Width = 400, Height = 300, Scene = scene };
        var other = new Button { Content = "elsewhere" };
        var panel = new StackPanel { Children = { control, other } };
        var window = new Window { Content = panel, SizeToContent = SizeToContent.WidthAndHeight };
        window.Show();
        other.Focus();
        float start = control.Pose.OrbitDistance;

        // A page being scrolled must not be trapped by a 3D view it passes over.
        window.MouseWheel(new Point(200, 150), new Vector(0, 2));
        Assert.Equal(start, control.Pose.OrbitDistance);

        control.Focus();
        window.MouseWheel(new Point(200, 150), new Vector(0, 2));
        Assert.True(control.Pose.OrbitDistance < start);

        window.MouseWheel(new Point(200, 150), new Vector(0, -4));
        Assert.True(control.Pose.OrbitDistance > start);
        window.Close();
    }

    /// <summary>Review focus: no size, no region, no scene, no geometry — none of it may throw.</summary>
    [AvaloniaFact]
    public void DegenerateSizesRegionsAndScenesAreHarmless()
    {
        using Scene empty = TestScenes.Empty();
        (Window window, Scene3DControl control) = Show(empty, 0, 0);

        Assert.True(control.Pose.IsFinite);
        control.RotateBy(10, 10);
        control.PanBy(10, 10);
        control.ZoomBy(1);
        Assert.True(control.Pose.IsFinite);

        control.VisibleRegion = new Rect(0.5, 0.5, 0, 0);
        control.PanBy(10, 10);
        control.Width = 200;
        control.Height = 100;
        control.VisibleRegion = new Rect(0.25, 0.25, 0.5, 0.5);
        control.PanBy(10, 10);
        control.RotateBy(float.NaN, 0);
        Assert.True(control.Pose.IsFinite);

        // Without a scene there is no camera to move.
        control.Scene = null;
        control.RotateBy(10, 10);
        control.ResetView();
        Assert.Equal(Scene3DStatus.Empty, control.Status);
        window.Close();
    }

    [AvaloniaFact]
    public void DetachingAndReattachingKeepsTheSceneAndTheCamera()
    {
        using Scene scene = TestScenes.Box();
        (Window window, Scene3DControl control) = Show(scene);
        control.RotateBy(50, 20);
        Vector3 position = control.Pose.Position;

        window.Content = null;
        Assert.Same(scene, control.Scene);
        window.Content = control;

        Assert.Equal(position, control.Pose.Position);
        Assert.True(scene.HasGeometry);
        window.Close();
    }
}
