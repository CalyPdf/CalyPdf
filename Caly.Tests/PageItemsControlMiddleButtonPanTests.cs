using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Caly.Core;
using Caly.Core.Controls;
using Caly.Core.Models;
using Caly.Pdf.Models;
using static Caly.Tests.Fakes.PageItemsControlTestHost;

namespace Caly.Tests;

public class PageItemsControlMiddleButtonPanTests
{
    [AvaloniaFact]
    public void MiddleDragOverAPage_PansAndRestoresTheCursor()
    {
        var control = Create(10, pageHeight: 1000);
        control.TextSelection = new TextSelection(10);
        var window = Show(control);

        // With a text layer, the text-selection handler runs and marks the release handled.
        foreach (var layer in control.GetVisualDescendants().OfType<PageInteractiveLayerControl>())
        {
            layer.PdfTextLayer = new PdfTextLayer([], []);
            layer.VisibleArea = new Rect(layer.Bounds.Size); // The layer only takes pointer events once it has a visible area
        }

        RunLayout();

        var scroll = control.Scroll!;
        var start = new Point(ViewportSize / 2, 300); // Over a page

        window.MouseDown(start, MouseButton.Middle);
        window.MouseMove(start, RawInputModifiers.MiddleMouseButton);
        window.MouseMove(start - new Point(0, 200), RawInputModifiers.MiddleMouseButton);
        RunLayout();

        Assert.Equal(200, scroll.Offset.Y, 1);
        Assert.Equal(App.PanCursor, control.Cursor);

        window.MouseUp(start - new Point(0, 200), MouseButton.Middle);
        RunLayout();

        Assert.Equal(App.DefaultCursor, control.Cursor);
    }
}
