using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using Caly.Core.Controls;
using Caly.Core.Converters;

namespace Caly.Tests;

/// <summary>
/// Zooming must keep the content under the zoom origin in place, even deep into a long
/// document. The page gap is a zoom-dependent margin (see <see cref="ZoomToPageSpacingConverter"/>),
/// so every zoom step changes the size of every page in panel space. That makes the
/// <see cref="VirtualizingStackPanel"/> drop its known first-realized position and
/// re-estimate it as <c>index * averageSize</c>, so pages far down the document jumped.
/// </summary>
public class PageItemsControlZoomAnchorTests
{
    private const int PageCount = 400;
    private const double PageSize = 100;

    private static T InScope<T>(T control, INameScope scope) where T : Control
    {
        scope.Register(control.Name!, control);
        return control;
    }

    private static PageItemsControl CreatePageItemsControl()
    {
        var control = new PageItemsControl
        {
            MinZoomLevel = 0.1,
            MaxZoomLevel = 10,
            // Mirrors PageItemsControl.axaml, including its virtualizing items panel.
            Template = new FuncControlTemplate<PageItemsControl>((owner, scope) =>
                InScope(new ScrollViewer
                {
                    Name = "PART_ScrollViewer",
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
                            ItemsPanel = owner.ItemsPanel
                        }
                    }, scope)
                }, scope)),
            ItemsSource = Enumerable.Range(0, PageCount).Select(_ => new object()).ToArray()
        };

        // Mirrors the PageItem control theme: a constant on-screen gap via a zoom-dependent margin.
        control.Styles.Add(new Style(x => x.OfType<PageItem>())
        {
            Setters =
            {
                new Setter(Layoutable.MarginProperty, new Binding
                {
                    Path = nameof(PageItemsControl.ZoomLevel),
                    RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor)
                    {
                        AncestorType = typeof(PageItemsControl)
                    },
                    Converter = ZoomToPageSpacingConverter.Instance,
                    ConverterParameter = "5"
                }),
                new Setter(Layoutable.UseLayoutRoundingProperty, false),
                new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<PageItem>((_, scope) =>
                    InScope(new PageInteractiveLayerControl
                    {
                        Name = "PART_PageInteractiveLayerControl",
                        Width = PageSize,
                        Height = PageSize
                    }, scope)))
            }
        });

        return control;
    }

    private static void RunLayout()
    {
        for (int i = 0; i < 5; ++i)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// Finds the realized page under the viewport's vertical centre and returns its index
    /// and the Y of its top edge, in viewport coordinates.
    /// </summary>
    private static (int Index, double Top) PageAtViewportCentre(PageItemsControl control, double centreY)
    {
        var scroll = control.Scroll!;
        foreach (var container in control.GetRealizedContainers())
        {
            var top = container.TranslatePoint(default, scroll)!.Value.Y;
            var bottom = container.TranslatePoint(new Point(0, container.Bounds.Height), scroll)!.Value.Y;
            if (top <= centreY && centreY < bottom)
            {
                return (control.IndexFromContainer(container), top);
            }
        }

        throw new InvalidOperationException("No realized page under the viewport centre.");
    }

    private static double PageTop(PageItemsControl control, int index)
    {
        var container = control.ContainerFromIndex(index);
        Assert.NotNull(container);
        return container.TranslatePoint(default, control.Scroll!)!.Value.Y;
    }

    [AvaloniaTheory]
    [InlineData(1.1)]
    [InlineData(1 / 1.1)]
    [InlineData(2.0)]
    [InlineData(0.5)]
    public void ExternalZoom_DeepInDocument_KeepsPageUnderViewportCentreInPlace(double dZoom)
    {
        var control = CreatePageItemsControl();
        var window = new Window { Width = 400, Height = 400, Content = control };
        window.Show();
        RunLayout();

        control.ScrollIntoView(299);
        RunLayout();
        var page300 = control.ContainerFromIndex(299)!;
        control.Scroll!.Offset = new Vector(0, page300.Bounds.Top + 30);
        RunLayout();

        // The external zoom path zooms around the centre of the control.
        double centreY = (int)(control.Bounds.Height / 2) - control.Scroll!.TranslatePoint(default, control)!.Value.Y;

        var (index, topBefore) = PageAtViewportCentre(control, centreY);
        Assert.InRange(index, 290, 310);

        control.SetCurrentValue(PageItemsControl.ZoomLevelProperty, dZoom);
        RunLayout();

        double expectedTop = centreY - (centreY - topBefore) * dZoom;
        Assert.Equal(expectedTop, PageTop(control, index), 1.5);
    }

    /// <summary>
    /// Zooming must not lay out pages far from the zoom origin on the way (e.g. by laying out
    /// at the new scale with the old scroll offset), which recycles the visible pages'
    /// containers onto other pages and back again: visible as flicker.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1.1)]
    [InlineData(1 / 1.1)]
    [InlineData(2.0)]
    [InlineData(0.5)]
    public void ExternalZoom_DeepInDocument_DoesNotRealizeDistantPages(double dZoom)
    {
        var control = CreatePageItemsControl();
        var window = new Window { Width = 400, Height = 400, Content = control };
        window.Show();
        RunLayout();

        control.ScrollIntoView(299);
        RunLayout();
        var page300 = control.ContainerFromIndex(299)!;
        control.Scroll!.Offset = new Vector(0, page300.Bounds.Top + 30);
        RunLayout();

        double centreY = (int)(control.Bounds.Height / 2) - control.Scroll!.TranslatePoint(default, control)!.Value.Y;
        var (index, _) = PageAtViewportCentre(control, centreY);
        var anchorContainer = control.ContainerFromIndex(index);

        var realized = new HashSet<int>();
        void Record(object? sender, EventArgs e)
        {
            foreach (var container in control.GetRealizedContainers())
            {
                realized.Add(control.IndexFromContainer(container));
            }
        }

        control.ItemsPanelRoot!.LayoutUpdated += Record;
        control.SetCurrentValue(PageItemsControl.ZoomLevelProperty, dZoom);
        RunLayout();
        control.ItemsPanelRoot!.LayoutUpdated -= Record;

        // Enough pages to fill the viewport at the smallest zoom tested, plus slack.
        Assert.All(realized, i => Assert.InRange(i, index - 6, index + 6));
        Assert.Same(anchorContainer, control.ContainerFromIndex(index));
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(-1)]
    public void CtrlWheelZoom_DeepInDocument_KeepsPageUnderPointerInPlace(int wheelDelta)
    {
        var control = CreatePageItemsControl();
        var window = new Window { Width = 400, Height = 400, Content = control };
        window.Show();
        RunLayout();

        control.ScrollIntoView(299);
        RunLayout();
        var page300 = control.ContainerFromIndex(299)!;
        control.Scroll!.Offset = new Vector(0, page300.Bounds.Top + 30);
        RunLayout();

        const double pointerY = 150;
        var (index, topBefore) = PageAtViewportCentre(control, pointerY);
        Assert.InRange(index, 290, 310);

        window.MouseWheel(new Point(50, pointerY), new Vector(0, wheelDelta), RawInputModifiers.Control);
        RunLayout();

        double dZoom = Math.Round(Math.Pow(1.1, wheelDelta), 4);
        Assert.Equal(dZoom, control.ZoomLevel, 6);

        double expectedTop = pointerY - (pointerY - topBefore) * dZoom;
        Assert.Equal(expectedTop, PageTop(control, index), 1.5);
    }
}
