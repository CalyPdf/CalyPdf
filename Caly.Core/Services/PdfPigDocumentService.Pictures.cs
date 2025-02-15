// Copyright (c) 2025 BobLd
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
using System.Threading;
using System.Threading.Tasks;
using Caly.Core.Utilities;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using UglyToad.PdfPig.Rendering.Skia;

namespace Caly.Core.Services;

internal sealed partial class PdfPigDocumentService
{
    public async Task<IRef<SKPicture>?> GetRenderPageAsync(int pageNumber, CancellationToken token)
    {
        Debug.ThrowOnUiThread();

        SKPicture? pic = await GuardDispose(async ct =>
        {
            return await ExecuteWithLockAsync(lct =>
            {
                try
                {
                    return _document?.GetPage<SKPicture>(pageNumber);
                }
                catch (Exception e)
                {
                    Debug.WriteExceptionToFile(e);
                    return GetErrorPicture(pageNumber, e, lct);
                }
            }, ct)
                .ConfigureAwait(false);
        }, token);

        return pic is null ? null : RefCountable.Create(pic);
    }

    private SKPicture? GetErrorPicture(int pageNumber, Exception ex, CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return null;
        }

        try
        {
            // Try get page size
            PdfPageSize? info = null;

            try
            {
                info = _document?.GetPage<PdfPageSize>(pageNumber) ?? throw new NullReferenceException();
            }
            catch (Exception)
            {
                if (token.IsCancellationRequested)
                {
                    return null;
                }
            }

            float width = (float)(info?.Width ?? 100);
            float height = (float)(info?.Height ?? 100);

            if (token.IsCancellationRequested)
            {
                return null;
            }

            using (var recorder = new SKPictureRecorder())
            using (var canvas = recorder.BeginRecording(SKRect.Create(width, height)))
            {
                float size = 9;
                using (var drawTypeface = SKTypeface.CreateDefault())
                using (var skFont = drawTypeface.ToFont(size))
                using (var paint = new SKPaint(skFont))
                {
                    paint.Color = SKColors.Red;
                    paint.IsAntialias = true;

                    float lineY = size + 1;
                    foreach (var textLine in ex.ToString().Split('\n'))
                    {
                        canvas.DrawShapedText(textLine, new SKPoint(0, lineY), paint);
                        lineY += size;
                    }
                }

                canvas.Flush();

                return recorder.EndRecording();
            }
        }
        catch (Exception e)
        {
            Debug.WriteExceptionToFile(e);
        }

        return null;
    }
}
