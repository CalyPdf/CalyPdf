using System.Buffers.Binary;
using Caly.Avalonia.Scene3D.Internal;
using Scene3D;
using SkiaSharp;

namespace Caly.Avalonia.Scene3D.Tests;

public class TextureDecoderTests
{
    private const int MaxSide = 2048;
    private const long MaxPixels = 32_000_000;

    private static byte[] Encode(int width, int height, SKEncodedImageFormat format, Func<int, int, SKColor> pixel, SKColorType colorType = SKColorType.Rgba8888)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, colorType, colorType == SKColorType.Gray8 ? SKAlphaType.Opaque : SKAlphaType.Unpremul));
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, pixel(x, y));
            }
        }

        using SKData data = bitmap.Encode(format, 100);
        return data.ToArray();
    }

    private static SceneTexture Texture(params SceneTextureImage[] images) => new("T", 1, 1, SceneTextureChannels.Rgba, images);

    private static SceneTextureImage Color(byte[] data, SceneImageEncoding encoding = SceneImageEncoding.Png) => new(encoding, SceneTextureChannels.Rgb, data);

    private static (byte R, byte G, byte B, byte A) Pixel(DecodedTexture texture, int x, int y)
    {
        int at = (y * texture.Width + x) * 4;
        return (texture.Pixels[at], texture.Pixels[at + 1], texture.Pixels[at + 2], texture.Pixels[at + 3]);
    }

    [Fact]
    public void APngDecodesToRgbaRowsTopToBottom()
    {
        byte[] png = Encode(4, 2, SKEncodedImageFormat.Png, (x, y) => new SKColor((byte)(x * 60), (byte)(y * 200), 7));

        using DecodedTexture texture = TextureDecoder.Decode(Texture(Color(png)), MaxSide, MaxPixels)!;

        Assert.Equal((4, 2), (texture.Width, texture.Height));
        Assert.False(texture.HasAlpha);
        Assert.Equal(32, texture.ByteCount);
        Assert.Equal((0, 0, 7, 255), Pixel(texture, 0, 0));
        Assert.Equal((180, 0, 7, 255), Pixel(texture, 3, 0));
        Assert.Equal((60, 200, 7, 255), Pixel(texture, 1, 1));
    }

    [Fact]
    public void AJpegDecodes()
    {
        byte[] jpeg = Encode(64, 32, SKEncodedImageFormat.Jpeg, (_, _) => new SKColor(200, 100, 50));

        using DecodedTexture texture = TextureDecoder.Decode(Texture(Color(jpeg, SceneImageEncoding.Jpeg24)), MaxSide, MaxPixels)!;

        Assert.Equal((64, 32), (texture.Width, texture.Height));
        (byte r, byte g, byte b, byte a) = Pixel(texture, 30, 15);
        Assert.InRange(r, 195, 205);
        Assert.InRange(g, 95, 105);
        Assert.InRange(b, 45, 55);
        Assert.Equal(255, a);
        Assert.False(texture.HasAlpha);
    }

    [Fact]
    public void AGreyscaleJpegBecomesGreyRgb()
    {
        byte[] jpeg = Encode(16, 16, SKEncodedImageFormat.Jpeg, (_, _) => new SKColor(90, 90, 90), SKColorType.Gray8);
        var image = new SceneTextureImage(SceneImageEncoding.Jpeg8, SceneTextureChannels.Luminance, jpeg);

        using DecodedTexture texture = TextureDecoder.Decode(Texture(image), MaxSide, MaxPixels)!;

        (byte r, byte g, byte b, byte a) = Pixel(texture, 8, 8);
        Assert.InRange(r, 86, 94);
        Assert.Equal((r, r, (byte)255), (g, b, a));
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Png, SceneImageEncoding.Png)]
    [InlineData(SKEncodedImageFormat.Jpeg, SceneImageEncoding.Jpeg24)]
    public void AnImageLargerThanTheLimitIsScaledDown(SKEncodedImageFormat format, SceneImageEncoding encoding)
    {
        // Left half red, right half blue.
        byte[] data = Encode(300, 100, format, (x, _) => x < 150 ? new SKColor(255, 0, 0) : new SKColor(0, 0, 255));

        using DecodedTexture texture = TextureDecoder.Decode(Texture(Color(data, encoding)), 100, MaxPixels)!;

        Assert.Equal((100, 33), (texture.Width, texture.Height));
        (byte r, _, byte b, _) = Pixel(texture, 10, 16);
        Assert.True(r > 230 && b < 25, $"left is red: {r}, {b}");
        (r, _, b, _) = Pixel(texture, 90, 16);
        Assert.True(b > 230 && r < 25, $"right is blue: {r}, {b}");
    }

    [Fact]
    public void ASeparateAlphaImageIsCombinedIn()
    {
        byte[] color = Encode(8, 8, SKEncodedImageFormat.Jpeg, (_, _) => new SKColor(0, 200, 0));

        // Alpha as a greyscale image of another size: opaque on the left, clear on the right.
        byte[] alpha = Encode(4, 4, SKEncodedImageFormat.Png, (x, _) => x < 2 ? new SKColor(255, 255, 255) : new SKColor(0, 0, 0), SKColorType.Gray8);
        SceneTexture source = Texture(
            new SceneTextureImage(SceneImageEncoding.Png, SceneTextureChannels.Alpha, alpha),
            new SceneTextureImage(SceneImageEncoding.Jpeg24, SceneTextureChannels.Rgb, color));

        using DecodedTexture texture = TextureDecoder.Decode(source, MaxSide, MaxPixels)!;

        Assert.Equal((8, 8), (texture.Width, texture.Height));
        Assert.True(texture.HasAlpha);
        Assert.Equal(255, Pixel(texture, 0, 4).A);
        Assert.Equal(0, Pixel(texture, 7, 4).A);

        // Colour is not darkened by the alpha: the pixels are not premultiplied.
        Assert.InRange(Pixel(texture, 7, 4).G, 190, 210);
    }

    [Fact]
    public void AnImageWithItsOwnAlphaKeepsIt()
    {
        byte[] png = Encode(2, 1, SKEncodedImageFormat.Png, (x, _) => x == 0 ? new SKColor(250, 10, 10, 128) : new SKColor(10, 250, 10, 255));
        var image = new SceneTextureImage(SceneImageEncoding.Png, SceneTextureChannels.Rgba, png);

        using DecodedTexture texture = TextureDecoder.Decode(Texture(image), MaxSide, MaxPixels)!;

        Assert.True(texture.HasAlpha);
        Assert.Equal((250, 10, 10, 128), Pixel(texture, 0, 0));
        Assert.Equal((10, 250, 10, 255), Pixel(texture, 1, 0));
    }

    [Fact]
    public void ATextureWithoutAUsableColourImageDecodesToNothing()
    {
        byte[] png = Encode(2, 2, SKEncodedImageFormat.Png, (_, _) => SKColors.White);

        // External (no data), alpha only, an encoding the decoder does not take, and no images at all.
        Assert.Null(TextureDecoder.Decode(Texture(new SceneTextureImage(SceneImageEncoding.Png, SceneTextureChannels.Rgb, ReadOnlyMemory<byte>.Empty)), MaxSide, MaxPixels));
        Assert.Null(TextureDecoder.Decode(Texture(new SceneTextureImage(SceneImageEncoding.Png, SceneTextureChannels.Alpha, png)), MaxSide, MaxPixels));
        Assert.Null(TextureDecoder.Decode(Texture(new SceneTextureImage(SceneImageEncoding.Tiff, SceneTextureChannels.Rgb, png)), MaxSide, MaxPixels));
        Assert.Null(TextureDecoder.Decode(Texture(), MaxSide, MaxPixels));
    }

    /// <summary>Review focus: images are hostile input too.</summary>
    [Fact]
    public void CorruptImagesDecodeToNothing()
    {
        var random = new Random(3);
        var garbage = new byte[2000];
        random.NextBytes(garbage);
        Assert.Null(TextureDecoder.Decode(Texture(Color(garbage)), MaxSide, MaxPixels));
        Assert.Null(TextureDecoder.Decode(Texture(Color([0x89, 0x50, 0x4E, 0x47])), MaxSide, MaxPixels));

        // A real image cut short: nothing, or a partial picture; never an exception.
        byte[] png = Encode(64, 64, SKEncodedImageFormat.Png, (x, y) => new SKColor((byte)x, (byte)y, 0));
        TextureDecoder.Decode(Texture(Color(png[..(png.Length / 2)])), MaxSide, MaxPixels)?.Dispose();

        // A broken alpha image does not cost the colour image.
        byte[] good = Encode(4, 4, SKEncodedImageFormat.Png, (_, _) => SKColors.Red);
        using DecodedTexture texture = TextureDecoder.Decode(Texture(Color(good), new SceneTextureImage(SceneImageEncoding.Png, SceneTextureChannels.Alpha, garbage)), MaxSide, MaxPixels)!;
        Assert.False(texture.HasAlpha);
        Assert.Equal((255, 0, 0, 255), Pixel(texture, 1, 1));
    }

    /// <summary>Review focus: a header may claim any size; the claim alone must not cost memory.</summary>
    [Fact]
    public void AnImageClaimingEnormousDimensionsIsRefusedBeforeItsPixelsAreAllocated()
    {
        byte[] huge = PngWithHeader(60_000, 60_000);
        TextureDecoder.Decode(Texture(Color(Encode(2, 2, SKEncodedImageFormat.Png, (_, _) => SKColors.Red))), MaxSide, MaxPixels)?.Dispose();

        long before = GC.GetAllocatedBytesForCurrentThread();
        DecodedTexture? texture = TextureDecoder.Decode(Texture(Color(huge)), MaxSide, MaxPixels);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Null(texture);
        Assert.True(allocated < 100_000, $"allocated {allocated} bytes");

        // The limit is on the source's pixel count.
        byte[] small = Encode(10, 10, SKEncodedImageFormat.Png, (_, _) => SKColors.Red);
        Assert.Null(TextureDecoder.Decode(Texture(Color(small)), MaxSide, 99));
        TextureDecoder.Decode(Texture(Color(small)), MaxSide, 100)!.Dispose();
    }

    [Fact]
    public void DisposingTwiceIsHarmless()
    {
        DecodedTexture texture = TextureDecoder.Decode(Texture(Color(Encode(2, 2, SKEncodedImageFormat.Png, (_, _) => SKColors.Red))), MaxSide, MaxPixels)!;
        texture.Dispose();
        texture.Dispose();
        Assert.True(texture.Pixels.IsEmpty);
    }

    /// <summary>A PNG that is only a valid header for the given size, a scrap of data and an end.</summary>
    private static byte[] PngWithHeader(int width, int height)
    {
        using var stream = new MemoryStream();
        stream.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8;
        header[9] = 2;
        WriteChunk(stream, "IHDR", header);
        WriteChunk(stream, "IDAT", [0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01]);
        WriteChunk(stream, "IEND", default);
        return stream.ToArray();

        static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(buffer, data.Length);
            output.Write(buffer);

            var body = new byte[4 + data.Length];
            for (int i = 0; i < 4; i++)
            {
                body[i] = (byte)type[i];
            }

            data.CopyTo(body.AsSpan(4));
            output.Write(body);

            uint crc = 0xFFFFFFFF;
            foreach (byte b in body)
            {
                crc ^= b;
                for (int k = 0; k < 8; k++)
                {
                    crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
                }
            }

            BinaryPrimitives.WriteUInt32BigEndian(buffer, crc ^ 0xFFFFFFFF);
            output.Write(buffer);
        }
    }
}
