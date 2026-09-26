using System.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using Caly.Core.Controls;

namespace Caly.Tests.Fakes;

/// <summary>
/// Headless host for <see cref="PageItemsControl"/>: a template mirroring PageItemsControl.axaml,
/// with the presenter's ItemsPanel bound to the control's (as the real TemplateBinding does) so the
/// panel can be swapped at runtime, and fixed-size pages with no inter-page gap.
/// </summary>
internal static class PageItemsControlTestHost
{
    public const double ViewportSize = 400;
    public const double PageWidth = 100;

    private static T InScope<T>(T control, INameScope scope) where T : Control
    {
        scope.Register(control.Name!, control);
        return control;
    }

    public static PageItemsControl Create(int pageCount, double pageHeight = 100)
        => Create(Enumerable.Range(0, pageCount).Select(_ => new object()).ToArray(), pageCount, pageHeight);

    public static PageItemsControl Create(IList items, int pageCount, double pageHeight = 100)
    {
        var control = new PageItemsControl
        {
            MinZoomLevel = 0.1,
            MaxZoomLevel = 10,
            PageCount = pageCount,
            Focusable = true,
            Template = new FuncControlTemplate<PageItemsControl>((owner, scope) =>
                InScope(new ScrollViewer
                {
                    Name = "PART_ScrollViewer",
                    Focusable = true,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = InScope(new LayoutTransformControl
                    {
                        Name = "PART_LayoutTransformControl",
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        ClipToBounds = true,
                        Child = new ItemsPresenter
                        {
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Stretch,
                            [!ItemsPresenter.ItemsPanelProperty] = owner[!ItemsControl.ItemsPanelProperty]
                        }
                    }, scope)
                }, scope)),
            ItemsSource = items
        };

        control.Styles.Add(new Style(x => x.OfType<PageItem>())
        {
            Setters =
            {
                new Setter(Layoutable.UseLayoutRoundingProperty, false),
                new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<PageItem>((_, scope) =>
                    InScope(new PageInteractiveLayerControl
                    {
                        Name = "PART_PageInteractiveLayerControl",
                        Width = PageWidth,
                        Height = pageHeight
                    }, scope)))
            }
        });

        return control;
    }

    public static Window Show(PageItemsControl control)
    {
        var window = new Window { Width = ViewportSize, Height = ViewportSize, Content = control };
        window.Show();
        RunLayout();
        return window;
    }

    public static void RunLayout()
    {
        for (int i = 0; i < 5; ++i)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }
}
