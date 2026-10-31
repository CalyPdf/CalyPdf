using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Caly.Core.Controls;
using Caly.Core.Models;
using static Caly.Tests.Fakes.PageItemsControlTestHost;

namespace Caly.Tests;

/// <summary>
/// A fit mode stays applied: the control re-fits when the viewer or the fitted page changes, and
/// leaves the zoom alone when only the scroll position or a scroll bar changes. Same 400×400 host
/// and expected zooms as <see cref="PageItemsControlZoomFitTests"/>.
/// </summary>
public class PageItemsControlZoomFitMaintenanceTests
{
    private static List<Size> Pages(int count, double width, double height)
        => Enumerable.Repeat(new Size(width, height), count).ToList();

    private static (PageItemsControl Control, Window Window) ShowFitHost(IList<Size> pages, bool singlePage = false)
    {
        var control = CreateFitHost(pages, singlePage);
        return (control, Show(control));
    }

    private static PageItem Page(PageItemsControl control, int pageNumber)
        => Assert.IsType<PageItem>(control.ContainerFromIndex(pageNumber - 1));

    /// <summary>
    /// What the bindings do when the window shows another tab: a new document (DataContext and
    /// pages), then that document's saved state.
    /// </summary>
    private static void SwitchTab(PageItemsControl control, IList<Size> pages, ZoomMode mode, double zoom,
        int selectedPage, Vector scrollOffset)
    {
        control.DataContext = new object();
        control.ItemsSource = pages;
        control.SetCurrentValue(PageItemsControl.ZoomModeProperty, mode);
        control.SetCurrentValue(PageItemsControl.ZoomLevelProperty, zoom);
        control.SetCurrentValue(PageItemsControl.SelectedPageNumberProperty, selectedPage);
        control.SetCurrentValue(PageItemsControl.ScrollOffsetProperty, scrollOffset);
    }

    /// <summary>
    /// What the first frame showing <paramref name="change"/> shows, read as the compositor commits
    /// it: the zoom, and whether the pages overflow the viewport sideways (the horizontal bar).
    /// </summary>
    /// <param name="showsTheChange">Tells a frame showing the change from an earlier one; by
    /// default, the first frame after the change shows it.</param>
    private static (double Zoom, bool OverflowsSideways) FirstFrameAfter(PageItemsControl control, Action change,
        Func<bool>? showsTheChange = null)
    {
        var scroll = control.Scroll!;
        var compositor = ElementComposition.GetElementVisual(control)!.Compositor;
        (double, bool)? frame = null;

        void OnCommit()
        {
            if (showsTheChange?.Invoke() == false)
            {
                compositor.RequestCompositionUpdate(OnCommit);
                return;
            }

            frame = (control.ZoomLevel, scroll.Extent.Width > scroll.Viewport.Width);
        }

        // The compositor holds a commit back until the render loop has taken the previous one:
        // have it take that one now, so that the next commit is the frame the change brings.
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        compositor.RequestCompositionUpdate(OnCommit);

        change();
        RunLayout();

        return Assert.NotNull(frame);
    }

    /// <summary>
    /// The fit runs between the layout pass that brings the change and the frame. Run after the
    /// frame (a job at Loaded priority), the frame showed the old zoom: on a shrinking window,
    /// pages wider than the viewport and the horizontal scroll bar, for one frame per resize step.
    /// </summary>
    [AvaloniaFact]
    public void Resize_IsFittedInTheFirstFrame()
    {
        var (control, window) = ShowFitHost(Pages(20, 100, 100));
        control.ZoomMode = ZoomMode.FitWidth;
        RunLayout();
        Assert.Equal(3.63, control.ZoomLevel, 6);

        // The headless window resizes after a frame at the old size (it emulates X11), and the
        // compositor would hold the frame at the new size back until that one is taken: take it as
        // the window resizes, before the layout pass at the new size.
        window.Resized += (_, _) => AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var frame = FirstFrameAfter(control, () => window.Width = 300, () => window.ClientSize.Width == 300);

        // (300 - 16 - 2 × 10 - 1) / 100
        Assert.Equal(2.63, frame.Zoom, 6);
        Assert.False(frame.OverflowsSideways);
    }

