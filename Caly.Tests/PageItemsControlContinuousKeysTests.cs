using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Caly.Core.Controls;
using static Caly.Tests.Fakes.PageItemsControlTestHost;

namespace Caly.Tests;

/// <summary>
/// Keyboard navigation in continuous mode (the default). Single-page mode repurposes some of
/// these keys to turn pages; continuous mode must keep scrolling the document.
/// </summary>
public class PageItemsControlContinuousKeysTests
{
    private static Window ShowFocused(PageItemsControl control)
    {
        var window = Show(control);
        control.Scroll!.Focus();
        return window;
    }

    private static void Press(Window window, Key key)
    {
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        RunLayout();
    }

    [AvaloniaFact]
    public void End_GoesToTheTopOfTheLastPage()
    {
        // The last page is taller than the viewport, so its top is not the end of the
        // document: End stops at the page top rather than scrolling to the bottom.
        var control = Create(10, pageHeight: 1000);
        var window = ShowFocused(control);

        Press(window, Key.End);

        var lastPage = control.ContainerFromIndex(9);
        Assert.NotNull(lastPage);
        var scroll = control.Scroll!;
        Assert.Equal(lastPage.Bounds.Top, scroll.Offset.Y, 1);
        Assert.True(scroll.Offset.Y < scroll.Extent.Height - scroll.Viewport.Height);
        Assert.Equal(10, control.SelectedPageNumber);
    }

    [AvaloniaFact]
    public void Home_GoesToTheTopOfTheFirstPage()
    {
        var control = Create(10, pageHeight: 1000);
        var window = ShowFocused(control);
        control.GoToPage(6, 300);
        RunLayout();
        Assert.Equal(6, control.SelectedPageNumber);

        Press(window, Key.Home);

        Assert.Equal(0, control.Scroll!.Offset.Y, 1);
        Assert.Equal(1, control.SelectedPageNumber);
    }

    [AvaloniaFact]
    public void Right_ScrollsOneScreenDown()
    {
        var control = Create(10, pageHeight: 1000);
        var window = ShowFocused(control);
        var scroll = control.Scroll!;

        Press(window, Key.Right);

        Assert.Equal(scroll.Viewport.Height, scroll.Offset.Y, 1);
        Assert.Equal(1, control.SelectedPageNumber);
    }

    [AvaloniaFact]
    public void Left_ScrollsOneScreenUp()
    {
        var control = Create(10, pageHeight: 1000);
        var window = ShowFocused(control);
        var scroll = control.Scroll!;
        scroll.Offset = new Vector(0, 1500);
        RunLayout();

        Press(window, Key.Left);

        Assert.Equal(1500 - scroll.Viewport.Height, scroll.Offset.Y, 1);
    }

    [AvaloniaTheory]
    [InlineData(Key.PageDown, 4)]
    [InlineData(Key.PageUp, 2)]
    public void PageKeys_GoToTheTopOfTheAdjacentPage(Key key, int expectedPage)
    {
        var control = Create(10, pageHeight: 1000);
        var window = ShowFocused(control);
        control.GoToPage(3, 200);
        RunLayout();
        Assert.Equal(3, control.SelectedPageNumber);

        Press(window, key);

        var page = control.ContainerFromIndex(expectedPage - 1);
        Assert.NotNull(page);
        Assert.Equal(expectedPage, control.SelectedPageNumber);
        Assert.Equal(page.Bounds.Top, control.Scroll!.Offset.Y, 1);
    }
}
