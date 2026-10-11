using Avalonia;
using Avalonia.Controls;
using Caly.Avalonia.Scene3D;

namespace Scene3DViewer;

/// <summary>
/// Plays the part a zoomed, scrolled page will play in Caly. The host is a viewport of fixed size onto a
/// 3D view that is <see cref="Zoom"/> times as large; it places its <see cref="Scene3DControl"/> over the
/// part of that view the viewport shows, at device resolution, and tells the control which part that is.
/// The control's framebuffer is therefore never larger than the viewport, whatever the zoom.
/// </summary>
internal sealed class ZoomedViewHost : Panel
{
    public static readonly StyledProperty<double> ZoomProperty = AvaloniaProperty.Register<ZoomedViewHost, double>(nameof(Zoom), 1);

    public static readonly StyledProperty<Point> CentreProperty = AvaloniaProperty.Register<ZoomedViewHost, Point>(nameof(Centre), new Point(0.5, 0.5));

    static ZoomedViewHost()
    {
        AffectsArrange<ZoomedViewHost>(ZoomProperty, CentreProperty);
        ClipToBoundsProperty.OverrideDefaultValue<ZoomedViewHost>(true);
    }

    /// <summary>How many times larger than the viewport the whole view is.</summary>
    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    /// <summary>The point of the whole view, in fractions of it, shown at the middle of the viewport.</summary>
    public Point Centre
    {
        get => GetValue(CentreProperty);
        set => SetValue(CentreProperty, value);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double zoom = Math.Max(Zoom, 0.01);
        var full = new Size(finalSize.Width * zoom, finalSize.Height * zoom);

        // Where the whole view lies relative to the viewport, then the part of it inside the viewport.
        var origin = new Point(finalSize.Width / 2 - Centre.X * full.Width, finalSize.Height / 2 - Centre.Y * full.Height);
        Rect visible = new Rect(origin, full).Intersect(new Rect(finalSize));

        foreach (Control child in Children)
        {
            child.Arrange(visible);
            if (child is Scene3DControl view && visible.Width > 0 && visible.Height > 0)
            {
                view.VisibleRegion = new Rect((visible.X - origin.X) / full.Width, (visible.Y - origin.Y) / full.Height, visible.Width / full.Width, visible.Height / full.Height);
            }
        }

        return finalSize;
    }
}
