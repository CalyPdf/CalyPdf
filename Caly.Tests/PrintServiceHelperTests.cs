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

    // SharpIppNext >= 4.1.1 sends a Content-Length computed from the document stream
    // (Length - the position captured when the request content was built) and 4.2.4 rewinds
    // to that same position before every send. A stream handed to Send-Document must therefore
    // be seekable and start at offset 0, or CUPS receives a truncated JPEG.
    [Fact]
    public void EncodeJpeg_ReturnsSeekableStreamAtOffsetZero()
    {
        using var bitmap = new SKBitmap(8, 8, SKColorType.Bgra8888, SKAlphaType.Premul);
        bitmap.Erase(SKColors.Red);

        using var stream = PrintServiceHelper.EncodeJpeg(bitmap);

        Assert.True(stream.CanSeek);
        Assert.Equal(0, stream.Position);
        Assert.True(stream.Length > 0);

        // The whole JPEG is readable from the returned position.
        var bytes = new byte[stream.Length];
        Assert.Equal(bytes.Length, stream.Read(bytes, 0, bytes.Length));

        // JPEG SOI marker.
        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0xD8, bytes[1]);
    }
}
