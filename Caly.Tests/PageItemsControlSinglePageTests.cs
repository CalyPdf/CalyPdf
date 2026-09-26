using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Caly.Core.Controls;
using Caly.Core.Models;
using static Caly.Tests.Fakes.PageItemsControlTestHost;

namespace Caly.Tests;

public class PageItemsControlSinglePageTests
{
    private static PageItemsControl CreateSinglePage(int pageCount, double pageHeight = 100)
    {
        var control = Create(pageCount, pageHeight);
        control.PageDisplayMode = PageDisplayMode.SinglePage;
        return control;
    }

    [AvaloniaFact]
    public void GoToPage_SinglePage_ReportsTheNewPageAsVisible()
    {
        // Pages smaller than the viewport: turning the page changes neither the scroll
        // offset nor the extent, so no ScrollChanged event fires.
        var control = CreateSinglePage(10);
        Show(control);

        control.GoToPage(5);
        RunLayout();

        Assert.Equal(new Range(5, 6), control.RealisedPages);
        Assert.Equal(new Range(5, 6), control.VisiblePages);
        Assert.Equal(5, control.SelectedPageNumber);
    }

    [AvaloniaFact]
    public void GoToPage_SinglePage_WithoutOffset_LandsAtThePageTop()
    {
        var control = CreateSinglePage(10, pageHeight: 1000);
        Show(control);
        control.GoToPage(2, 500);
        RunLayout();
        Assert.Equal(500, control.Scroll!.Offset.Y, 1);

        control.GoToPage(3);
        RunLayout();

        Assert.Equal(0, control.Scroll!.Offset.Y, 1);
        Assert.Equal(3, control.SelectedPageNumber);
    }

    [AvaloniaFact]
    public void SwitchingMode_KeepsTheCurrentPageAndOffset()
    {
        var control = Create(50, pageHeight: 1000);
        Show(control);
        control.GoToPage(30, 200);
        RunLayout();
        Assert.Equal(30, control.SelectedPageNumber);

        control.PageDisplayMode = PageDisplayMode.SinglePage;
        RunLayout();

        Assert.IsType<SinglePageVirtualizingPanel>(control.ItemsPanelRoot);
        Assert.NotNull(control.ContainerFromIndex(29));
        Assert.Equal(30, control.SelectedPageNumber);
        Assert.Equal(new Range(30, 31), control.VisiblePages);
        Assert.Equal(200, control.Scroll!.Offset.Y, 1);

        control.PageDisplayMode = PageDisplayMode.Continuous;
        RunLayout();

        Assert.IsType<VirtualizingStackPanel>(control.ItemsPanelRoot);
        var page = control.ContainerFromIndex(29);
        Assert.NotNull(page);
        Assert.Equal(page.Bounds.Top + 200, control.Scroll!.Offset.Y, 1);
        Assert.Equal(30, control.SelectedPageNumber);
    }

