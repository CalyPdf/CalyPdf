using System.Numerics;
using Avalonia;
using Caly.Scene3D.Avalonia.Internal;

namespace Caly.Scene3D.Avalonia.Tests;

/// <summary>How a node becomes a colour in the picking pass and back, and which pixel of the view a click is.</summary>
public class PickingTests
{
    private static byte[] Bytes(Vector3 colour) =>
    [
        (byte)MathF.Round(colour.X * 255), (byte)MathF.Round(colour.Y * 255), (byte)MathF.Round(colour.Z * 255), 255
    ];

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(65536)]
    [InlineData(0xFFFFFE)] // the largest node 24 bits of colour carry, as node + 1
    public void EncodeThenDecodeGivesTheNodeBack(int node)
    {
        Assert.Equal(node, Picking.Decode(Bytes(Picking.Encode(node))));
    }

    [Fact]
    public void BlackIsNoNode()
    {
        Assert.Equal(-1, Picking.Decode([0, 0, 0, 255]));
    }

    [Fact]
    public void PickRegionIsThePixelUnderThePoint()
    {
        ViewRegion pixel = Picking.PickRegion(ViewRegion.Full, new Point(50, 25), new Size(200, 100), 1);

        Assert.Equal(0.25f, pixel.Left, 6);
        Assert.Equal(0.25f, pixel.Top, 6);
        Assert.Equal(0.255f, pixel.Right, 6);
        Assert.Equal(0.26f, pixel.Bottom, 6);
    }

    [Fact]
    public void PickRegionUsesTheScaling()
    {
        // 300 x 150 framebuffer pixels; the point is pixel (75, 37).
        ViewRegion pixel = Picking.PickRegion(ViewRegion.Full, new Point(50, 25), new Size(200, 100), 1.5);

        Assert.Equal(0.25f, pixel.Left, 6);
        Assert.Equal(37f / 150, pixel.Top, 6);
        Assert.Equal(0.25f + 1f / 300, pixel.Right, 6);
        Assert.Equal(38f / 150, pixel.Bottom, 6);
    }

    [Fact]
    public void PickRegionIsInsideTheVisibleRegion()
    {
        ViewRegion pixel = Picking.PickRegion(new ViewRegion(0.5f, 0, 1, 0.5f), new Point(0, 0), new Size(100, 100), 1);

        Assert.Equal(0.5f, pixel.Left, 6);
        Assert.Equal(0f, pixel.Top, 6);
        Assert.Equal(0.505f, pixel.Right, 6);
        Assert.Equal(0.005f, pixel.Bottom, 6);
    }

    [Fact]
    public void PickRegionClampsAPointOnTheEdge()
    {
        ViewRegion pixel = Picking.PickRegion(ViewRegion.Full, new Point(200, 100), new Size(200, 100), 1);

        Assert.Equal(199f / 200, pixel.Left, 6);
        Assert.Equal(99f / 100, pixel.Top, 6);
        Assert.Equal(1f, pixel.Right, 6);
        Assert.Equal(1f, pixel.Bottom, 6);
    }
}
