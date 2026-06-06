// Copyright (c) BobLd
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Caly.Core.Services.Interfaces;
using SkiaSharp;

namespace Caly.Printing.Core;

/// <summary>
/// Shared rendering and encoding helpers used by all platform-specific print service implementations.
/// </summary>
public static class PrintServiceHelper
{
    /// <summary>
    /// Renders a single PDF page to an <see cref="SKBitmap"/>, applying the per-page rotation
    /// stored in <paramref name="pageInfo"/>.
    /// Returns <c>null</c> if the page size or picture cannot be retrieved.
    /// </summary>
    /// <param name="overridePpiScale">
    /// When provided, overrides <see cref="IPdfDocumentService.PpiScale"/> for the render.
    /// Use this to render at a fixed print DPI rather than the screen scale.
    /// </param>
    public static async Task<SKBitmap?> RenderPageToBitmapAsync(
        IPdfDocumentService documentService,
        PrintPageInfo pageInfo,
        CancellationToken token,
        float? overridePpiScale = null)
    {
        var pageSize = await documentService.GetPageSizeAsync(pageInfo.PageNumber, token)
            .ConfigureAwait(false);
        if (pageSize is null)
        {
            return null;
        }

        using var picRef = await documentService.GetRenderPageAsync(pageInfo.PageNumber, token)
            .ConfigureAwait(false);
        if (picRef is null)
        {
            return null;
        }

        float ppiScale = overridePpiScale ?? (float)documentService.PpiScale;
        float pdfW = (float)pageSize.Value.Width;
        float pdfH = (float)pageSize.Value.Height;
        int rotation = pageInfo.Rotation;

        // Bitmap dimensions at PpiScale resolution, with rotation taken into account.
        int bitmapW = (rotation == 90 || rotation == 270)
            ? (int)(pdfH * ppiScale)
            : (int)(pdfW * ppiScale);
        int bitmapH = (rotation == 90 || rotation == 270)
            ? (int)(pdfW * ppiScale)
            : (int)(pdfH * ppiScale);

        var bitmap = new SKBitmap(bitmapW, bitmapH, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        // The SKPicture is recorded in PDF-point coordinates (pdfW × pdfH).
        // Build a combined scale + rotation matrix, mirroring the on-screen rendering in
        // SkiaPdfPageControl which does: canvas.DrawPicture(picture, SKMatrix.CreateScale(ppiScale, ppiScale)).
        SKMatrix transform = rotation switch
        {
            // 90° CW:  x' = (pdfH − y) * ppiScale,  y' = x * ppiScale
            90 => new SKMatrix(0, -ppiScale, pdfH * ppiScale, ppiScale, 0, 0, 0, 0, 1),
            // 180°:    x' = (pdfW − x) * ppiScale,  y' = (pdfH − y) * ppiScale
            180 => new SKMatrix(-ppiScale, 0, pdfW * ppiScale, 0, -ppiScale, pdfH * ppiScale, 0, 0, 1),
            // 270° CW: x' = y * ppiScale,            y' = (pdfW − x) * ppiScale
            270 => new SKMatrix(0, ppiScale, 0, -ppiScale, 0, pdfW * ppiScale, 0, 0, 1),
            _ => SKMatrix.CreateScale(ppiScale, ppiScale)
        };

        canvas.DrawPicture(picRef.Item, in transform);
        canvas.Flush();

        return bitmap;
    }


    /// <summary>
    /// Encodes <paramref name="bitmap"/> as a JPEG (quality 100) into a new <see cref="MemoryStream"/>
    /// positioned at offset 0.
    /// </summary>
    public static MemoryStream EncodeJpeg(SKBitmap bitmap)
    {
        var stream = new MemoryStream();
        using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, quality: 100);
        data.SaveTo(stream);
        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// Converts a BGRA8888 bitmap to grayscale in-place using a SkiaSharp color matrix
    /// instead of manual per-pixel iteration. Alpha is preserved.
    /// </summary>
    public static void ConvertToGrayscaleInPlace(SKBitmap bitmap)
    {
        if (bitmap is null)
        {
            throw new ArgumentNullException(nameof(bitmap));
        }

        if (bitmap.ColorType != SKColorType.Bgra8888)
        {
            throw new ArgumentException("Bitmap must be BGRA8888.", nameof(bitmap));
        }

        using var paint = new SKPaint
        {
            ColorFilter = SKColorFilter.CreateColorMatrix(new float[]
            {
                // R' = 0.299R + 0.587G + 0.114B
                0.299f, 0.587f, 0.114f, 0f, 0f,
                // G' = 0.299R + 0.587G + 0.114B
                0.299f, 0.587f, 0.114f, 0f, 0f,
                // B' = 0.299R + 0.587G + 0.114B
                0.299f, 0.587f, 0.114f, 0f, 0f,
                // A' = A
                0f,    0f,    0f,    1f, 0f
            })
        };

        using var canvas = new SKCanvas(bitmap);
        canvas.DrawBitmap(bitmap, 0, 0, paint);
    }

    public static SKBitmap RotateBitmap90Cw(SKBitmap source)
    {
        var rotated = new SKBitmap(source.Height, source.Width, source.ColorType, source.AlphaType);
        using var canvas = new SKCanvas(rotated);
        canvas.Translate(rotated.Width, 0);
        canvas.RotateDegrees(90);
        canvas.DrawBitmap(source, 0, 0);
        canvas.Flush();
        return rotated;
    }
}
