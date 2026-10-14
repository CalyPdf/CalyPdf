using System.Buffers;
using Caly.Rendering.Scene3D;
using SkiaSharp;

namespace Caly.Avalonia.Scene3D.Internal;

/// <summary>A texture as pixels: RGBA, 8 bits each, not premultiplied, rows top to bottom, in a pooled array.</summary>
internal sealed class DecodedTexture : IDisposable
{
    private byte[]? _rented;

    public DecodedTexture(byte[] rented, int width, int height, bool hasAlpha)
    {
        _rented = rented;
        Width = width;
        Height = height;
        HasAlpha = hasAlpha;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Whether any pixel is less than fully opaque.</summary>
    public bool HasAlpha { get; }

    public int ByteCount => Width * Height * 4;

    /// <summary>The pixels; empty once disposed.</summary>
    public ReadOnlySpan<byte> Pixels => _rented is null ? default : _rented.AsSpan(0, ByteCount);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _rented, null) is { } rented)
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
}

/// <summary>
/// Decodes the encoded images of a <see cref="SceneTexture"/> with SkiaSharp. Images are untrusted: sizes
/// are checked from the header before any pixel memory is taken, and whatever goes wrong yields "no
/// texture", never an exception. Safe to call from any thread.
/// </summary>
internal static unsafe class TextureDecoder
{
    private const SceneTextureChannels ColorChannels = SceneTextureChannels.Rgb | SceneTextureChannels.Luminance;

    /// <summary>Whether the texture has an image this decoder can turn into colour.</summary>
    public static bool HasColorImage(SceneTexture texture) => FindImages(texture, out _) >= 0;

