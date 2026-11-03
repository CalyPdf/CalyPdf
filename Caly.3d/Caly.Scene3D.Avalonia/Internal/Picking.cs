using System.Numerics;
using Avalonia;

namespace Caly.Scene3D.Avalonia.Internal;

/// <summary>
/// The picking pass's arithmetic: each draw item is drawn in a flat colour that is its node plus one, in
/// 24 bits of RGB (black is the background), and only the framebuffer pixel under the click is drawn.
/// </summary>
internal static class Picking
{
    /// <summary>The largest node a colour can carry.</summary>
    public const int MaxNode = 0xFFFFFE;

    /// <summary>The colour that stands for <paramref name="node"/>, as the shader's 0..1 channels.</summary>
    public static Vector3 Encode(int node)
    {
        int id = node + 1;
        return new Vector3((id >> 16) & 255, (id >> 8) & 255, id & 255) / 255f;
    }

    /// <summary>The node a read-back RGBA pixel stands for, or -1 for the background.</summary>
    public static int Decode(ReadOnlySpan<byte> rgba) => ((rgba[0] << 16) | (rgba[1] << 8) | rgba[2]) - 1;

    /// <summary>
    /// The one framebuffer pixel under <paramref name="point"/> (device-independent pixels, in the control),
    /// as a region of the whole view. <paramref name="region"/> is the part of the view the control shows.
    /// </summary>
    public static ViewRegion PickRegion(ViewRegion region, Point point, Size bounds, double scaling)
    {
        // The framebuffer's size, as the render callback computes it.
        int pixelWidth = Math.Max(1, (int)(bounds.Width * scaling)), pixelHeight = Math.Max(1, (int)(bounds.Height * scaling));
        int px = Math.Clamp((int)Math.Floor(point.X * scaling), 0, pixelWidth - 1);
        int py = Math.Clamp((int)Math.Floor(point.Y * scaling), 0, pixelHeight - 1);

        float width = region.Right - region.Left, height = region.Bottom - region.Top;
        float left = region.Left + width * px / pixelWidth, top = region.Top + height * py / pixelHeight;
        return new ViewRegion(left, top, left + width / pixelWidth, top + height / pixelHeight);
    }
}
