using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Caly.Core.Controls;
using static Caly.Tests.Fakes.PageItemsControlTestHost;

namespace Caly.Tests;

public class SinglePageVirtualizingPanelTests
{
    private static PageItemsControl CreateSinglePage(int pageCount)
    {
        var control = Create(pageCount);
        control.ItemsPanel = new FuncTemplate<Panel?>(() => new SinglePageVirtualizingPanel());
        return control;
    }

    [AvaloniaFact]
    public void RealizesOnlyTheFirstPageInitially()
    {
        var control = CreateSinglePage(10);
        Show(control);

        Assert.IsType<SinglePageVirtualizingPanel>(control.ItemsPanelRoot);
        Assert.Single(control.GetRealizedContainers());
        Assert.NotNull(control.ContainerFromIndex(0));
        Assert.Null(control.ContainerFromIndex(1));
        Assert.Equal(new Range(1, 2), control.RealisedPages);
        Assert.Equal(new Range(1, 2), control.VisiblePages);
    }

    [AvaloniaFact]
    public void StartsOnTheSelectedPage()
    {
        var control = CreateSinglePage(10);
        control.SelectedPageNumber = 7;
        Show(control);

        Assert.NotNull(control.ContainerFromIndex(6));
        Assert.Equal(new Range(7, 8), control.RealisedPages);
    }

    [AvaloniaFact]
    public void ScrollIntoView_SwapsThePageInTheSameContainer()
    {
        var control = CreateSinglePage(10);
        Show(control);
        var container = control.ContainerFromIndex(0);

        control.ScrollIntoView(4);

        Assert.Same(container, control.ContainerFromIndex(4));
        Assert.Null(control.ContainerFromIndex(0));
        Assert.Equal(4, control.IndexFromContainer(container!));
        Assert.Equal(4, ((SinglePageVirtualizingPanel)control.ItemsPanelRoot!).RealizedIndex);
    }

    [AvaloniaFact]
    public void ItemsReset_KeepsThePagePositionClampedToTheNewCount()
    {
        var control = CreateSinglePage(10);
        Show(control);
        control.ScrollIntoView(4);

        control.ItemsSource = Enumerable.Range(0, 3).Select(_ => new object()).ToArray();
        RunLayout();

        Assert.NotNull(control.ContainerFromIndex(2));
    }

    [AvaloniaFact]
    public void AppendedPages_DoNotRecycleThePageOnScreen()
    {
        var items = new ObservableCollection<object>();
        var control = Create(items, pageCount: 3);
        control.ItemsPanel = new FuncTemplate<Panel?>(() => new SinglePageVirtualizingPanel());
        Show(control);
        Assert.Empty(control.GetRealizedContainers());

        items.Add(new object());
        RunLayout();
        var first = control.ContainerFromIndex(0);
        Assert.NotNull(first);

        // Recycling hands the same instance straight back from the pool, so checking the
        // container's identity alone cannot catch it: count clear/prepare cycles instead.
        int cleared = 0, prepared = 0;
        control.ContainerClearing += (_, _) => cleared++;
        control.ContainerPrepared += (_, _) => prepared++;

        items.Add(new object());
        items.Add(new object());
        RunLayout();

        Assert.Equal(0, cleared);
        Assert.Equal(0, prepared);
        Assert.Same(first, control.ContainerFromIndex(0));
        Assert.Single(control.GetRealizedContainers());
    }
}
