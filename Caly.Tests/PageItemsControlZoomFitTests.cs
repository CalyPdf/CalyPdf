using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Caly.Core.Controls;
using Caly.Core.Models;
using static Caly.Tests.Fakes.PageItemsControlTestHost;

namespace Caly.Tests;

/// <summary>
/// Picking a zoom fit mode fits the current page and lands the view on it. The viewer is 400×400
/// with 16 px scroll bars that take room from the viewport, as in PageItemsControl.axaml; the
/// expected zooms are those worked out in <see cref="ZoomFitTests"/>.
/// </summary>
public class PageItemsControlZoomFitTests
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

    [AvaloniaFact]
    public void FitWidth_Continuous_FitsTheWidthBesideTheVerticalBar()
    {
        var (control, _) = ShowFitHost(Pages(20, 100, 100));

        control.ZoomMode = ZoomMode.FitWidth;
        RunLayout();

        Assert.Equal(3.63, control.ZoomLevel, 6);
        Assert.Equal(384, control.Scroll!.Viewport.Width, 1.0);
        Assert.True(control.Scroll.Extent.Width <= control.Scroll.Viewport.Width,
            $"no horizontal overflow, extent {control.Scroll.Extent.Width} > viewport {control.Scroll.Viewport.Width}");
    }

    /// <summary>
    /// Fit width keeps the margin fit content width keeps: the page's sides sit 10 px from the
    /// viewport's (the 1 px of slack splits between them).
    /// </summary>
    [AvaloniaFact]
    public void FitWidth_KeepsTheMarginOnBothSides()
    {
        var (control, _) = ShowFitHost(Pages(20, 100, 100));

        control.ZoomMode = ZoomMode.FitWidth;
        RunLayout();

        var page = Page(control, 1);
        var scroll = control.Scroll!;
        double left = page.TranslatePoint(default, scroll)!.Value.X;
        double right = scroll.Viewport.Width - page.TranslatePoint(new Point(page.Bounds.Width, 0), scroll)!.Value.X;
        Assert.Equal(ZoomFit.FitMargin, left, 1.0);
        Assert.Equal(ZoomFit.FitMargin, right, 1.0);
    }

    /// <summary>
    /// With only 1 px of slack, fit width brought the horizontal bar whenever the pages ended up a
    /// little wider than the fitted page: a page slightly wider than the current one (page sizes
    /// vary in scanned documents), or a fractional page width, which the pages panel rounds up to
    /// a whole pixel before the zoom scales it. The current page is page 1, the tallest.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(100.0, 101.0)]
    [InlineData(100.3, 100.3)]
    public void FitWidth_NearlyEqualPageWidths_ShowNoHorizontalScrollBar(double currentWidth, double otherWidth)
    {
        var pages = Enumerable.Range(0, 20)
            .Select(i => i == 0 ? new Size(currentWidth, 300) : new Size(otherWidth, 100))
            .ToList();
        var (control, _) = ShowFitHost(pages);
        Assert.Equal(1, control.SelectedPageNumber);

        control.ZoomMode = ZoomMode.FitWidth;
        RunLayout();

        Assert.True(control.Scroll!.Extent.Width <= control.Scroll.Viewport.Width,
            $"no horizontal overflow, extent {control.Scroll.Extent.Width} > viewport {control.Scroll.Viewport.Width} at zoom {control.ZoomLevel}");
    }

    [AvaloniaFact]
    public void FitPage_Continuous_ShowsTheCurrentPageAtTheTop()
    {
        var (control, _) = ShowFitHost(Pages(20, 100, 200));
        control.GoToPage(3, 50);
        RunLayout();
        int reference = control.SelectedPageNumber!.Value;

        control.ZoomMode = ZoomMode.FitPage;
        RunLayout();

        Assert.Equal(1.995, control.ZoomLevel, 6);
        Assert.Equal(Page(control, reference).Bounds.Top * 1.995, control.Scroll!.Offset.Y, 1.0);
    }

    [AvaloniaFact]
    public void FitPage_SinglePage_ShowsTheWholePageWithoutScrollBars()
    {
        var (control, _) = ShowFitHost(Pages(10, 100, 200), singlePage: true);

        control.ZoomMode = ZoomMode.FitPage;
        RunLayout();

        Assert.Equal(1.995, control.ZoomLevel, 6);
        Assert.Equal(new Size(400, 400), control.Scroll!.Viewport);
    }

    /// <summary>
    /// Single-page view shows one page, so only its gap counts. Counting every page's gap made a
    /// long document always "need" the vertical bar, and fit width came out a bar's width short.
    /// </summary>
    [AvaloniaFact]
    public void FitWidth_SinglePage_LongDocument_ReservesNoVerticalBar()
    {
        // The 5 px gap of PageItem.axaml: 200 gaps (1000 px) would outgrow the 400 px viewport.
        var control = CreateFitHost(Pages(200, 200, 100), singlePage: true, pageGap: 5);
        Show(control);

        control.ZoomMode = ZoomMode.FitWidth;
        RunLayout();

        Assert.Equal(1.895, control.ZoomLevel, 6);
        Assert.Equal(new Size(400, 400), control.Scroll!.Viewport);
    }

    [AvaloniaFact]
    public void FitContent_SinglePage_CentresTheContentBox()
    {
        var (control, _) = ShowFitHost(Pages(10, 100, 200), singlePage: true);
        Page(control, 1).ContentBounds = new Rect(25, 50, 50, 100);

        control.ZoomMode = ZoomMode.FitContent;
        RunLayout();

        Assert.Equal(3.79, control.ZoomLevel, 6);
        // The box centre (50, 100) lands on the viewport centre: 100 * 3.79 - 400 / 2. The page,
        // 379 px wide, fits beside the vertical bar, so there is no horizontal scrolling.
        Assert.Equal(0, control.Scroll!.Offset.X, 1.0);
        Assert.Equal(179, control.Scroll.Offset.Y, 1.0);
    }

    [AvaloniaFact]
    public void FitContentWidth_SinglePage_PutsTheBoxAMarginFromTheLeft()
    {
        var (control, _) = ShowFitHost(Pages(10, 100, 200), singlePage: true);
        Page(control, 1).ContentBounds = new Rect(25, 50, 50, 100);

        control.ZoomMode = ZoomMode.FitContentWidth;
        RunLayout();

        Assert.Equal(7.26, control.ZoomLevel, 6);
        // The box's left edge, 25 page units in, sits 10 px from the viewport's left edge.
        Assert.Equal(10, 25 * 7.26 - control.Scroll!.Offset.X, 1.0);
    }

    [AvaloniaFact]
    public void FitWidth_WiderNeighbour_KeepsTheCurrentPageInView()
    {
        var pages = Enumerable.Range(0, 20).Select(i => i % 2 == 0 ? new Size(100, 100) : new Size(200, 100)).ToList();
        var (control, _) = ShowFitHost(pages);

        // The current page is the one showing the most area: at 100% that is the wide page 2, so
        // zoom in first until page 1 fills most of the viewer, with page 2 still realised below it.
        control.SetCurrentValue(PageItemsControl.ZoomLevelProperty, 3.5);
        RunLayout();
        control.GoToPage(1, 0);
        RunLayout();
        Assert.Equal(1, control.SelectedPageNumber);
        Assert.NotNull(control.ContainerFromIndex(1));

        control.ZoomMode = ZoomMode.FitWidth;
        RunLayout();

        // Page 1 is fitted, not its wider neighbour. The panel is as wide as the widest realised
        // page (200), so page 1 starts 50 units in: its left edge goes 10 px from the viewport's,
        // where fit content width puts the content box's.
        Assert.Equal(3.63, control.ZoomLevel, 6);
        Assert.Equal(50 * 3.63 - ZoomFit.FitMargin, control.Scroll!.Offset.X, 1.0);
    }

    [AvaloniaFact]
    public void FitPage_WiderNeighbour_KeepsTheMarginOnTheLeft()
    {
        var pages = Enumerable.Range(0, 20).Select(i => i % 2 == 0 ? new Size(100, 100) : new Size(200, 100)).ToList();
        var (control, _) = ShowFitHost(pages);
        // Make the narrow page 1 the current page (see FitWidth_WiderNeighbour_KeepsTheCurrentPageInView).
        control.SetCurrentValue(PageItemsControl.ZoomLevelProperty, 3.5);
        RunLayout();
        control.GoToPage(1, 0);
        RunLayout();

        control.ZoomMode = ZoomMode.FitPage;
        RunLayout();

        // Limited by the width, as fit width: (400 - 16 - 2 × 10 - 1) / 100. The view lands on
        // the top of page 1, its left edge 10 px from the viewport's.
        Assert.Equal(3.63, control.ZoomLevel, 6);
        Assert.Equal(50 * 3.63 - ZoomFit.FitMargin, control.Scroll!.Offset.X, 1.0);
        Assert.Equal(0, control.Scroll.Offset.Y, 1.0);
    }

    [AvaloniaFact]
    public void PickingTheActiveModeAgain_SnapsBack()
    {
        var (control, _) = ShowFitHost(Pages(20, 100, 200));
        control.ZoomMode = ZoomMode.FitPage;
        RunLayout();
        control.GoToPage(3, 50);
        RunLayout();
        int reference = control.SelectedPageNumber!.Value;

        // What the view model's SetZoomMode does when the active mode is picked again.
        control.ZoomMode = ZoomMode.Manual;
        control.ZoomMode = ZoomMode.FitPage;
        RunLayout();

        Assert.Equal(Page(control, reference).Bounds.Top * 1.995, control.Scroll!.Offset.Y, 1.0);
    }

    [AvaloniaFact]
    public void CtrlWheel_LeavesTheFitMode()
    {
        var (control, window) = ShowFitHost(Pages(20, 100, 100));
        control.ZoomMode = ZoomMode.FitWidth;
        RunLayout();

        window.MouseWheel(new Point(50, 150), new Vector(0, 1), RawInputModifiers.Control);
        RunLayout();

        Assert.Equal(ZoomMode.Manual, control.ZoomMode);
        Assert.Equal(3.63 * 1.1, control.ZoomLevel, 6);
    }

    [AvaloniaFact]
    public void FitRequestedWhileAWheelZoomSettles_StillApplies()
    {
        var (control, window) = ShowFitHost(Pages(20, 100, 100));

        // The wheel zoom is still settling (it finishes at Loaded priority) when the mode is picked.
        window.MouseWheel(new Point(50, 150), new Vector(0, 1), RawInputModifiers.Control);
        control.ZoomMode = ZoomMode.FitWidth;
        RunLayout();

        Assert.Equal(3.63, control.ZoomLevel, 6);
    }
}
