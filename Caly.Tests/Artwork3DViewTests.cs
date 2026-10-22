using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Caly.Scene3D.Avalonia;
using Caly.Core.Controls;
using Caly.Core.Services;
using Caly.Core.ViewModels;
using Caly.Rendering.Scene3D;
using static Caly.Tests.Fakes.DocumentTestHarness;

namespace Caly.Tests;

/// <summary>
/// The 3D view as it sits on a page: bound to its artwork, laid out at its on-screen size, with its
/// toolbar. Headless Avalonia has no OpenGL, which is also what the software renderer looks like.
/// </summary>
public class Artwork3DViewTests
{
    private static async Task<Artwork3DViewModel> ReadyArtwork(Artwork3DFixture f)
    {
        Artwork3DViewModel artwork = f.Activate();
        Assert.True(await WaitUntil(() => artwork.State == Artwork3DState.Ready));
        return artwork;
    }

    /// <summary>A "page" of 400 x 300 with the view over it, as PageItem builds it.</summary>
    private static (Window Window, Artwork3DView View, Scene3DControl Control) Show(Artwork3DViewModel artwork, double zoom = 1, Rect? visibleArea = null)
    {
        var view = new Artwork3DView { DataContext = artwork, ZoomLevel = zoom, VisibleArea = visibleArea ?? new Rect(0, 0, 400, 300) };
        var page = new Panel { Width = 400, Height = 300, Focusable = true, Children = { view } };
        var window = new Window { Content = page, SizeToContent = SizeToContent.WidthAndHeight };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Scene3DControl control = view.GetVisualDescendants().OfType<Scene3DControl>().Single();
        return (window, view, control);
    }

    [AvaloniaFact]
    public async Task TheViewShowsTheSceneAndTheViewsOfItsArtwork()
    {
        await using var f = new Artwork3DFixture();
        Artwork3DViewModel artwork = await ReadyArtwork(f);

        (Window window, Artwork3DView view, Scene3DControl control) = Show(artwork);

        Assert.Same(artwork.Scene, control.Scene);
        Assert.Same(artwork.SelectedView, control.View);
        Assert.Equal(Colors.White, control.DefaultBackground);

        ComboBox views = view.GetVisualDescendants().OfType<ComboBox>().Single();
        Assert.Equal(2, views.ItemCount);
        Assert.Equal(0, views.SelectedIndex);
        Assert.True(views.IsVisible);

        // Choosing in the list changes the view; so does the view model.
        views.SelectedIndex = 1;
        Assert.Equal(1, artwork.SelectedViewIndex);
        Assert.Null(control.View);
        artwork.SelectedViewIndex = 0;
        Assert.Equal(0, views.SelectedIndex);
        Assert.Same(artwork.SelectedView, control.View);
        window.Close();
    }

    [AvaloniaFact]
    public async Task TheControlHasCalysGestures()
    {
        await using var f = new Artwork3DFixture();
        Artwork3DViewModel artwork = await ReadyArtwork(f);

        (Window window, _, Scene3DControl control) = Show(artwork);

        // Ctrl (the platform's command key) is the page's: pan and zoom. The model pans with Shift.
        Assert.NotEqual(KeyModifiers.None, control.ReservedModifiers);
        Assert.Equal(KeyModifiers.Shift, control.PanModifiers);
        Assert.Null(control.ResetKey);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(0.5)]
    public async Task TheControlIsLaidOutAtItsSizeOnScreenAndScaledBack(double zoom)
    {
        await using var f = new Artwork3DFixture();
        Artwork3DViewModel artwork = await ReadyArtwork(f);
        Rect bounds = artwork.Bounds;

        (Window window, Artwork3DView view, Scene3DControl control) = Show(artwork, zoom);

        Assert.Equal(bounds.Width * zoom, control.Bounds.Width, 1e-6);
        Assert.Equal(bounds.Height * zoom, control.Bounds.Height, 1e-6);
        Assert.Equal(new Rect(0, 0, 1, 1), control.VisibleRegion);

        // Where it lands on the page: exactly on the artwork's rectangle.
        Matrix toPage = control.TransformToVisual(view)!.Value;
        Rect onPage = new Rect(control.Bounds.Size).TransformToAABB(toPage);
        Assert.Equal(bounds.X, onPage.X, 1e-6);
        Assert.Equal(bounds.Y, onPage.Y, 1e-6);
        Assert.Equal(bounds.Width, onPage.Width, 1e-6);
        Assert.Equal(bounds.Height, onPage.Height, 1e-6);

        // A new zoom is a new layout.
        view.ZoomLevel = zoom * 2;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(bounds.Width * zoom * 2, control.Bounds.Width, 1e-6);
        window.Close();
    }

    [AvaloniaFact]
    public async Task NothingIsLaidOutWhileThePageIsOffScreen()
    {
        await using var f = new Artwork3DFixture();
        Artwork3DViewModel artwork = await ReadyArtwork(f);

        (Window window, Artwork3DView view, Scene3DControl control) = Show(artwork);
        view.VisibleArea = null;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(default, control.Bounds.Size);
        window.Close();
    }

