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
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using Caly.Core.Utilities;
using CommunityToolkit.Mvvm.Messaging;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Rendering.Skia;

namespace Caly.Core.Services;

internal sealed partial class PdfPigDocumentService
{
    private static readonly TimeSpan PageTimeOut = TimeSpan.FromSeconds(30); // TODO - Make that a setting
    
    public async Task<IRef<SKPicture>?> GetRenderPageAsync(int pageNumber, CancellationToken token)
    {
        Debug.ThrowOnUiThread();

        SKPicture? pic = await GuardDispose(async guardCt =>
        {
            await WaitForDocumentToOpen(guardCt);
            var document = _document;
            if (document is null)
            {
                return null;
            }

            return await ExecuteWithLockAsync(lockCt =>
                {
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(lockCt);
                    try
                    {
                        linkedCts.CancelAfter(PageTimeOut);
                        var picture = document?.GetPageAsSKPicture(pageNumber, linkedCts.Token);

                        // The renderer catches exceptions per annotation, the cancellation among them, so
                        // a render cancelled while drawing annotations returns a picture without them
                        // (e.g. the poster of 3D artwork). Such a picture must not be cached.
                        if (picture is not null && linkedCts.IsCancellationRequested)
                        {
                            picture.Dispose();
                            linkedCts.Token.ThrowIfCancellationRequested();
                        }

                        return picture;
                    }
                    catch (Exception) when (linkedCts.IsCancellationRequested)
                    {
                        // Cancelled by the caller or by the timeout. Also any other exception once
                        // cancelled: PdfPig wraps everything a page of an encrypted document throws,
                        // the cancellation included, in a PdfDocumentEncryptedException. It must not
                        // become a cached error picture.
                        // An OperationCanceledException while neither token is cancelled is not a
                        // timeout: it is an error, handled below.
                        if (!lockCt.IsCancellationRequested)
                        {
                            App.Messenger.Send(new ShowNotificationMessage(NotificationType.Error,
                                $"Error in page {pageNumber}",
                                $"Could not display page after {PageTimeOut.TotalSeconds} seconds."));
                            return GetTimeOutPicture(document, pageNumber, lockCt);
                        }

                        return null;
                    }
                    catch (Exception e)
                    {
                        Debug.WriteExceptionToFile(e);
                        return GetErrorPicture(document, pageNumber, e, lockCt);
                    }
                }, guardCt)
                .ConfigureAwait(false);
        }, token);

        return pic is null ? null : RefCountable.Create(pic);
    }

    private static SKPicture? GetTimeOutPicture(PdfDocument document, int pageNumber, CancellationToken token)
    {
        return GetCalyStatusPicture(document, pageNumber, $"Could not display page after {PageTimeOut.TotalSeconds:0.##} seconds.", token);
    }

    private static SKPicture? GetErrorPicture(PdfDocument document, int pageNumber, Exception ex, CancellationToken token)
    {
        return GetCalyStatusPicture(document, pageNumber, ex.ToString(), token);
    }
    
    private static SKPicture? GetCalyStatusPicture(PdfDocument document, int pageNumber, string text, CancellationToken token)
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
                info = document.GetPage<PdfPageSize>(pageNumber, token);
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
                using (var paint = new SKPaint())
                {
                    paint.Color = SKColors.Red;
                    paint.IsAntialias = true;

                    float lineY = size + 1;
                    foreach (var textLine in text.Split('\n'))
                    {
                        canvas.DrawShapedText(textLine, new SKPoint(0, lineY), skFont, paint);
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
