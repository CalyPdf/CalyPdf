using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Caly.Avalonia.Scene3D.Internal;
using Caly.Rendering.Scene3D;
using Vector = Avalonia.Vector;

namespace Caly.Avalonia.Scene3D.Tests;

/// <summary>
/// What a host that has gestures of its own asks of the control: leave some input alone, pan with another
/// modifier, give up the Home key, and let a plain right-click through to the host's context menu.
/// </summary>
public class Scene3DControlHostTests
{
    private static (Window Window, Scene3DControl Control) Show(Scene? scene)
    {
        var control = new Scene3DControl { Width = 400, Height = 300, Scene = scene };
        var window = new Window { Content = control, SizeToContent = SizeToContent.WidthAndHeight };
        window.Show();
        control.View = new SceneView { CameraToWorld = TestScenes.LookFrom(new Vector3(0, -20, 0), Vector3.Zero, Vector3.UnitZ), OrbitDistance = 20 };
        return (window, control);
    }

    [AvaloniaFact]
    public void ReservedModifiersLeavePressesAndWheelToTheHost()
    {
        using Scene scene = TestScenes.Box();
        (Window window, Scene3DControl control) = Show(scene);
        control.ReservedModifiers = KeyModifiers.Control;
        CameraPose start = control.Pose;

        int unhandledPresses = 0, unhandledWheels = 0;
        window.AddHandler(InputElement.PointerPressedEvent, (_, e) => unhandledPresses += e.Handled ? 0 : 1, RoutingStrategies.Bubble, handledEventsToo: true);
        window.AddHandler(InputElement.PointerWheelChangedEvent, (_, e) => unhandledWheels += e.Handled ? 0 : 1, RoutingStrategies.Bubble, handledEventsToo: true);

        // Ctrl + drag is the host's (a page pan): the camera stays where it is and the press travels on.
        window.MouseDown(new Point(200, 150), MouseButton.Left, RawInputModifiers.Control);
        window.MouseMove(new Point(260, 190), RawInputModifiers.Control);
        window.MouseUp(new Point(260, 190), MouseButton.Left, RawInputModifiers.Control);
        Assert.Equal(start, control.Pose);
        Assert.Equal(1, unhandledPresses);

        // So is Ctrl + wheel (a page zoom), even while the control has the focus.
        control.Focus();
        window.MouseWheel(new Point(200, 150), new Vector(0, 2), RawInputModifiers.Control);
        Assert.Equal(start, control.Pose);
        Assert.Equal(1, unhandledWheels);

        // Without the modifier the control takes both, as before.
        window.MouseDown(new Point(100, 100), MouseButton.Left);
        window.MouseMove(new Point(150, 100));
        window.MouseUp(new Point(150, 100), MouseButton.Left);
        Assert.NotEqual(start.Position, control.Pose.Position);
        Assert.Equal(1, unhandledPresses);
        window.MouseWheel(new Point(200, 150), new Vector(0, 2));
        Assert.Equal(1, unhandledWheels);
        window.Close();
    }

    [AvaloniaFact]
    public void PanModifiersChooseWhichDragPans()
    {
        using Scene scene = TestScenes.Box();
        (Window window, Scene3DControl control) = Show(scene);
        control.PanModifiers = KeyModifiers.Shift;
        CameraPose start = control.Pose;

        window.MouseDown(new Point(100, 60), MouseButton.Left, RawInputModifiers.Shift);
        window.MouseMove(new Point(140, 60), RawInputModifiers.Shift);
        window.MouseUp(new Point(140, 60), MouseButton.Left, RawInputModifiers.Shift);
        CameraPose panned = control.Pose;
        Assert.Equal(start.Orientation, panned.Orientation);
        Assert.NotEqual(start.OrbitPoint, panned.OrbitPoint);

        // Ctrl no longer pans: with nothing reserved, Ctrl + left-drag is an ordinary rotation.
        window.MouseDown(new Point(300, 200), MouseButton.Left, RawInputModifiers.Control);
        window.MouseMove(new Point(340, 200), RawInputModifiers.Control);
        window.MouseUp(new Point(340, 200), MouseButton.Left, RawInputModifiers.Control);
        Assert.NotEqual(panned.Orientation, control.Pose.Orientation);
        Assert.True(Vector3.Distance(panned.OrbitPoint, control.Pose.OrbitPoint) < 1e-3f);
        window.Close();
    }

