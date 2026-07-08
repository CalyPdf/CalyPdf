using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Caly.Core.Controls;

namespace Caly.Tests;

/// <summary>
/// Detach/reattach lifecycle tests for <see cref="PageItemsControl"/>'s per-container
/// event wiring. A tab tear-off (Tabalonia) detaches and reattaches the control while
/// the virtualizing panel keeps its realized containers, so no container is
/// re-prepared — the wiring must survive the Unloaded/Loaded cycle.
/// </summary>
public class PageItemsControlLifecycleTests
{
    private static T InScope<T>(T control, INameScope scope) where T : Control
    {
        scope.Register(control.Name!, control);
        return control;
    }

    /// <summary>
    /// A <see cref="PageItemsControl"/> with minimal inline templates: the control's
    /// own template provides the required PART_ScrollViewer/PART_LayoutTransformControl
    /// chain (ScrollViewer itself is templated by the Fluent theme), and a style gives
    /// each <see cref="PageItem"/> container a template exposing the interactive layer.
    /// </summary>
    private static PageItemsControl CreatePageItemsControl()
    {
        var control = new PageItemsControl
        {
            Template = new FuncControlTemplate<PageItemsControl>((_, scope) =>
                InScope(new ScrollViewer
                {
                    Name = "PART_ScrollViewer",
                    Content = InScope(new LayoutTransformControl
                    {
                        Name = "PART_LayoutTransformControl",
                        Child = new ItemsPresenter()
                    }, scope)
                }, scope)),
            ItemsSource = new[] { new object() }
        };

        control.Styles.Add(new Style(x => x.OfType<PageItem>())
        {
            Setters =
            {
                new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<PageItem>((_, scope) =>
                    InScope(new PageInteractiveLayerControl
                    {
                        Name = "PART_PageInteractiveLayerControl",
                        Width = 100,
                        Height = 100
                    }, scope)))
            }
        });

        return control;
    }

    /// <summary>
    /// Raises PointerExited on the page's interactive layer (a direct — non-routing —
    /// event, hence the per-container subscription under test) and asserts the
    /// control's handler ran, i.e. it cleared <see cref="PageItemsControl.InteractiveActionOver"/>.
    /// </summary>
    private static void AssertPointerExitedIsHandled(PageItemsControl control, PageItem pageItem)
    {
        control.SetCurrentValue(PageItemsControl.InteractiveActionOverProperty, "Open 'https://example.com'");

        var layer = pageItem.InteractiveLayer!;
        layer.RaiseEvent(new PointerEventArgs(
            InputElement.PointerExitedEvent,
            layer,
            new Pointer(1, PointerType.Mouse, isPrimary: true),
            null,
            default,
            0,
            new PointerPointProperties(),
            KeyModifiers.None));

        Assert.Null(control.InteractiveActionOver);
    }

    [AvaloniaFact]
    public void PointerExitedWiring_SurvivesDetachAndReattach()
    {
        var control = CreatePageItemsControl();
        var window = new Window { Content = control };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var pageItem = Assert.IsType<PageItem>(control.ContainerFromIndex(0));
        Assert.NotNull(pageItem.InteractiveLayer);

        // Sanity: wiring works while attached.
        AssertPointerExitedIsHandled(control, pageItem);

        // Detach and reattach the whole control (tab tear-off / move to another window).
        window.Content = null;
        Dispatcher.UIThread.RunJobs();
        window.Content = control;
        Dispatcher.UIThread.RunJobs();

        // The precondition for the bug: the panel kept its realized container,
        // so PrepareContainerForItemOverride did NOT run again.
        Assert.Same(pageItem, control.ContainerFromIndex(0));

        // The wiring must have been re-established on reload.
        AssertPointerExitedIsHandled(control, pageItem);
    }
}
