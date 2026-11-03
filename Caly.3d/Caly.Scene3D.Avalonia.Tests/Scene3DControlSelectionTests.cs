using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Caly.Rendering.Scene3D;

namespace Caly.Scene3D.Avalonia.Tests;

/// <summary>
/// The selected part, without a GPU: what clears it and what a click does when nothing is on the GPU. The
/// pick itself, and a click on a drawn model, are checked by the viewer's self-test.
/// </summary>
public class Scene3DControlSelectionTests
{
    private sealed record Shown(Window Window, Panel Host, Scene3DControl Control, List<bool> EscapesReachingHost);

    private static Shown Show(Scene? scene)
    {
        var control = new Scene3DControl { Width = 400, Height = 300, Scene = scene };
        var host = new Panel { Children = { control } };
        var escapes = new List<bool>();
        host.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                escapes.Add(e.Handled);
            }
        }, RoutingStrategies.Bubble, handledEventsToo: true);

        var window = new Window { Content = host, SizeToContent = SizeToContent.WidthAndHeight };
        window.Show();
        return new Shown(window, host, control, escapes);
    }

    [AvaloniaFact]
    public void SelectedNodeStartsEmpty()
    {
        Assert.Equal(-1, new Scene3DControl().SelectedNode);
    }

    [AvaloniaFact]
    public void EscapeClearsTheSelectionAndIsHandled()
    {
        Shown s = Show(TestScenes.Box());
        s.Control.SelectedNode = 3;
        s.Control.Focus();

        s.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.Equal(-1, s.Control.SelectedNode);
        Assert.Equal([true], s.EscapesReachingHost);
    }

    [AvaloniaFact]
    public void EscapeWithNothingSelectedIsLeftToTheHost()
    {
        Shown s = Show(TestScenes.Box());
        s.Control.Focus();

        s.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.Equal([false], s.EscapesReachingHost);
    }

    [AvaloniaFact]
    public void ANewSceneClearsTheSelection()
    {
        Shown s = Show(TestScenes.Box());
        s.Control.SelectedNode = 0;

        s.Control.Scene = TestScenes.Box();

        Assert.Equal(-1, s.Control.SelectedNode);
    }

    [AvaloniaFact]
    public void ANewViewClearsTheSelection()
    {
        Shown s = Show(TestScenes.Box());
        s.Control.SelectedNode = 0;

        s.Control.View = new SceneView();

        Assert.Equal(-1, s.Control.SelectedNode);
    }

    [AvaloniaFact]
    public void AClickWithoutASceneOnTheGpuPicksNothing()
    {
        // Headless has no OpenGL: the scene never reaches the GPU, as while a model is still loading.
        Shown s = Show(TestScenes.Box());

        s.Window.MouseDown(new Point(10, 10), MouseButton.Left);
        s.Window.MouseUp(new Point(10, 10), MouseButton.Left);

        Assert.Null(s.Control.PendingPick);
        Assert.Equal(-1, s.Control.SelectedNode);
    }

    [AvaloniaFact]
    public void ALeftClickIsTakenAsAClick()
    {
        Shown s = Show(TestScenes.Box());

        s.Window.MouseDown(new Point(100, 100), MouseButton.Left);
        s.Window.MouseUp(new Point(100, 100), MouseButton.Left);

        Assert.Equal(1, s.Control.ClicksTaken);
    }

    [AvaloniaFact]
    public void ADragThatEndsElsewhereIsNotAClick()
    {
        Shown s = Show(TestScenes.Box());

        s.Window.MouseDown(new Point(100, 100), MouseButton.Left);
        s.Window.MouseMove(new Point(120, 100), RawInputModifiers.LeftMouseButton);
        s.Window.MouseUp(new Point(120, 100), MouseButton.Left);

        Assert.Equal(0, s.Control.ClicksTaken);
    }

    [AvaloniaFact]
    public void ADragThatComesBackIsNotAClick()
    {
        Shown s = Show(TestScenes.Box());

        s.Window.MouseDown(new Point(100, 100), MouseButton.Left);
        s.Window.MouseMove(new Point(130, 100), RawInputModifiers.LeftMouseButton);
        s.Window.MouseMove(new Point(100, 100), RawInputModifiers.LeftMouseButton);
        s.Window.MouseUp(new Point(100, 100), MouseButton.Left);

        Assert.Equal(0, s.Control.ClicksTaken);
    }

    [AvaloniaFact]
    public void APanClickIsNotAClick()
    {
        Shown s = Show(TestScenes.Box());

        s.Window.MouseDown(new Point(100, 100), MouseButton.Left, RawInputModifiers.Control);
        s.Window.MouseUp(new Point(100, 100), MouseButton.Left, RawInputModifiers.Control);

        Assert.Equal(0, s.Control.ClicksTaken);
    }

    [AvaloniaTheory]
    [InlineData(MouseButton.Right)]
    [InlineData(MouseButton.Middle)]
    public void OtherButtonsDoNotClick(MouseButton button)
    {
        Shown s = Show(TestScenes.Box());

        s.Window.MouseDown(new Point(100, 100), button);
        s.Window.MouseUp(new Point(100, 100), button);

        Assert.Equal(0, s.Control.ClicksTaken);
    }
}