    [AvaloniaFact]
    public void SwitchingMode_ClearsVisibleAreaOfDroppedContainers()
    {
        var control = Create(20);
        Show(control);
        var dropped = control.GetRealizedContainers().OfType<PageItem>().ToArray();
        Assert.Contains(dropped, p => p.VisibleArea.HasValue);

        control.PageDisplayMode = PageDisplayMode.SinglePage;
        RunLayout();

        Assert.All(dropped, p => Assert.Null(p.VisibleArea));
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void TabSwitch_WithModeChange_RestoresSavedPage(bool modeFirst)
    {
        var control = Create(20, pageHeight: 1000);
        Show(control);

        // What the two-way bindings pull in from the newly selected document.
        control.SetCurrentValue(PageItemsControl.SelectedPageNumberProperty, 7);
        control.SetCurrentValue(PageItemsControl.ScrollOffsetProperty, new Vector(0, 300));

        if (modeFirst)
        {
            control.PageDisplayMode = PageDisplayMode.SinglePage;
            control.DataContext = new object();
        }
        else
        {
            control.DataContext = new object();
            control.PageDisplayMode = PageDisplayMode.SinglePage;
        }

        RunLayout();

        Assert.NotNull(control.ContainerFromIndex(6));
        Assert.Equal(new Range(7, 8), control.VisiblePages);
        Assert.Equal(300, control.Scroll!.Offset.Y, 1);
    }

    /// <summary>
    /// Sends <paramref name="notches"/> whole mouse-wheel notches over the centre of the window
    /// (negative <paramref name="deltaY"/> scrolls down).
    /// </summary>
    private static void Wheel(Window window, int notches, double deltaY = -1)
    {
        var centre = new Point(ViewportSize / 2, ViewportSize / 2);
        for (int i = 0; i < notches; ++i)
        {
            window.MouseWheel(centre, new Vector(0, deltaY));
        }
    }

    private static int NotchesToTurn => (int)Math.Ceiling(WheelPageFlipGate.OverscrollThreshold);

    [AvaloniaFact]
    public void WheelDown_AtThePageEnd_NeedsExtraScrollBeforeTurning()
    {
        var control = CreateSinglePage(10);
        var window = Show(control);

        Wheel(window, NotchesToTurn - 1);
        RunLayout();
        Assert.Equal(1, control.SelectedPageNumber);

        Wheel(window, 1);
        RunLayout();
        Assert.Equal(2, control.SelectedPageNumber);
    }

    [AvaloniaFact]
    public void WheelUp_AtThePageTop_NeedsExtraScrollBeforeTurningBack()
    {
        var control = CreateSinglePage(10);
        var window = Show(control);
        control.GoToPage(5);
        RunLayout();

        Wheel(window, NotchesToTurn - 1, deltaY: 1);
        RunLayout();
        Assert.Equal(5, control.SelectedPageNumber);

        Wheel(window, 1, deltaY: 1);
        RunLayout();
        Assert.Equal(4, control.SelectedPageNumber);
    }

    [AvaloniaFact]
    public void WheelDown_InTheMiddleOfATallPage_ScrollsInsteadOfTurning()
    {
        var control = CreateSinglePage(10, pageHeight: 2000);
        var window = Show(control);

        window.MouseWheel(new Point(ViewportSize / 2, ViewportSize / 2), new Vector(0, -1));
        RunLayout();

        Assert.Equal(1, control.SelectedPageNumber);
        Assert.True(control.Scroll!.Offset.Y > 0);
    }

    [AvaloniaFact]
    public void WheelBurst_OnAPageThatFitsTheViewport_TurnsOnlyOnePage()
    {
        // A page that fits is at both edges at once, so a fling piles up overscroll far
        // past the threshold: the cooldown still holds it to a single page.
        var control = CreateSinglePage(10);
        var window = Show(control);

        Wheel(window, NotchesToTurn * 4);
        RunLayout();

        Assert.Equal(2, control.SelectedPageNumber);
    }

    [AvaloniaFact]
    public void WheelUp_OnTheFirstPage_StaysPut()
    {
        var control = CreateSinglePage(10);
        var window = Show(control);

        Wheel(window, NotchesToTurn * 2, deltaY: 1);
        RunLayout();

        Assert.Equal(1, control.SelectedPageNumber);
    }

    [AvaloniaFact]
    public void PreviousPage_LandsAtItsBottom()
    {
        var control = CreateSinglePage(10, pageHeight: 1000);
        Show(control);
        control.GoToPage(3);
        RunLayout();

        control.GoToAdjacentPage(-1);
        RunLayout();

        var scroll = control.Scroll!;
        Assert.Equal(2, control.SelectedPageNumber);
        Assert.Equal(scroll.Extent.Height - scroll.Viewport.Height, scroll.Offset.Y, 1);
    }

    [AvaloniaFact]
    public void RepeatedTurns_BeforeLayoutSettles_EachAdvanceOnePage()
    {
        var control = CreateSinglePage(10);
        Show(control);

        // No dispatcher pumping in between: SelectedPageNumber is still 1 for the second call.
        control.GoToAdjacentPage(1);
        control.GoToAdjacentPage(1);
        RunLayout();

        Assert.Equal(3, control.SelectedPageNumber);
    }

    [AvaloniaFact]
    public void NextPage_OnTheLastPage_StaysPut()
    {
        var control = CreateSinglePage(3);
        Show(control);
        control.GoToPage(3);
        RunLayout();

        control.GoToAdjacentPage(1);
        RunLayout();

        Assert.Equal(3, control.SelectedPageNumber);
    }

    [AvaloniaTheory]
    [InlineData(Key.End, 10)]
    [InlineData(Key.Home, 1)]
    [InlineData(Key.Right, 6)]
    [InlineData(Key.Left, 4)]
    public void Keys_SinglePage_TurnPages(Key key, int expectedPage)
    {
        var control = CreateSinglePage(10);
        var window = Show(control);
        control.GoToPage(5);
        RunLayout();
        control.Scroll!.Focus();

        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        RunLayout();

        Assert.Equal(expectedPage, control.SelectedPageNumber);
    }
}