    [AvaloniaFact]
    public void ARightClickWithoutMovementAsksForTheContextMenu()
    {
        using Scene scene = TestScenes.Box();
        (Window window, Scene3DControl control) = Show(scene);
        CameraPose start = control.Pose;
        int requests = 0;
        window.AddHandler(Control.ContextRequestedEvent, (_, _) => requests++, RoutingStrategies.Bubble, handledEventsToo: true);

        window.MouseDown(new Point(200, 150), MouseButton.Right);
        window.MouseUp(new Point(200, 150), MouseButton.Right);
        Assert.Equal(1, requests);
        Assert.Equal(start, control.Pose);

        // A hand is never perfectly still: a pixel or two is still a click.
        window.MouseDown(new Point(100, 100), MouseButton.Right);
        window.MouseMove(new Point(102, 101));
        window.MouseUp(new Point(102, 101), MouseButton.Right);
        Assert.Equal(2, requests);
        window.Close();
    }

    [AvaloniaFact]
    public void ARightDragZoomsAndAsksForNoMenu()
    {
        using Scene scene = TestScenes.Box();
        (Window window, Scene3DControl control) = Show(scene);
        float start = control.Pose.OrbitDistance;
        int requests = 0;
        window.AddHandler(Control.ContextRequestedEvent, (_, _) => requests++, RoutingStrategies.Bubble, handledEventsToo: true);

        window.MouseDown(new Point(50, 250), MouseButton.Right);
        window.MouseMove(new Point(50, 190));
        window.MouseUp(new Point(50, 190), MouseButton.Right);
        Assert.True(control.Pose.OrbitDistance < start);
        Assert.Equal(0, requests);

        // Out and back to where the press was is still a drag, not a click.
        window.MouseDown(new Point(300, 100), MouseButton.Right);
        window.MouseMove(new Point(300, 140));
        window.MouseMove(new Point(300, 100));
        window.MouseUp(new Point(300, 100), MouseButton.Right);
        Assert.Equal(0, requests);
        window.Close();
    }

    [AvaloniaFact]
    public void ResetKeyCanBeSwitchedOff()
    {
        using Scene scene = TestScenes.Box();
        (Window window, Scene3DControl control) = Show(scene);
        CameraPose start = control.Pose;
        control.Focus();
        control.RotateBy(40, 10);
        CameraPose rotated = control.Pose;

        control.ResetKey = null;
        window.KeyPressQwerty(PhysicalKey.Home, RawInputModifiers.None);
        Assert.Equal(rotated, control.Pose);

        control.ResetKey = Key.Home;
        window.KeyPressQwerty(PhysicalKey.Home, RawInputModifiers.None);
        Assert.Equal(start.Position, control.Pose.Position);
        window.Close();
    }

    [AvaloniaFact]
    public void ASceneWithoutGeometryIsReportedAtOnce()
    {
        using Scene released = TestScenes.Box();
        released.ReleaseGeometry();
        (Window window, Scene3DControl control) = Show(null);

        // No GPU is needed to know that this scene cannot be uploaded: the host can start decoding
        // again while OpenGL is still starting.
        control.Scene = released;
        Assert.Equal(Scene3DStatus.NeedsScene, control.Status);

        using Scene whole = TestScenes.Box();
        control.Scene = whole;
        Assert.Equal(Scene3DStatus.Uploading, control.Status);
        window.Close();
    }
}
