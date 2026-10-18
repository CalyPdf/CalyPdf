using System;
using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Caly.Scene3D.Avalonia;

/// <summary>
/// A 3D coordinate axis gizmo showing world axes (X = Red, Y = Green, Z = Blue)
/// oriented according to the camera's orientation.
/// </summary>
public class AxisGizmoControl : Control
{
    /// <summary>Defines the <see cref="CameraOrientation"/> property.</summary>
    public static readonly StyledProperty<Quaternion> CameraOrientationProperty =
        AvaloniaProperty.Register<AxisGizmoControl, Quaternion>(nameof(CameraOrientation), Quaternion.Identity);

    /// <summary>Defines the <see cref="GizmoSize"/> property.</summary>
    public static readonly StyledProperty<double> GizmoSizeProperty =
        AvaloniaProperty.Register<AxisGizmoControl, double>(nameof(GizmoSize), 64.0);

    private static readonly ImmutableSolidColorBrush XBrush = new(Color.FromRgb(229, 57, 53));
    private static readonly ImmutableSolidColorBrush YBrush = new(Color.FromRgb(67, 160, 71));
    private static readonly ImmutableSolidColorBrush ZBrush = new(Color.FromRgb(30, 136, 229));
    private static readonly ImmutableSolidColorBrush BackgroundBrush = new(Color.FromArgb(40, 0, 0, 0));
    private static readonly ImmutableSolidColorBrush BackgroundBorderBrush = new(Color.FromArgb(20, 255, 255, 255));
    private static readonly ImmutableSolidColorBrush CenterBrush = new(Color.FromRgb(180, 180, 180));
    private static readonly ImmutableSolidColorBrush NegativeBrush = new(Color.FromArgb(120, 160, 160, 160));

    private static readonly ImmutablePen XPen = new(XBrush, 2.5);
    private static readonly ImmutablePen YPen = new(YBrush, 2.5);
    private static readonly ImmutablePen ZPen = new(ZBrush, 2.5);
    private static readonly ImmutablePen BackgroundPen = new(BackgroundBorderBrush, 1.0);
    private static readonly ImmutablePen NegativePen = new(NegativeBrush, 1.2);

    // Per control, not per process: a FormattedText keeps the font memory of the application instance that was current
    // when it was made, and a process that starts several (headless tests) would draw with memory already freed.
    private readonly FormattedText _textX = CreateLabel("X");
    private readonly FormattedText _textY = CreateLabel("Y");
    private readonly FormattedText _textZ = CreateLabel("Z");

    static AxisGizmoControl()
    {
        AffectsRender<AxisGizmoControl>(CameraOrientationProperty, GizmoSizeProperty);
        AffectsMeasure<AxisGizmoControl>(GizmoSizeProperty);
    }

    public AxisGizmoControl()
    {
        IsHitTestVisible = false;
    }

    /// <summary>The orientation of the camera in world space.</summary>
    public Quaternion CameraOrientation
    {
        get => GetValue(CameraOrientationProperty);
        set => SetValue(CameraOrientationProperty, value);
    }

