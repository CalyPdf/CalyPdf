using Caly.Printing.Core;
using SkiaSharp;

namespace Caly.Tests;

public class PrintServiceHelperTests
{
    [Fact]
    public void ConvertToGrayscaleInPlace_PureRed_GivesBT601Luma()
    {
        // BT.601: Y = 0.299*R + 0.587*G + 0.114*B
        // Pure red (255,0,0) → Y ≈ 76.245 → 76
        using var bitmap = new SKBitmap(1, 1, SKColorType.Bgra8888, SKAlphaType.Premul);
        bitmap.SetPixel(0, 0, new SKColor(255, 0, 0, 255));

        PrintServiceHelper.ConvertToGrayscaleInPlace(bitmap);

        var p = bitmap.GetPixel(0, 0);
        Assert.Equal(76, p.Red);
        Assert.Equal(76, p.Green);
        Assert.Equal(76, p.Blue);
        Assert.Equal(255, p.Alpha);
    }

    [Fact]
    public void ConvertToGrayscaleInPlace_PureWhite_StaysWhite()
    {
        using var bitmap = new SKBitmap(2, 2, SKColorType.Bgra8888, SKAlphaType.Premul);
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 2; x++)
                bitmap.SetPixel(x, y, SKColors.White);

        PrintServiceHelper.ConvertToGrayscaleInPlace(bitmap);

        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 2; x++)
            {
                var p = bitmap.GetPixel(x, y);
                Assert.Equal(255, p.Red);
                Assert.Equal(255, p.Green);
                Assert.Equal(255, p.Blue);
            }
    }

    [Fact]
    public void ConvertToGrayscaleInPlace_PureBlack_StaysBlack()
    {
        using var bitmap = new SKBitmap(1, 1, SKColorType.Bgra8888, SKAlphaType.Premul);
        bitmap.SetPixel(0, 0, SKColors.Black);

        PrintServiceHelper.ConvertToGrayscaleInPlace(bitmap);

        var p = bitmap.GetPixel(0, 0);
        Assert.Equal(0, p.Red);
        Assert.Equal(0, p.Green);
        Assert.Equal(0, p.Blue);
    }
}