    /// <summary>
    /// A resize step coming before the last step's zoom has settled (at Loaded priority, after the
    /// frame) is fitted in its first frame too. Waiting for that zoom to settle, as other requests
    /// do, showed the step at the last step's zoom for a frame.
    /// </summary>
    [AvaloniaFact]
    public void ResizeStepBeforeTheLastZoomSettles_IsFittedInTheFirstFrame()
    {
        var (control, window) = ShowFitHost(Pages(20, 100, 100));
        control.ZoomMode = ZoomMode.FitWidth;
        RunLayout();
        window.Resized += (_, _) => AvaloniaHeadlessPlatform.ForceRenderTimerTick(); // See Resize_IsFittedInTheFirstFrame

        // A first step, fitted but with its zoom not settled: layout passes and frames only.
        window.Width = 300;
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
        Assert.Equal(2.63, control.ZoomLevel, 6);

        var frame = FirstFrameAfter(control, () => window.Width = 250, () => window.ClientSize.Width == 250);

        // (250 - 16 - 2 × 10 - 1) / 100
        Assert.Equal(2.13, frame.Zoom, 6);
        Assert.False(frame.OverflowsSideways);
    }

    [AvaloniaFact]
    public void ReferencePageSizeChange_IsFittedInTheFirstFrame()
    {
        var pages = new ObservableCollection<Size>(Pages(20, 100, 100));
        var (control, _) = ShowFitHost(pages);
        control.ZoomMode = ZoomMode.FitWidth;
        RunLayout();
        Assert.Equal(3.63, control.ZoomLevel, 6);

        var frame = FirstFrameAfter(control, () => pages[0] = new Size(200, 100));

        // (400 - 16 - 2 × 10 - 1) / 200
        Assert.Equal(1.815, frame.Zoom, 6);
        Assert.False(frame.OverflowsSideways);
    }

    [AvaloniaFact]
    public void Resize_Refits()
    {
        var (control, window) = ShowFitHost(Pages(20, 100, 100));
        control.ZoomMode = ZoomMode.FitWidth;
        RunLayout();

        window.Width = 600;
        RunLayout();

        // (600 - 16 - 2 × 10 - 1) / 100
        Assert.Equal(5.63, control.ZoomLevel, 6);
    }

    /// <summary>
    /// Found on screen: a document opened in fit width, then its window was enlarged. Zooming in
    /// around the viewport centre pushed the top of page 1 out of view; the fit keeps the content
    /// at the top edge in place instead.
    /// </summary>
    [AvaloniaFact]
    public void Resize_AtTheTopOfTheDocument_KeepsThePageTopInView()
    {
        var (control, window) = ShowFitHost(Pages(20, 100, 141));
        control.ZoomMode = ZoomMode.FitWidth;
        RunLayout();
        Assert.Equal(0, control.Scroll!.Offset.Y, 1.0);

        window.Width = 600;
        RunLayout();

        Assert.Equal(5.63, control.ZoomLevel, 6);
        Assert.Equal(1, control.SelectedPageNumber);
        Assert.Equal(0, control.Scroll.Offset.Y, 1.0);
    }

    [AvaloniaFact]
    public void Manual_ResizeKeepsTheZoom()
    {
        var (control, window) = ShowFitHost(Pages(20, 100, 100));

        window.Width = 600;
        RunLayout();

        Assert.Equal(1, control.ZoomLevel, 6);
    }

    [AvaloniaFact]
    public void ScrollingOntoAWiderPage_DoesNotRefit()
    {
        var pages = new ObservableCollection<Size>(
            Enumerable.Range(0, 20).Select(i => i % 2 == 0 ? new Size(100, 100) : new Size(200, 100)));
        var (control, _) = ShowFitHost(pages);
        // Make the narrow page 1 the current page (see PageItemsControlZoomFitTests).
        control.SetCurrentValue(PageItemsControl.ZoomLevelProperty, 3.5);
        RunLayout();
        control.GoToPage(1, 0);
        RunLayout();
        control.ZoomMode = ZoomMode.FitWidth;
        RunLayout();
        Assert.Equal(3.63, control.ZoomLevel, 6);

        control.GoToPage(6);
        RunLayout();
        // While the user reads the wide page 6, the document changes size (pages still streaming
        // in, a page size arriving): the fit is checked again, still against page 1.
        pages.Add(new Size(100, 100));
        RunLayout();

        Assert.Equal(6, control.SelectedPageNumber);
        Assert.Equal(3.63, control.ZoomLevel, 6);
    }