    /// <summary>
    /// The size of the texture's colour image, read from its header without decoding it. False for a
    /// texture without a usable image or whose header claims more than <paramref name="maxSourcePixels"/>.
    /// </summary>
    public static bool TryGetSize(SceneTexture texture, long maxSourcePixels, out int width, out int height)
    {
        width = height = 0;
        int colorIndex = FindImages(texture, out _);
        if (colorIndex < 0)
        {
            return false;
        }

        try
        {
            ReadOnlyMemory<byte> data = texture.Images[colorIndex].Data;
            using System.Buffers.MemoryHandle pin = data.Pin();
            using SKData encoded = SKData.Create((IntPtr)pin.Pointer, data.Length);
            using SKCodec? codec = SKCodec.Create(encoded);
            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0 || (long)codec.Info.Width * codec.Info.Height > maxSourcePixels)
            {
                return false;
            }

            width = codec.Info.Width;
            height = codec.Info.Height;
            return true;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return false;
        }
    }

    /// <summary>A size scaled down, keeping its proportions, so that its longer side is at most <paramref name="maxSide"/>.</summary>
    public static (int Width, int Height) FitSize(int width, int height, int maxSide)
    {
        if (width <= 0 || height <= 0)
        {
            return (0, 0);
        }

        float scale = MathF.Min(1f, maxSide / (float)Math.Max(width, height));
        return (Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)));
    }

    /// <summary>The index of the colour image to use, or -1; and of a separate alpha image, or -1.</summary>
    private static int FindImages(SceneTexture texture, out int alphaIndex)
    {
        int colorIndex = -1;
        alphaIndex = -1;
        for (int i = 0; i < texture.Images.Count; i++)
        {
            SceneTextureImage image = texture.Images[i];
            if (image.Data.IsEmpty || image.Encoding is not (SceneImageEncoding.Png or SceneImageEncoding.Jpeg24 or SceneImageEncoding.Jpeg8))
            {
                continue;
            }

            if (colorIndex < 0 && (image.Channels & ColorChannels) != 0)
            {
                colorIndex = i;
            }
            else if (alphaIndex < 0 && image.Channels == SceneTextureChannels.Alpha)
            {
                alphaIndex = i;
            }
        }

        return colorIndex;
    }

    /// <summary>
    /// Decodes a texture's colour image, combined with its separate alpha image if it has one.
    /// </summary>
    /// <param name="maxSide">Longest side of the result; larger images are scaled down.</param>
    /// <param name="maxSourcePixels">Images whose header claims more pixels than this are refused.</param>
    /// <returns>The pixels, or null when the texture has no image this can use.</returns>
    public static DecodedTexture? Decode(SceneTexture texture, int maxSide, long maxSourcePixels)
    {
        int colorIndex = FindImages(texture, out int alphaIndex);
        if (colorIndex < 0)
        {
            return null;
        }

        byte[]? pixels = null;
        try
        {
            pixels = DecodeImage(texture.Images[colorIndex].Data, maxSide, maxSourcePixels, 0, 0, out int width, out int height, out bool ownAlpha);
            if (pixels is null)
            {
                return null;
            }

            bool hasAlpha = ownAlpha && AnyTransparent(pixels.AsSpan(0, width * height * 4));
            if (alphaIndex >= 0)
            {
                // The alpha image at the colour image's size: its own alpha channel if it has one, else its grey level.
                byte[]? alpha = DecodeImage(texture.Images[alphaIndex].Data, maxSide, maxSourcePixels, width, height, out _, out _, out bool alphaHasAlpha);
                if (alpha is not null)
                {
                    int source = alphaHasAlpha ? 3 : 0;
                    for (int i = 0; i < width * height * 4; i += 4)
                    {
                        pixels[i + 3] = alpha[i + source];
                    }

                    ArrayPool<byte>.Shared.Return(alpha);
                    hasAlpha = AnyTransparent(pixels.AsSpan(0, width * height * 4));
                }
            }

            var result = new DecodedTexture(pixels, width, height, hasAlpha);
            pixels = null;
            return result;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // SkiaSharp reports most failures by return value; this is for the ones it throws.
            return null;
        }
        finally
        {
            if (pixels is not null)
            {
                ArrayPool<byte>.Shared.Return(pixels);
            }
        }
    }

    /// <summary>
    /// Decodes one image into a rented RGBA array. With a forced size (both non-zero) the result has
    /// exactly that size; otherwise the image's own, scaled down to <paramref name="maxSide"/>.
    /// </summary>
    private static byte[]? DecodeImage(ReadOnlyMemory<byte> data, int maxSide, long maxSourcePixels, int forcedWidth, int forcedHeight, out int width, out int height, out bool hasAlphaChannel)
    {
        width = height = 0;
        hasAlphaChannel = false;

        using System.Buffers.MemoryHandle pin = data.Pin();
        using SKData encoded = SKData.Create((IntPtr)pin.Pointer, data.Length);
        using SKCodec? codec = SKCodec.Create(encoded);
        if (codec is null)
        {
            return null;
        }

        SKImageInfo source = codec.Info;
        if (source.Width <= 0 || source.Height <= 0 || (long)source.Width * source.Height > maxSourcePixels)
        {
            return null;
        }

        hasAlphaChannel = source.AlphaType != SKAlphaType.Opaque;

        // The size wanted, and the nearest size the codec can decode to directly (JPEG can skip detail).
        int targetWidth = forcedWidth, targetHeight = forcedHeight;
        if (forcedWidth <= 0 || forcedHeight <= 0)
        {
            (targetWidth, targetHeight) = FitSize(source.Width, source.Height, maxSide);
        }

        float wanted = MathF.Max(targetWidth / (float)source.Width, targetHeight / (float)source.Height);
        SKSizeI decodeSize = wanted < 1 ? codec.GetScaledDimensions(wanted) : new SKSizeI(source.Width, source.Height);
        if (decodeSize.Width < targetWidth || decodeSize.Height < targetHeight || decodeSize.Width > source.Width || decodeSize.Height > source.Height)
        {
            decodeSize = new SKSizeI(source.Width, source.Height);
        }

        var decodeInfo = new SKImageInfo(decodeSize.Width, decodeSize.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        byte[] decoded = ArrayPool<byte>.Shared.Rent(decodeInfo.BytesSize);
        bool keep = false;
        try
        {
            fixed (byte* destination = decoded)
            {
                SKCodecResult result = codec.GetPixels(decodeInfo, (IntPtr)destination);
                if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
                {
                    return null;
                }

                if (decodeSize.Width == targetWidth && decodeSize.Height == targetHeight)
                {
                    width = targetWidth;
                    height = targetHeight;
                    keep = true;
                    return decoded;
                }

                var targetInfo = new SKImageInfo(targetWidth, targetHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                byte[] scaled = ArrayPool<byte>.Shared.Rent(targetInfo.BytesSize);
                fixed (byte* scaledPointer = scaled)
                {
                    using var from = new SKPixmap(decodeInfo, (IntPtr)destination);
                    using var to = new SKPixmap(targetInfo, (IntPtr)scaledPointer);
                    if (!from.ScalePixels(to, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)))
                    {
                        ArrayPool<byte>.Shared.Return(scaled);
                        return null;
                    }
                }

                width = targetWidth;
                height = targetHeight;
                return scaled;
            }
        }
        finally
        {
            if (!keep)
            {
                ArrayPool<byte>.Shared.Return(decoded);
            }
        }
    }

    private static bool AnyTransparent(ReadOnlySpan<byte> rgba)
    {
        for (int i = 3; i < rgba.Length; i += 4)
        {
            if (rgba[i] != 255)
            {
                return true;
            }
        }

        return false;
    }
}