    [AvaloniaFact]
    public async Task TheToolbarStaysAtTheVisibleCornerAtItsOwnSize()
    {
        await using var f = new Artwork3DFixture();
        Artwork3DViewModel artwork = await ReadyArtwork(f);
        Rect bounds = artwork.Bounds;

        // The page shows only the lower right of the artwork, zoomed in.
        var visible = new Rect(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2, bounds.Width, bounds.Height);
        (Window window, Artwork3DView view, _) = Show(artwork, zoom: 4, visibleArea: visible);

        Control toolbar = view.GetVisualDescendants().OfType<Border>().First(b => b.Name == "Toolbar");
        Matrix toPage = toolbar.TransformToVisual(view)!.Value;
        Rect onPage = new Rect(toolbar.Bounds.Size).TransformToAABB(toPage);

        // At the corner of what is visible, a quarter of its natural size on the page (4 x on screen).
        Assert.True(onPage.X >= visible.X - 1e-6 && onPage.Y >= visible.Y - 1e-6);
        Assert.True(onPage.X < visible.X + 10 && onPage.Y < visible.Y + 10);
        Assert.Equal(toolbar.Bounds.Width / 4, onPage.Width, 1e-6);
        Assert.True(toolbar.Bounds.Width > 40);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhileTheModelIsReadThePosterShowsThroughAndTheToolbarSaysSo()
    {
        await using var f = new Artwork3DFixture();
        var gate = new TaskCompletionSource();
        var content = f.Content;
        f.Service.Artwork3DContent = async (_, _) =>
        {
            await gate.Task;
            return content;
        };
        Artwork3DViewModel artwork = f.Activate();

        (Window window, Artwork3DView view, Scene3DControl control) = Show(artwork);

        Assert.Null(control.Scene);
        Assert.Equal(Colors.Transparent, control.DefaultBackground);
        Assert.False(view.GetVisualDescendants().OfType<ComboBox>().Single().IsVisible);
        Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.IsVisible && t.Text == "Loading 3D…");

        gate.SetResult();
        Assert.True(await WaitUntil(() => artwork.State == Artwork3DState.Ready));
        Dispatcher.UIThread.RunJobs();

        Assert.Same(artwork.Scene, control.Scene);
        Assert.Equal(Colors.White, control.DefaultBackground);
        Assert.Equal(0, view.GetVisualDescendants().OfType<ComboBox>().Single().SelectedIndex);
        window.Close();
    }

    [AvaloniaFact]
    public async Task AViewThatComesBackAsksForItsSceneAgain()
    {
        await using var f = new Artwork3DFixture();
        Artwork3DViewModel artwork = await ReadyArtwork(f);
        Scene old = artwork.Scene!;

        // The state after a first view uploaded the geometry and then left the screen.
        old.ReleaseGeometry();

        (Window window, _, Scene3DControl control) = Show(artwork);

        Assert.True(await WaitUntil(() => artwork.Scene is not null && !ReferenceEquals(artwork.Scene, old)));
        Assert.True(artwork.Scene!.HasGeometry);
        Assert.Same(artwork.Scene, control.Scene);
        window.Close();
    }

    /// <summary>
    /// A tab switch, or a scroll away and back: the view is dropped and built again, and the model is
    /// read again. The camera is where the user left it, not back at the view's.
    /// </summary>
    [AvaloniaFact]
    public async Task AViewThatComesBackKeepsTheCameraItWasLeftWith()
    {
        await using var f = new Artwork3DFixture();
        Artwork3DViewModel artwork = await ReadyArtwork(f);
        Scene old = artwork.Scene!;

        (Window window, Artwork3DView view, Scene3DControl control) = Show(artwork);
        control.RotateBy(80, 30);
        control.ZoomBy(2);
        Vector3 moved = control.Pose.Position;
        Quaternion turned = control.Pose.Orientation;

        // The page leaves the screen, taking the view with it, after the geometry went to the GPU.
        ((Panel)view.Parent!).Children.Remove(view);
        old.ReleaseGeometry();
        window.Close();
        Assert.NotNull(artwork.CameraState);

        (Window again, _, Scene3DControl rebuilt) = Show(artwork);
        Assert.True(await WaitUntil(() => artwork.Scene is not null && !ReferenceEquals(artwork.Scene, old)));
        Dispatcher.UIThread.RunJobs();

        Assert.Same(artwork.Scene, rebuilt.Scene);
        Assert.Equal(moved, rebuilt.Pose.Position);
        Assert.Equal(turned, rebuilt.Pose.Orientation);
        again.Close();
    }