    [AvaloniaFact]
    public void BorderlineSinglePage_SettlesWithoutLooping()
    {
        var (control, _) = ShowFitHost([new Size(100, 108)], singlePage: true);
        int zoomChanges = 0;
        control.PropertyChanged += (_, e) =>
        {
            if (e.Property == PageItemsControl.ZoomLevelProperty)
            {
                zoomChanges++;
            }
        };

        control.ZoomMode = ZoomMode.FitWidth;
        for (int i = 0; i < 10; ++i)
        {
            RunLayout();
        }

        // At 3.63 the page no longer needs the vertical bar, which hides; the viewport widens, and
        // a fit that followed the viewport would zoom back in (3.79), bring the bar back, and so on.
        Assert.Equal(3.63, control.ZoomLevel, 6);
        Assert.Equal(1, zoomChanges);
    }

    [AvaloniaFact]
    public void TabSwitch_WithoutResize_KeepsTheSavedPosition()
    {
        // Fit page, because picking it lands on the page top: the mode changing with a tab switch
        // (here back from a tab at 100%), if mistaken for the user picking it, would move the
        // view off the saved position.
        var (control, _) = ShowFitHost(Pages(20, 100, 200));
        control.ZoomMode = ZoomMode.FitPage;
        RunLayout();
        control.GoToPage(7, 20);
        RunLayout();
        Assert.Equal(7, control.SelectedPageNumber);

        SwitchTab(control, Pages(5, 100, 200), ZoomMode.Manual, 1, 1, default);
        RunLayout();
        SwitchTab(control, Pages(20, 100, 200), ZoomMode.FitPage, 1.995, 7, new Vector(0, 20));
        RunLayout();

        Assert.Equal(1.995, control.ZoomLevel, 6);
        Assert.Equal((Page(control, 7).Bounds.Top + 20) * 1.995, control.Scroll!.Offset.Y, 1.0);
    }

    [AvaloniaFact]
    public void TabSwitchAfterResize_RefitsOnTheSamePage()
    {
        var (control, window) = ShowFitHost(Pages(20, 100, 100));
        control.ZoomMode = ZoomMode.FitWidth;
        RunLayout();
        control.GoToPage(7, 20);
        RunLayout();

        // Another tab, at 100%, is showing while the window is resized.
        SwitchTab(control, Pages(5, 100, 100), ZoomMode.Manual, 1, 1, default);
        RunLayout();
        window.Width = 600;
        RunLayout();
        Assert.Equal(1, control.ZoomLevel, 6);

        SwitchTab(control, Pages(20, 100, 100), ZoomMode.FitWidth, 3.63, 7, new Vector(0, 20));
        RunLayout();

        Assert.Equal(5.63, control.ZoomLevel, 6);
        Assert.Equal(7, control.SelectedPageNumber);
    }

    [AvaloniaFact]
    public void ContentBoundsArrival_Refits()
    {
        var (control, _) = ShowFitHost(Pages(10, 100, 200), singlePage: true);
        control.ZoomMode = ZoomMode.FitContent;
        RunLayout();
        Assert.Equal(1.995, control.ZoomLevel, 6); // No content box yet: the whole page

        // The page's picture is rendered and its content box becomes known.
        Page(control, 1).ContentBounds = new Rect(25, 50, 50, 100);
        RunLayout();

        Assert.Equal(3.79, control.ZoomLevel, 6);
    }

    [AvaloniaFact]
    public void NeighbourContentBounds_DoNotRefit()
    {
        var (control, _) = ShowFitHost(Pages(20, 100, 200));
        Page(control, 1).ContentBounds = new Rect(25, 50, 50, 100);
        control.ZoomMode = ZoomMode.FitContent;
        RunLayout();
        Assert.Equal(3.79, control.ZoomLevel, 6);

        // Page 1 is still the fitted page (and still realised); page 2 shows below it.
        control.GoToPage(1, 150);
        RunLayout();
        Page(control, 2).ContentBounds = new Rect(0, 0, 10, 10);
        RunLayout();

        Assert.Equal(3.79, control.ZoomLevel, 6);
    }

