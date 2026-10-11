using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Caly.Avalonia.Scene3D;
using Caly.Core;
using Caly.Core.Controls;
using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Core.ViewModels;
using Caly.Pdf.Models;
using CommunityToolkit.Mvvm.Input;
using static Caly.Tests.Fakes.DocumentTestHarness;
using static Caly.Tests.Fakes.PageItemsControlTestHost;

namespace Caly.Tests;

/// <summary>
/// The pointer over 3D artwork that is not being viewed: a click asks to view it, hovering says what a
/// click will do; and the wheel over a view that is.
/// </summary>
public class Artwork3DActivationInputTests
{
    private sealed class Setup
    {
        public required Window Window { get; init; }

        public required PageItemsControl Pages { get; init; }

        public required PageItem Page { get; init; }

        public required PageInteractiveLayerControl Layer { get; init; }

        public List<Artwork3DActivationRequest> Requests { get; } = [];

        /// <summary>A point of the page's layer, in window coordinates.</summary>
        public Point At(double x, double y) => Layer.TranslatePoint(new Point(x, y), Window)!.Value;

        public void Click(double x, double y, MouseButton button = MouseButton.Left)
        {
            Window.MouseDown(At(x, y), button);
            Window.MouseUp(At(x, y), button);
        }
    }

    /// <summary>
    /// One page of 100 x 400 whose text layer is that of box_base.pdf: 3D artwork over (0, 0, 300, 300),
    /// so the top 300 units of the page are artwork and the 100 below are not.
    /// </summary>
    private static Setup Show(bool with3DHost = false, int pageCount = 1, double pageHeight = 400)
    {
        PdfTextLayer textLayer;
        using (Artwork3DTestDocument box = Artwork3DTestDocument.OpenBox())
        {
            textLayer = box.Layer;
        }

        PageItemsControl pages = Create(pageCount, pageHeight, with3DHost);
        pages.TextSelection = new TextSelection(pageCount);
        Window window = Fakes.PageItemsControlTestHost.Show(pages);

        var page = Assert.IsType<PageItem>(pages.ContainerFromIndex(0));
        PageInteractiveLayerControl layer = page.InteractiveLayer!;
        layer.PdfTextLayer = textLayer;
        layer.PageNumber = 1;
        layer.VisibleArea = new Rect(0, 0, PageWidth, pageHeight);
        RunLayout();

        var setup = new Setup { Window = window, Pages = pages, Page = page, Layer = layer };
        pages.ActivateArtwork3D = new RelayCommand<Artwork3DActivationRequest>(r => setup.Requests.Add(r!));
        return setup;
    }

    [AvaloniaFact]
    public void AClickOnArtworkAsksToViewIt()
    {
        Setup s = Show();

        s.Click(50, 150);

        Artwork3DActivationRequest request = Assert.Single(s.Requests);
        Assert.Equal(1, request.PageNumber);
        Assert.Equal(0, request.ArtworkIndex);
        Assert.Same(s.Layer.PdfTextLayer!.Artworks3D![0], request.Artwork);
    }

    [AvaloniaFact]
    public void AClickBesideTheArtworkDoesNot()
    {
        Setup s = Show();

        s.Click(50, 350);

        Assert.Empty(s.Requests);
    }

    [AvaloniaFact]
    public void APressDraggedAcrossTheArtworkIsNotAClick()
    {
        Setup s = Show();

        s.Window.MouseDown(s.At(20, 100), MouseButton.Left);
        s.Window.MouseMove(s.At(60, 180));
        s.Window.MouseUp(s.At(60, 180), MouseButton.Left);

        Assert.Empty(s.Requests);
    }

    [AvaloniaFact]
    public void ARightClickIsLeftToThePageMenu()
    {
        Setup s = Show();

        s.Click(50, 150, MouseButton.Right);

        Assert.Empty(s.Requests);
    }

    [AvaloniaFact]
    public void WithoutACommandAClickDoesNothing()
    {
        Setup s = Show();
        s.Pages.ActivateArtwork3D = null;

        s.Click(50, 150);

        Assert.Empty(s.Requests);
    }

    [AvaloniaFact]
    public void HoveringShowsThatAClickWillOpenTheModel()
    {
        Setup s = Show();
        Artwork3DAvailability.MarkAvailable();
        try
        {
            s.Window.MouseMove(s.At(50, 150));
            Assert.Equal("3D model (U3D)", s.Pages.InteractiveActionOver);
            Assert.Same(App.HandCursor, s.Pages.Cursor);

            s.Window.MouseMove(s.At(50, 350));
            Assert.NotSame(App.HandCursor, s.Pages.Cursor);

            // Once 3D is known not to work here, the artwork is no longer offered as something to click.
            Artwork3DAvailability.MarkUnavailable();
            s.Window.MouseMove(s.At(50, 160));
            Assert.Equal("3D model (U3D)", s.Pages.InteractiveActionOver);
            Assert.NotSame(App.HandCursor, s.Pages.Cursor);
        }
        finally
        {
            Artwork3DAvailability.MarkAvailable();
        }
    }

    /// <summary>
    /// In single-page display the wheel turns the page at its end. Over a 3D view that has the focus the
    /// wheel zooms the model instead; over one that has not, the page is turned as anywhere else.
    /// </summary>
    [AvaloniaFact]
    public async Task InSinglePageDisplayAFocusedViewKeepsTheWheel()
    {
        await using var f = new Artwork3DFixture();
        Artwork3DViewModel artwork = f.Activate();
        Assert.True(await WaitUntil(() => artwork.State == Artwork3DState.Ready));

        Setup s = Show(with3DHost: true, pageCount: 5, pageHeight: 100);
        s.Pages.PageDisplayMode = PageDisplayMode.SinglePage;
        RunLayout();
        var page = Assert.IsType<PageItem>(s.Pages.ContainerFromIndex(0));
        page.ActiveArtwork3D = artwork;
        RunLayout();
        Scene3DControl view3D = page.GetVisualDescendants().OfType<Scene3DControl>().Single();

        // The middle of the page, which the view covers (the pages control clips at the page).
        Point centre = page.TranslatePoint(new Point(PageWidth / 2, 50), s.Window)!.Value;
        // A visual can be hit once the compositor has drawn a frame with it in.
        // A visual can be hit once the compositor has drawn it: the view was only just built, and
        // nothing draws frames in a headless test unless asked.
        for (int i = 0; i < 3; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            RunLayout();
        }

        Assert.Same(view3D, s.Window.InputHitTest(centre));
        int notches = (int)Math.Ceiling(WheelPageFlipGate.OverscrollThreshold) + 1;

        view3D.Focus();
        for (int i = 0; i < notches; i++)
        {
            s.Window.MouseWheel(centre, new Vector(0, -1));
        }

        RunLayout();
        Assert.Equal(1, s.Pages.SelectedPageNumber);

        s.Pages.Focus();
        for (int i = 0; i < notches; i++)
        {
            s.Window.MouseWheel(centre, new Vector(0, -1));
        }

        RunLayout();
        Assert.Equal(2, s.Pages.SelectedPageNumber);
    }
}