    [AvaloniaFact]
    public async Task EscapeGivesTheKeyboardBackToThePage()
    {
        await using var f = new Artwork3DFixture();
        Artwork3DViewModel artwork = await ReadyArtwork(f);
        (Window window, Artwork3DView view, Scene3DControl control) = Show(artwork);

        control.Focus();
        Assert.True(control.IsFocused);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.False(control.IsFocused);
        Assert.True(((Panel)view.Parent!).IsFocused);
        Assert.Same(artwork, f.Document.ActiveArtwork3D);
        window.Close();
    }

    /// <summary>
    /// The page scrolled away while OpenGL was still starting: the view says it left, and what the
    /// control said before (unavailable) does not become a verdict for the whole run.
    /// </summary>
    [AvaloniaFact]
    public async Task AViewThatLeavesItsPageBeforeOpenGlStartsDoesNotTurn3DOff()
    {
        await using var f = new Artwork3DFixture();
        TimeSpan grace = Artwork3DViewModel.UnavailableGrace;
        Artwork3DViewModel.UnavailableGrace = TimeSpan.FromMilliseconds(200);
        try
        {
            Artwork3DViewModel artwork = await ReadyArtwork(f);
            (Window window, Artwork3DView view, Scene3DControl control) = Show(artwork);

            control.CheckAvailability();
            Dispatcher.UIThread.RunJobs();
            ((Panel)view.Parent!).Children.Remove(view);

            await Task.Delay(400);
            Dispatcher.UIThread.RunJobs();

            Assert.False(Artwork3DAvailability.IsUnavailable);
            Assert.Same(artwork, f.Document.ActiveArtwork3D);
            Assert.Empty(f.Notifications);
            window.Close();
        }
        finally
        {
            Artwork3DViewModel.UnavailableGrace = grace;
            Artwork3DAvailability.MarkAvailable();
        }
    }

    /// <summary>
    /// On artwork too narrow on screen for both, the axes go below the toolbar instead of over it.
    /// </summary>
    [AvaloniaFact]
    public async Task OnNarrowArtworkTheAxesGoBelowTheToolbar()
    {
        await using var f = new Artwork3DFixture();
        Artwork3DViewModel artwork = await ReadyArtwork(f);

        (Window window, Artwork3DView view, _) = Show(artwork, zoom: 0.3);

        Control toolbar = view.GetVisualDescendants().OfType<Border>().First(b => b.Name == "Toolbar");
        Control axes = view.GetVisualDescendants().OfType<AxisGizmoControl>().Single();
        Rect toolbarOnPage = new Rect(toolbar.Bounds.Size).TransformToAABB(toolbar.TransformToVisual(view)!.Value);
        Rect axesOnPage = new Rect(axes.Bounds.Size).TransformToAABB(axes.TransformToVisual(view)!.Value);

        Assert.True(artwork.Bounds.Width * 0.3 < toolbar.Bounds.Width + axes.Bounds.Width, "the artwork is not narrow enough for this test");
        Assert.False(toolbarOnPage.Intersects(axesOnPage));
        Assert.True(axesOnPage.Y >= toolbarOnPage.Bottom - 1e-6);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WithoutOpenGlTheViewClosesAndSaysWhy()
    {
        await using var f = new Artwork3DFixture();
        TimeSpan grace = Artwork3DViewModel.UnavailableGrace;
        Artwork3DViewModel.UnavailableGrace = TimeSpan.FromMilliseconds(30);
        try
        {
            Artwork3DViewModel artwork = await ReadyArtwork(f);
            (Window window, _, Scene3DControl control) = Show(artwork);

            // What the control's own timer does half a second after it appears without OpenGL.
            control.CheckAvailability();

            Assert.True(await WaitUntil(() => f.Document.ActiveArtwork3D is null));
            Assert.True(Artwork3DAvailability.IsUnavailable);
            Assert.Equal(Artwork3DAvailability.UnavailableMessage, Assert.Single(f.Notifications));
            window.Close();
        }
        finally
        {
            Artwork3DViewModel.UnavailableGrace = grace;
        }
    }

    [AvaloniaFact]
    public async Task AViewThatLeftThePageSaysNothingMore()
    {
        await using var f = new Artwork3DFixture();
        TimeSpan grace = Artwork3DViewModel.UnavailableGrace;
        Artwork3DViewModel.UnavailableGrace = TimeSpan.FromMilliseconds(30);
        try
        {
            Artwork3DViewModel artwork = await ReadyArtwork(f);
            (Window window, Artwork3DView view, Scene3DControl control) = Show(artwork);

            // The page scrolls away: the view is taken off it. What its control reports from then on
            // (it loses its GPU objects, and with them its scene) is no business of the artwork's.
            ((Panel)view.Parent!).Children.Remove(view);
            control.CheckAvailability();
            await Task.Delay(120);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(artwork, f.Document.ActiveArtwork3D);
            Assert.False(Artwork3DAvailability.IsUnavailable);
            Assert.Empty(f.Notifications);
            window.Close();
        }
        finally
        {
            Artwork3DViewModel.UnavailableGrace = grace;
        }
    }
}