    [AvaloniaFact]
    public void ReferencePageSizeChange_Refits()
    {
        var pages = new ObservableCollection<Size>(Pages(20, 100, 100));
        var (control, _) = ShowFitHost(pages);
        control.ZoomMode = ZoomMode.FitWidth;
        RunLayout();
        Assert.Equal(3.63, control.ZoomLevel, 6);

        // The fitted page's real size arrives (or the page is rotated): twice as wide.
        pages[0] = new Size(200, 100);
        RunLayout();

        // (400 - 16 - 2 × 10 - 1) / 200
        Assert.Equal(1.815, control.ZoomLevel, 6);
    }

    [AvaloniaFact]
    public void StreamingPages_SettleOnTheScrollbarReservedZoom()
    {
        var pages = new ObservableCollection<Size>(Pages(1, 100, 100));
        var (control, _) = ShowFitHost(pages);
        control.ZoomMode = ZoomMode.FitWidth;
        RunLayout();
        // One page fits the viewer at (400 - 2 × 10 - 1) / 100 with no scroll bar.
        Assert.Equal(3.79, control.ZoomLevel, 6);

        // The rest of the document arrives, as pages stream in while it opens.
        for (int i = 0; i < 19; ++i)
        {
            pages.Add(new Size(100, 100));
        }

        RunLayout();

        Assert.Equal(3.63, control.ZoomLevel, 6);
        Assert.True(control.Scroll!.Extent.Width <= control.Scroll.Viewport.Width,
            $"no horizontal overflow, extent {control.Scroll.Extent.Width} > viewport {control.Scroll.Viewport.Width}");
    }

    /// <summary>
    /// The order a document opens in: shown in its tab before it has pages, then its opening zoom
    /// mode and page 1 set (LoadDocumentCore), then its pages added (LoadPages).
    /// </summary>
    [AvaloniaTheory]
    [InlineData(100, 141, 0, true, false)]
    [InlineData(100, 141, 0, false, false)]
    [InlineData(100, 98, 0, true, false)]
    [InlineData(100, 98, 0, false, false)]
    [InlineData(800, 1035, 0, false, false)] // Wider than the viewer: the fit zooms out
    [InlineData(800, 1035, 5, false, false)]
    [InlineData(100, 141, 5, false, false)]
    [InlineData(800, 792, 5, false, true)] // Page 1 alone fits without a bar, as the paper on screen did
    [InlineData(800, 792, 5, false, false)]
    public void OpeningInAFitMode_LandsAtTheTopOfPage1(double pageWidth, double pageHeight, double pageGap,
        bool pagesArriveOneByOne, bool firstPageAlone)
    {
        var pages = new ObservableCollection<Size>();
        var control = CreateFitHost(pages, pageGap: pageGap);
        Show(control);

        control.DataContext = new object();
        control.SetCurrentValue(PageItemsControl.ZoomModeProperty, ZoomMode.FitWidth);
        control.PageCount = 8;
        control.SetCurrentValue(PageItemsControl.SelectedPageNumberProperty, 1);
        control.SetCurrentValue(PageItemsControl.ScrollOffsetProperty, default(Vector));
        RunLayout();

        for (int i = 0; i < 8; ++i)
        {
            pages.Add(new Size(pageWidth, pageHeight));
            if (pagesArriveOneByOne || (firstPageAlone && i == 0))
            {
                RunLayout();
            }
        }

        RunLayout();

        Assert.Equal((400 - 16 - 2 * 10 - 1) / pageWidth, control.ZoomLevel, 6);
        Assert.Equal(1, control.SelectedPageNumber);
        Assert.Equal(0, control.Scroll!.Offset.Y, 1.0);
    }

    [AvaloniaFact]
    public void SinglePageTurn_FitsTheNewPage()
    {
        var (control, _) = ShowFitHost([new Size(100, 200), new Size(200, 200)], singlePage: true);
        control.ZoomMode = ZoomMode.FitWidth;
        RunLayout();
        Assert.Equal(3.63, control.ZoomLevel, 6);

        control.GoToPage(2);
        RunLayout();

        // The 200×200 page fits without a scroll bar: (400 - 2 × 10 - 1) / 200.
        Assert.Equal(1.895, control.ZoomLevel, 6);
        Assert.Equal(0, control.Scroll!.Offset.Y, 1.0);
    }
}
