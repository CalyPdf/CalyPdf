using Caly.Core.Controls.Rendering;
using SkiaSharp;

namespace Caly.Tests;

/// <summary>
/// Tile sampling, as measured (docs/sampling-bench): nearest-pixel is ~4x cheaper than bilinear on
/// the CPU for a scaled tile, but on the GPU the two cost about the same, and mipmaps cost the GPU
/// 39% more texture memory and 2.7x slower settled frames when tiles are shrunk below ~0.71x.
/// </summary>
public class TiledPdfPageControlSamplingTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Gpu_IsAlwaysBilinearWithoutMipmaps(bool interactive)
    {
        var sampling = TiledPdfPageControl.GetSamplingOptions(interactive, gpu: true);

        Assert.Equal(SKFilterMode.Linear, sampling.Filter);
        Assert.Equal(SKMipmapMode.None, sampling.Mipmap);
    }

    [Fact]
    public void Software_IsNearestWhileMoving()
    {
        var sampling = TiledPdfPageControl.GetSamplingOptions(interactive: true, gpu: false);

        Assert.Equal(SKFilterMode.Nearest, sampling.Filter);
        Assert.Equal(SKMipmapMode.None, sampling.Mipmap);
    }

    [Fact]
    public void Software_IsBilinearWithoutMipmapsOnceSettled()
    {
        var sampling = TiledPdfPageControl.GetSamplingOptions(interactive: false, gpu: false);

        Assert.Equal(SKFilterMode.Linear, sampling.Filter);
        Assert.Equal(SKMipmapMode.None, sampling.Mipmap);
    }
}
