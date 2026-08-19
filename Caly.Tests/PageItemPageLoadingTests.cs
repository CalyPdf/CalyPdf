// Copyright (c) BobLd
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Caly.Core.Controls;

namespace Caly.Tests;

/// <summary>
/// The page-loading skeleton is built only while a page is rendering, and never carried across a
/// container recycling cycle.
/// </summary>
public class PageItemPageLoadingTests
{
    private static PageItem CreatePageItem()
    {
        return new PageItem
        {
            Template = new FuncControlTemplate<PageItem>((_, scope) =>
            {
                var host = new Panel { Name = "PART_PageLoadingHost", Width = 200, Height = 300 };
                scope.Register(host.Name, host);

                var layer = new PageInteractiveLayerControl
                {
                    Name = "PART_PageInteractiveLayerControl",
                    Width = 200,
                    Height = 300
                };
                scope.Register(layer.Name, layer);

                return new Panel { Children = { host, layer } };
            })
        };
    }

    private static Panel Host(PageItem item) =>
        Assert.IsType<Panel>(item.GetTemplateChildren().Single(c => c.Name == "PART_PageLoadingHost"));

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

    [AvaloniaFact]
    public void NoSkeletonIsBuiltWhileThePageIsNotRendering()
    {
        var item = ShowPageItem(out _);

        Assert.Empty(Host(item).Children);
    }

    [AvaloniaFact]
    public void SkeletonIsShownWhileThePageRenders()
    {
        var item = ShowPageItem(out _);

        item.SetValue(PageItem.IsPageLoadingProperty, true);

        Assert.Single(Host(item).Children);
        Assert.IsAssignableFrom<Control>(Host(item).Children[0]);
    }

    [AvaloniaFact]
    public void SkeletonIsRemovedWhenRenderingFinishes()
    {
        var item = ShowPageItem(out _);

        item.SetValue(PageItem.IsPageLoadingProperty, true);
        Assert.Single(Host(item).Children);

        item.SetValue(PageItem.IsPageLoadingProperty, false);

        Assert.Empty(Host(item).Children);
    }

    [AvaloniaFact]
    public void RecycledContainerGetsAFreshSkeletonWhileStillRendering()
    {
        var item = ShowPageItem(out var parent);

        item.SetValue(PageItem.IsPageLoadingProperty, true);
        var original = Host(item).Children[0];

        // Recycle the container while the page is still rendering.
        parent.Children.Remove(item);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(Host(item).Children);

        parent.Children.Add(item);
        Dispatcher.UIThread.RunJobs();

        var rebuilt = Assert.Single(Host(item).Children);
        Assert.NotSame(original, rebuilt);
    }
}