    /// <summary>The size (width and height) of the gizmo in device-independent pixels.</summary>
    public double GizmoSize
    {
        get => GetValue(GizmoSizeProperty);
        set => SetValue(GizmoSizeProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double size = GizmoSize;
        return new Size(size, size);
    }

    private static FormattedText CreateLabel(string text)
    {
        return new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold),
            9.0,
            Brushes.White);
    }

    private readonly struct AxisInfo
    {
        public readonly ImmutableSolidColorBrush Brush;
        public readonly ImmutablePen Pen;
        public readonly FormattedText Text;
        public readonly Point EndPoint;
        public readonly Point NegEndPoint;
        public readonly float Depth;

        public AxisInfo(ImmutableSolidColorBrush brush, ImmutablePen pen, FormattedText text, Point endPoint, Point negEndPoint, float depth)
        {
            Brush = brush;
            Pen = pen;
            Text = text;
            EndPoint = endPoint;
            NegEndPoint = negEndPoint;
            Depth = depth;
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        double width = Bounds.Width;
        double height = Bounds.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        Point center = new(width / 2.0, height / 2.0);
        double radius = Math.Min(width, height) / 2.0;
        double armLength = radius * 0.65;
        double capRadius = Math.Clamp(radius * 0.24, 6.0, 9.0);

        // Draw subtle background disc
        context.DrawEllipse(BackgroundBrush, BackgroundPen, center, radius - 1.0, radius - 1.0);

        Quaternion q = CameraOrientation;
        if (!float.IsFinite(q.X) || !float.IsFinite(q.Y) || !float.IsFinite(q.Z) || !float.IsFinite(q.W) || q.LengthSquared() < 1e-6f)
        {
            q = Quaternion.Identity;
        }
        else
        {
            q = Quaternion.Normalize(q);
        }

        Vector3 camRight = Vector3.Transform(Vector3.UnitX, q);
        Vector3 camUp = Vector3.Transform(Vector3.UnitY, q);
        Vector3 camForward = Vector3.Transform(-Vector3.UnitZ, q);

        // Project X, Y, Z axes
        AxisInfo a0 = ProjectAxis(Vector3.UnitX, XBrush, XPen, _textX, camRight, camUp, camForward, center, armLength);
        AxisInfo a1 = ProjectAxis(Vector3.UnitY, YBrush, YPen, _textY, camRight, camUp, camForward, center, armLength);
        AxisInfo a2 = ProjectAxis(Vector3.UnitZ, ZBrush, ZPen, _textZ, camRight, camUp, camForward, center, armLength);

        // Sort axes by depth ascending (lowest depth / furthest back drawn first)
        if (a0.Depth > a1.Depth)
        {
            (a0, a1) = (a1, a0);
        }
        if (a1.Depth > a2.Depth)
        {
            (a1, a2) = (a2, a1);
        }
        if (a0.Depth > a1.Depth)
        {
            (a0, a1) = (a1, a0);
        }

        // Draw negative axis stubs for axes pointing backwards (depth < 0)
        DrawNegativeStub(context, center, a0);
        DrawNegativeStub(context, center, a1);
        DrawNegativeStub(context, center, a2);

        // Center dot
        context.DrawEllipse(CenterBrush, null, center, 3.0, 3.0);

        // Draw positive axes from back to front
        DrawPositiveAxis(context, center, a0, capRadius);
        DrawPositiveAxis(context, center, a1, capRadius);
        DrawPositiveAxis(context, center, a2, capRadius);
    }

    private static void DrawNegativeStub(DrawingContext context, Point center, in AxisInfo axis)
    {
        if (axis.Depth > 0)
        {
            context.DrawLine(NegativePen, center, axis.NegEndPoint);
            context.DrawEllipse(NegativeBrush, null, axis.NegEndPoint, 2.0, 2.0);
        }
    }

    private static void DrawPositiveAxis(DrawingContext context, Point center, in AxisInfo axis, double capRadius)
    {
        context.DrawLine(axis.Pen, center, axis.EndPoint);
        context.DrawEllipse(axis.Brush, null, axis.EndPoint, capRadius, capRadius);

        Point textOrigin = new(axis.EndPoint.X - axis.Text.Width / 2.0, axis.EndPoint.Y - axis.Text.Height / 2.0);
        context.DrawText(axis.Text, textOrigin);
    }

    private static AxisInfo ProjectAxis(
        Vector3 worldDir,
        ImmutableSolidColorBrush brush,
        ImmutablePen pen,
        FormattedText text,
        Vector3 camRight,
        Vector3 camUp,
        Vector3 camForward,
        Point center,
        double armLength)
    {
        float sx = Vector3.Dot(worldDir, camRight);
        float sy = -Vector3.Dot(worldDir, camUp);
        float sz = -Vector3.Dot(worldDir, camForward);

        Point endPoint = new(center.X + sx * armLength, center.Y + sy * armLength);
        Point negEndPoint = new(center.X - sx * armLength * 0.7, center.Y - sy * armLength * 0.7);

        return new AxisInfo(brush, pen, text, endPoint, negEndPoint, sz);
    }
}
