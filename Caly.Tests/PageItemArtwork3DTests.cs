using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Caly.Avalonia.Scene3D;
using Caly.Core.Controls;
using Caly.Core.ViewModels;
using static Caly.Tests.Fakes.DocumentTestHarness;

namespace Caly.Tests;

/// <summary>
/// A page builds its 3D view only while one of its artworks is being viewed, and never carries it
/// across a container recycling cycle: a page without 3D holds an empty panel and nothing else.
/// </summary>
public class PageItemArtwork3DTests
{
    private static PageItem CreatePageItem()
    {
        return new PageItem
        {
            Template = new FuncControlTemplate<PageItem>((_, scope) =>
            {
                var layer = new PageInteractiveLayerControl { Name = "PART_PageInteractiveLayerControl", Width = 400, Height = 300 };
                scope.Register(layer.Name, layer);
                var host = new Panel { Name = "PART_Artwork3DHost", Width = 400, Height = 300 };
                scope.Register(host.Name, host);
                return new Panel { Children = { layer, host } };
            })
        };
    }

    private static Panel Host(PageItem item) =>
        Assert.IsType<Panel>(item.GetTemplateChildren().Single(c => c.Name == "PART_Artwork3DHost"));

    private static PageItem ShowPageItem(out Panel parent)
    {
        parent = new Panel();
        var window = new Window { Content = parent };
        window.Show();

        var item = CreatePageItem();
        parent.Children.Add(item);
        Dispatcher.UIThread.RunJobs();
        return item;
    }

    private static async Task<Artwork3DViewModel> ReadyArtwork(Artwork3DFixture f, int page = 1)
    {
        Artwork3DViewModel artwork = f.Activate(page);
        Assert.True(await WaitUntil(() => artwork.State == Artwork3DState.Ready));
        return artwork;
    }

    [AvaloniaFact]
    public void APageWithoutActiveArtworkBuildsNothing()
    {
        var item = ShowPageItem(out _);

        Assert.Empty(Host(item).Children);
        Assert.Empty(item.GetVisualDescendants().OfType<Scene3DControl>());
    }

    [AvaloniaFact]
    public async Task TheViewIsBuiltWhenAnArtworkBecomesActiveAndDroppedWhenItCloses()
    {
        await using var f = new Artwork3DFixture();
        Artwork3DViewModel artwork = await ReadyArtwork(f);
        var item = ShowPageItem(out _);
        item.VisibleArea = new Rect(0, 0, 400, 300);
        item.ZoomLevel = 2;

        item.ActiveArtwork3D = artwork;

        var view = Assert.IsType<Artwork3DView>(Assert.Single(Host(item).Children));
        Assert.Same(artwork, view.DataContext);
        Assert.Equal(new Rect(0, 0, 400, 300), view.VisibleArea);
        Assert.Equal(2, view.ZoomLevel);

        // The page's own state reaches the view as it changes.
        item.VisibleArea = new Rect(0, 100, 400, 200);
        item.ZoomLevel = 3;
        Assert.Equal(new Rect(0, 100, 400, 200), view.VisibleArea);
        Assert.Equal(3, view.ZoomLevel);

        // Setting the same artwork again builds nothing new.
        item.ActiveArtwork3D = artwork;
        Assert.Same(view, Assert.Single(Host(item).Children));

        item.ActiveArtwork3D = null;
        Assert.Empty(Host(item).Children);
    }

    [AvaloniaFact]
    public async Task TheViewIsDroppedWithItsPageAndBuiltAgainWhenThePageComesBack()
    {
        await using var f = new Artwork3DFixture();
        Artwork3DViewModel artwork = await ReadyArtwork(f);
        var item = ShowPageItem(out Panel parent);
        item.ActiveArtwork3D = artwork;
        Control first = Assert.Single(Host(item).Children);

        parent.Children.Remove(item);
        Assert.Empty(Host(item).Children);

        parent.Children.Add(item);
        Dispatcher.UIThread.RunJobs();
        Control second = Assert.Single(Host(item).Children);
        Assert.NotSame(first, second);
        Assert.Same(artwork, second.DataContext);
    }

    /// <summary>Review focus: the container is recycled for another page while a view is live.</summary>
    [AvaloniaFact]
    public async Task RecyclingTheContainerForAnotherPageReplacesOrDropsTheView()
    {
        await using var f = new Artwork3DFixture();
        Artwork3DViewModel artwork = await ReadyArtwork(f);
        var item = ShowPageItem(out Panel parent);
        item.ActiveArtwork3D = artwork;

        // Recycled for a page without 3D while still in the tree (the binding gives null).
        item.ActiveArtwork3D = null;
        Assert.Empty(Host(item).Children);

        // Recycled for a page with another active artwork: taken out, given the new page, put back.
        item.ActiveArtwork3D = artwork;
        Control first = Assert.Single(Host(item).Children);
        parent.Children.Remove(item);
        Artwork3DViewModel other = await ReadyArtwork(f, page: 2);
        item.ActiveArtwork3D = other;
        Assert.Empty(Host(item).Children);
        parent.Children.Add(item);
        Dispatcher.UIThread.RunJobs();

        Control second = Assert.Single(Host(item).Children);
        Assert.NotSame(first, second);
        Assert.Same(other, second.DataContext);
    }

    [AvaloniaFact]
    public async Task ATemplateWithoutTheHostIsLeftAlone()
    {
        await using var f = new Artwork3DFixture();
        Artwork3DViewModel artwork = await ReadyArtwork(f);
        var item = new PageItem
        {
            Template = new FuncControlTemplate<PageItem>((_, scope) =>
            {
                var layer = new PageInteractiveLayerControl { Name = "PART_PageInteractiveLayerControl", Width = 100, Height = 100 };
                scope.Register(layer.Name, layer);
                return layer;
            })
        };
        var window = new Window { Content = item };
        window.Show();

        item.ActiveArtwork3D = artwork;

        Assert.Empty(item.GetVisualDescendants().OfType<Scene3DControl>());
    }
}
