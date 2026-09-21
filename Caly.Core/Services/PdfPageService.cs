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

using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Caly.Core.Models;
using Caly.Core.Services.Interfaces;
using Caly.Core.Services.Rendering;
using Caly.Core.Utilities;
using Caly.Core.ViewModels;
using Caly.Pdf.Models;
using SkiaSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Caly.Core.Services
{
    public class PdfPageService : IAsyncDisposable
    {
        private readonly Task _processingLoopTask;

        private readonly IPdfDocumentService _pdfDocumentService;

        private readonly ChannelWriter<RenderRequest> _requestsWriter;
        private readonly ChannelReader<RenderRequest> _requestsReader;
        private readonly CancellationTokenSource _mainCts = new();
        private readonly CancellationToken _mainToken;
        private readonly CancellationGenerations _thumbnailsGenerations;
        private readonly CancellationGenerations _pagesGenerations;

        private async Task ProcessingLoop()
        {
            Debug.ThrowOnUiThread();

            var options = new ParallelOptions()
            {
                // PdfPig cannot process pages in parallel, so we limit number of request being processed in parallel.
                // The main reason to allow parallel processing of request is for the creation of the text layer
                // via `PdfTextLayerHelper.GetTextLayer()` (which is independent of PdfPig) to not block requests relying on PdfPig.
                MaxDegreeOfParallelism = 4,
                CancellationToken = _mainToken
            };
            
            try
            {
                await Parallel.ForEachAsync(_requestsReader.ReadAllAsync(_mainToken), options, async (r, _) =>
                {
                    try
                    {
                        if (r.Token.IsCancellationRequested)
                        {
                            return;
                        }

                        switch (r.Type)
                        {
                            case RenderRequestTypes.PageSize:
                                await ProcessPageSizeRequest(r);
                                break;

                            case RenderRequestTypes.Picture:
                                await ProcessPictureRequest(r);
                                break;

                            case RenderRequestTypes.Thumbnail:
                                await ProcessThumbnailRequest(r);
                                break;

                            case RenderRequestTypes.TextLayer:
                                await ProcessTextLayerRequest(r);
                                break;

                            default:
                                throw new NotImplementedException(r.Type.ToString());
                        }
                    }
                    catch (OperationCanceledException)
                    { }
                    catch (Exception e)
                    {
                        // We just ignore for the moment
                        Debug.WriteExceptionToFile(e);
                    }
                });
            }
            catch (OperationCanceledException) { }
        }

        /// <summary>
        /// <paramref name="settingsService"/> defaults to <c>null</c> so tests that only care about the
        /// document/tile pipeline, not settings, are unaffected; the DI-resolved <c>App</c> container
        /// always has one registered and passes it in (<c>AddScoped&lt;PdfPageService&gt;</c>).
        /// </summary>
        public PdfPageService(IPdfDocumentService pdfDocumentService, ISettingsService? settingsService = null)
        {
            // Read once per document, not live: changing the setting mid-document would leave already
            // cached tiles in the old format sitting alongside new ones in the new format.
            bool useCompactTileFormat = settingsService?.GetSettings().UseCompactTileFormat ?? false;
            TileRenderService = new TileRenderService(new TileCache(), startProcessingLoop: true, useCompactTileFormat);
            _pdfDocumentService = pdfDocumentService;

            var channel = Channel.CreateUnboundedPrioritized(new UnboundedPrioritizedChannelOptions<RenderRequest>()
            {
                Comparer = RenderRequestComparer.Instance,
                SingleWriter = false,
                SingleReader = false
            });

            _requestsWriter = channel.Writer;
            _requestsReader = channel.Reader;

            _mainToken = _mainCts.Token;
            _pagesGenerations = new CancellationGenerations(_mainToken);
            _thumbnailsGenerations = new CancellationGenerations(_mainToken);
            _processingLoopTask = Task.Run(ProcessingLoop, _mainToken);
        }

        public void Initialise()
        {
            System.Diagnostics.Debug.Assert(NumberOfPages > 0);

            var renderLocks = new SemaphoreSlim[NumberOfPages];
            for (int i = 0; i < NumberOfPages; ++i)
            {
                renderLocks[i] = new SemaphoreSlim(1, 1);
            }

            _renderLocks = renderLocks;
        }

        public int NumberOfPages => _pdfDocumentService.NumberOfPages;

        /// <summary>
        /// The tile render service for this document. Created in the constructor and disposed
        /// in <see cref="DisposeAsync"/>; shared across all <see cref="PageViewModel"/>s for this document.
        /// </summary>
        public TileRenderService TileRenderService { get; }

        private readonly ConcurrentDictionary<int, IRef<SKPicture>> _cachePictures = new();
        private readonly ConcurrentDictionary<int, PdfTextLayer> _cacheTextLayers = new();

        /// <summary>
        /// Number of pages to keep cached in <see cref="_cachePictures"/> and
        /// <see cref="_cacheTextLayers"/> beyond the realised range on each side.
        /// </summary>
        private const int PageCacheBuffer = 1;

        private SemaphoreSlim[]? _renderLocks;

        private async Task ProcessPageSizeRequest(RenderRequest renderRequest)
        {
            if (renderRequest.Page.IsSizeSet())
            {
                return;
            }

            var pageSize = await GetPageSize(renderRequest.Page.PageNumber, renderRequest.Token)
                .ConfigureAwait(false);

            if (pageSize.HasValue)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    renderRequest.Page.SetSize(pageSize.Value);
                });
            }
        }

        /// <summary>
        /// Get the page size, scaled by <see cref="IPdfDocumentService.PpiScale"/>.
        /// </summary>
        public async Task<Size?> GetPageSize(int pageNumber, CancellationToken token)
        {
            // No caching
            var pdfSize = await _pdfDocumentService.GetPageSizeAsync(pageNumber, token)
                .ConfigureAwait(false);

            if (!pdfSize.HasValue)
            {
                return null;
            }

            double ppiScale = _pdfDocumentService.PpiScale;
            return new Size(pdfSize.Value.Width * ppiScale, pdfSize.Value.Height * ppiScale);
        }

        public async Task<IRef<SKPicture>?> GetPicture(int pageNumber, CancellationToken token)
        {
            if (_renderLocks is null)
            {
                return null;
            }

            token.ThrowIfCancellationRequested();

            if (!_cachePictures.TryGetValue(pageNumber, out var picture))
            {
                bool hasLock = false;
                var mutex = _renderLocks[pageNumber - 1];

                try
                {
                    await mutex.WaitAsync(token);
                    hasLock = true;

                    if (_cachePictures.TryGetValue(pageNumber, out picture))
                    {
                        return picture.Clone();
                    }

                    System.Diagnostics.Debug.WriteLine($"Render page #{pageNumber} started.");
                    
                    var sw = ValueStopwatch.StartNew();
                    picture = await _pdfDocumentService.GetRenderPageAsync(pageNumber, token);
                    TimeSpan elapsed = sw.GetElapsedTime();

                    System.Diagnostics.Debug.WriteLine($"Render page #{pageNumber} done in {elapsed.TotalMilliseconds}ms.");

                    if (picture is not null)
                    {
                        System.Diagnostics.Debug.Assert(picture.IsAlive);
                        _cachePictures[pageNumber] = picture;
                    }
                }
                finally
                {
                    if (hasLock)
                    {
                        mutex.Release();
                    }
                }
            }

            return picture?.Clone();
        }

        public async Task<PdfTextLayer?> GetTextLayer(int pageNumber, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            if (!_cacheTextLayers.TryGetValue(pageNumber, out var textLayer))
            {
                var sw = ValueStopwatch.StartNew();
                textLayer = await _pdfDocumentService.GetPageTextLayerAsync(pageNumber, token);
                TimeSpan elapsed = sw.GetElapsedTime();

                System.Diagnostics.Debug.WriteLine($"Text layer page #{pageNumber} in {elapsed.TotalMilliseconds}ms.");

                if (textLayer is not null)
                {
                    _cacheTextLayers[pageNumber] = textLayer;
                }
            }

            return textLayer;
        }

        private async Task ProcessPictureRequest(RenderRequest renderRequest)
        {
            if (renderRequest.Page.PdfPicture is not null)
            {
                return;
            }

            IRef<SKPicture>? picture = null;
            try
            {
                picture = await GetPicture(renderRequest.Page.PageNumber, renderRequest.Token)
                    .ConfigureAwait(false);

                Size? pageSize = null;
                if (!renderRequest.Page.IsSizeSet())
                {
                    pageSize = await GetPageSize(renderRequest.Page.PageNumber, renderRequest.Token)
                        .ConfigureAwait(false);
                }

                if (picture is not null)
                {
                    var pictureToAssign = picture;
                    Dispatcher.UIThread.Invoke(() =>
                    {
                        renderRequest.Page.PdfPicture = pictureToAssign;

                        if (pageSize.HasValue)
                        {
                            renderRequest.Page.SetSize(pageSize.Value);
                        }
                    });
                    picture = null;
                }
            }
            finally
            {
                picture?.Dispose();
            }
        }

        private async Task ProcessThumbnailRequest(RenderRequest renderRequest)
        {
            if (renderRequest.Page.Thumbnail is not null)
            {
                return;
            }

            using var picture = await GetPicture(renderRequest.Page.PageNumber, renderRequest.Token)
                .ConfigureAwait(false);

            if (!renderRequest.Page.IsSizeSet())
            {
                // This is the first we load the page, width and height are not set yet
                var pageSize = await GetPageSize(renderRequest.Page.PageNumber, renderRequest.Token)
                    .ConfigureAwait(false);

                if (pageSize.HasValue)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        renderRequest.Page.SetSize(pageSize.Value);
                    });
                }
            }

            if (picture is not null)
            {
                await SetThumbnail(renderRequest.Page, picture.Item, renderRequest.Token)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Draws <paramref name="picture"/> into a <paramref name="target"/>-sized bitmap.
        /// </summary>
        /// <remarks>
        /// Rgb565 (2 bytes/pixel, no alpha): the colour-depth loss is invisible at these sizes.
        /// </remarks>
        private static WriteableBitmap RasterisePicture(SKPicture picture, PixelSize target,
            Size pageSize, double ppiScale)
        {
            var skImageInfo = new SKImageInfo(target.Width, target.Height,
                SKColorType.Rgb565, SKAlphaType.Opaque);

            SKMatrix scale = SKMatrix.CreateScale(target.Width / (float)(pageSize.Width / ppiScale),
                target.Height / (float)(pageSize.Height / ppiScale));

            using (var surface = SKSurface.Create(skImageInfo))
            {
                var canvas = surface.Canvas;
                canvas.Clear(SKColors.White);
                canvas.DrawPicture(picture, in scale);

#if DEBUG
                using (var skFont = SKTypeface.Default.ToFont(target.Height * (float)ppiScale / 5f, 1f))
                using (var paint = new SKPaint())
                {
                    paint.Style = SKPaintStyle.Fill;
                    paint.Color = SKColors.Blue.WithAlpha(150);
                    canvas.DrawText(picture.UniqueId.ToString(), target.Width / 4f, target.Height / 2f, skFont, paint);
                }
#endif

                var bitmap = new WriteableBitmap(
                    target,
                    new Vector(96, 96),
                    PixelFormat.Rgb565,
                    AlphaFormat.Opaque);

                using (var fb = bitmap.Lock())
                {
                    surface.ReadPixels(skImageInfo, fb.Address, fb.RowBytes, 0, 0);
                }

                return bitmap;
            }
        }

        private async Task SetThumbnail(PageViewModel vm, SKPicture picture, CancellationToken token)
        {
            Debug.ThrowOnUiThread();

            token.ThrowIfCancellationRequested();

            var thumbnail = RasterisePicture(picture, vm.ThumbnailSize, vm.Size, vm.PpiScale);

            await Dispatcher.UIThread.InvokeAsync(() => vm.Thumbnail = thumbnail, DispatcherPriority.Background);
        }

        /// <summary>
        /// Rasterises <paramref name="pageNumber"/> for a tab hover preview, but only if its
        /// picture is already cached. Returns <c>null</c> otherwise.
        /// </summary>
        /// <remarks>
        /// Deliberately not <see cref="GetPicture"/>: the caller is a document being torn down,
        /// and rendering there would defeat the teardown it is part of.
        /// </remarks>
        public WriteableBitmap? TryCapturePreview(int pageNumber, PixelSize target, Size pageSize)
        {
            if (!_cachePictures.TryGetValue(pageNumber, out var cached))
            {
                return null;
            }

            IRef<SKPicture>? picture = null;
            try
            {
                picture = cached.Clone();
                return RasterisePicture(picture.Item, target, pageSize, _pdfDocumentService.PpiScale);
            }
            catch (ObjectDisposedException)
            {
                return null;
            }
            finally
            {
                picture?.Dispose();
            }
        }

        /// <summary>
        /// Renders <paramref name="pageNumber"/> for a tab hover preview, rendering the page
        /// itself if it is not cached.
        /// </summary>
        /// <remarks>
        /// Only for the hover fallback, where the user has asked for a preview a deactivation
        /// could not capture. It leaves the page in the picture cache, so an inactive caller
        /// must clear up after it.
        /// </remarks>
        public async Task<WriteableBitmap?> RenderPreviewAsync(int pageNumber, PixelSize target,
            Size pageSize, CancellationToken token)
        {
            Debug.ThrowOnUiThread();

            using var picture = await GetPicture(pageNumber, token).ConfigureAwait(false);

            if (picture is null)
            {
                return null;
            }

            token.ThrowIfCancellationRequested();

            return RasterisePicture(picture.Item, target, pageSize, _pdfDocumentService.PpiScale);
        }

        private async Task ProcessTextLayerRequest(RenderRequest renderRequest)
        {
            renderRequest.Token.ThrowIfCancellationRequested();

            if (renderRequest.Page.PdfTextLayer is not null)
            {
                return;
            }

            var textLayer = await GetTextLayer(renderRequest.Page.PageNumber, renderRequest.Token)
                .ConfigureAwait(false);

            if (textLayer is not null)
            {
                await Dispatcher.UIThread.InvokeAsync(() => renderRequest.Page.PdfTextLayer = textLayer);
            }
        }

        public void RequestPageSize(PageViewModel page)
        {
            var request = new RenderRequest(page, RenderRequestTypes.PageSize, _mainToken);
            if (!_requestsWriter.TryWrite(request))
            {
                throw new Exception("Could not write request to channel."); // Should never happen as unbounded channel
            }
        }
        
        public async Task RefreshThumbnails(RefreshPagesRequestMessage m)
        {
            System.Diagnostics.Debug.WriteLine($"[{_pdfDocumentService.FileName}] RefreshThumbnails: '{m.VisibleThumbnails}' ('{m.RealisedThumbnails}')");

            _mainToken.ThrowIfCancellationRequested();

            var token = await _thumbnailsGenerations.BeginAsync();

            await Task.Run(async () =>
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                var document = m.Document;

                if (!m.VisibleThumbnails.HasValue || !m.RealisedThumbnails.HasValue)
                {
                    // clear all pages - batch the UI update
                    List<(PageViewModel Page, Bitmap? Thumbnail)>? allThumbnailsToClear = null;

                    for (int p = 1; p <= document.Pages.Count; ++p)
                    {
                        var page = document.GetPage(p);
                        if (page is null)
                        {
                            continue; // Pages might still be loading
                        }

                        if (page.Thumbnail is not null)
                        {
                            System.Diagnostics.Debug.WriteLine($"Cleared thumbnail #{p}'s picture.");
                            (allThumbnailsToClear ??= []).Add((page, page.Thumbnail));
                        }
                    }

                    if (allThumbnailsToClear is not null)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            foreach (var (page, _) in allThumbnailsToClear)
                            {
                                page.Thumbnail = null;
                            }
                        });

                        foreach (var (_, thumbnail) in allThumbnailsToClear)
                        {
                            thumbnail?.Dispose();
                        }
                    }

                    return;
                }

                token.ThrowIfCancellationRequested();

                var range = m.VisibleThumbnails.Value;
                int start = range.Start.GetOffset(NumberOfPages);
                int end = range.End.GetOffset(NumberOfPages);

                for (int p = start; p < end; ++p)
                {
                    var page = document.GetPage(p);
                    if (page is null)
                    {
                        continue; // Pages might still be loading
                    }

                    if (page.Thumbnail is null)
                    {
                        var request = new RenderRequest(page, RenderRequestTypes.Thumbnail, token); // No caching for the moment
                        if (!_requestsWriter.TryWrite(request))
                        {
                            throw new Exception("Could not write request to channel."); // Should never happen as unbounded channel
                        }
                    }
                }

                var realised = m.RealisedThumbnails.Value;
                int realisedStart = realised.Start.GetOffset(NumberOfPages);
                int realisedEnd = realised.End.GetOffset(NumberOfPages);

                List<(PageViewModel Page, Bitmap? Thumbnail)>? thumbnailsToClear = null;

                for (int p = 1; p <= document.Pages.Count; ++p)
                {
                    if (p >= realisedStart && p < realisedEnd)
                    {
                        continue;
                    }

                    // Thumbnail is not realised anymore
                    var page = document.GetPage(p);
                    if (page is null)
                    {
                        continue; // Pages might still be loading
                    }

                    if (page.Thumbnail is not null)
                    {
                        System.Diagnostics.Debug.WriteLine($"Cleared thumbnail #{p}'s picture.");
                        (thumbnailsToClear ??= []).Add((page, page.Thumbnail));
                    }
                }

                if (thumbnailsToClear is not null)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        foreach (var (page, _) in thumbnailsToClear)
                        {
                            page.Thumbnail = null;
                        }
                    });

                    foreach (var (_, thumbnail) in thumbnailsToClear)
                    {
                        thumbnail?.Dispose();
                    }
                }

                UpdatePictureCache(m.RealisedPages, m.VisiblePages);
            }, token);
        }

        private void UpdatePictureCache(Range? realisedPages, Range? visiblePages)
        {
            if (!realisedPages.HasValue)
            {
                // TODO - Clear cache?
                return;
            }

            var realised = realisedPages.Value;
            int realisedStart = realised.Start.GetOffset(NumberOfPages);
            int realisedEnd = realised.End.GetOffset(NumberOfPages);

            int keepStart = Math.Max(1, realisedStart - PageCacheBuffer);
            int keepEnd = Math.Min(NumberOfPages + 1, realisedEnd + PageCacheBuffer);

            EvictPicturesOutside(keepStart, keepEnd);
        }

        /// <summary>
        /// Removes and disposes every cached picture whose page number falls outside
        /// [<paramref name="keepStart"/>, <paramref name="keepEnd"/>). Pass (0, 0) to clear the
        /// whole cache unconditionally.
        /// </summary>
        private void EvictPicturesOutside(int keepStart, int keepEnd)
        {
            foreach (var kvp in _cachePictures)
            {
                if (kvp.Key >= keepStart && kvp.Key < keepEnd)
                {
                    continue;
                }

                // Page is outside the realised range, safe to evict.
                if (_cachePictures.TryRemove(kvp.Key, out var picture))
                {
                    System.Diagnostics.Debug.WriteLine($"Removed page #{kvp.Key}'s picture from cache.");
                    picture.Dispose();
                    TileRenderService.InvalidatePage(kvp.Key);
                }
            }
        }

        private void UpdateTextLayerCache(Range? realisedPages, Range? visiblePages)
        {
            if (!realisedPages.HasValue)
            {
                // TODO - Clear cache?
                return;
            }

            var realised = realisedPages.Value;
            int realisedStart = realised.Start.GetOffset(NumberOfPages);
            int realisedEnd = realised.End.GetOffset(NumberOfPages);

            int keepStart = Math.Max(1, realisedStart - PageCacheBuffer);
            int keepEnd = Math.Min(NumberOfPages + 1, realisedEnd + PageCacheBuffer);

            EvictTextLayersOutside(keepStart, keepEnd);
        }

        /// <summary>
        /// Removes every cached text layer whose page number falls outside
        /// [<paramref name="keepStart"/>, <paramref name="keepEnd"/>). Pass (0, 0) to clear the
        /// whole cache unconditionally.
        /// </summary>
        private void EvictTextLayersOutside(int keepStart, int keepEnd)
        {
            foreach (var kvp in _cacheTextLayers)
            {
                if (kvp.Key >= keepStart && kvp.Key < keepEnd)
                {
                    continue;
                }

                // Page is outside the realised range, safe to evict.
                if (_cacheTextLayers.TryRemove(kvp.Key, out _))
                {
                    System.Diagnostics.Debug.WriteLine($"Removed page #{kvp.Key}'s text layer from cache.");
                }
            }
        }

        public async Task RefreshPages(RefreshPagesRequestMessage m)
        {
            System.Diagnostics.Debug.WriteLine($"[{_pdfDocumentService.FileName}] RefreshPages: '{m.VisiblePages}' ('{m.RealisedPages}')");

            _mainToken.ThrowIfCancellationRequested();

            var token = await _pagesGenerations.BeginAsync();

            await Task.Run(async () =>
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                if (!m.VisiblePages.HasValue || !m.RealisedPages.HasValue)
                {
                    // clear all pages
                    return;
                }

                var document = m.Document;

                token.ThrowIfCancellationRequested();

                var realised = m.RealisedPages.Value;
                int realisedStart = realised.Start.Value;
                int realisedEnd = realised.End.Value;

                // Clear view models - collect pages to clear, then batch the UI update
                List<(PageViewModel Page, IRef<SKPicture>? Picture)>? picturesToClear = null;
                List<PageViewModel>? textLayersToClear = null;

                for (int p = 1; p <= document.Pages.Count; ++p)
                {
                    if (p >= realisedStart && p < realisedEnd)
                    {
                        continue;
                    }

                    var page = document.GetPage(p);
                    if (page is null)
                    {
                        continue; // Pages might still be loading
                    }

                    if (page.PdfPicture is not null)
                    {
                        System.Diagnostics.Debug.WriteLine($"Cleared page #{p}'s picture.");
                        (picturesToClear ??= []).Add((page, page.PdfPicture));
                    }

                    if (page.PdfTextLayer is not null)
                    {
                        System.Diagnostics.Debug.WriteLine($"Cleared page #{p}'s text layer.");
                        (textLayersToClear ??= []).Add(page);
                    }
                }

                token.ThrowIfCancellationRequested();

                if (picturesToClear is not null || textLayersToClear is not null)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (picturesToClear is not null)
                        {
                            foreach (var (page, _) in picturesToClear)
                            {
                                page.PdfPicture = null;
                            }
                        }

                        if (textLayersToClear is not null)
                        {
                            foreach (var page in textLayersToClear)
                            {
                                page.PdfTextLayer = null;
                            }
                        }
                    });

                    // Dispose old pictures off the UI thread
                    if (picturesToClear is not null)
                    {
                        foreach (var (_, picture) in picturesToClear)
                        {
                            picture?.Dispose();
                        }
                    }
                }

                // Picture Cache
                UpdatePictureCache(m.RealisedPages, m.VisiblePages);

                // Text Cache
                UpdateTextLayerCache(m.RealisedPages, m.VisiblePages);

                var visible = m.VisiblePages.Value;
                int visibleStart = visible.Start.Value;
                int visibleEnd = visible.End.Value;

                for (int p = visibleStart; p < visibleEnd; ++p)
                {
                    var page = document.GetPage(p);
                    if (page is null)
                    {
                        continue; // Pages might still be loading
                    }

                    // Picture
                    if (page.PdfPicture is null)
                    {
                        if (_cachePictures.TryGetValue(page.PageNumber, out var pic))
                        {
                            IRef<SKPicture>? clone = null; // Clone before sending to UI
                            try
                            {
                                clone = pic.Clone();
                            }
                            catch (Exception)
                            { /* No Op */ }

                            if (clone is not null)
                            {
                                var cloneToAssign = clone;
                                try
                                {
                                    await Dispatcher.UIThread.InvokeAsync(() => page.PdfPicture = cloneToAssign);
                                    clone = null;
                                }
                                finally
                                {
                                    clone?.Dispose();
                                }
                            }
                        }
                        else
                        {
                            var request = new RenderRequest(page, RenderRequestTypes.Picture, token);
                            if (!_requestsWriter.TryWrite(request))
                            {
                                throw new Exception("Could not write request to channel."); // Should never happen as unbounded channel
                            }
                        }
                    }

                    // TextLayer
                    if (page.PdfTextLayer is null)
                    {
                        if (_cacheTextLayers.TryGetValue(page.PageNumber, out var textLayer))
                        {
                            await Dispatcher.UIThread.InvokeAsync(() => page.PdfTextLayer = textLayer);
                        }
                        else
                        {
                            var request = new RenderRequest(page, RenderRequestTypes.TextLayer, token);
                            if (!_requestsWriter.TryWrite(request))
                            {
                                throw new Exception("Could not write request to channel."); // Should never happen as unbounded channel
                            }
                        }
                    }
                }
            }, token);
        }

        public async Task CancelAndClear()
        {
            await _pagesGenerations.CancelCurrentAsync();
            await _thumbnailsGenerations.CancelCurrentAsync();

            // Caches - full clear, no buffer retained.
            EvictPicturesOutside(0, 0);
            EvictTextLayersOutside(0, 0);

            // Tiles too
            TileRenderService.Clear();
        }

        public async ValueTask DisposeAsync()
        {
            await _mainCts.CancelAsync();

            await _pagesGenerations.CancelCurrentAsync();
            await _thumbnailsGenerations.CancelCurrentAsync();

            _pagesGenerations.Dispose();
            _thumbnailsGenerations.Dispose();

            try
            {
                await _processingLoopTask;
            }
            catch
            {
                // No op
            }

            _cacheTextLayers.Clear();

            foreach (var kvp in _cachePictures)
            {
                if (_cachePictures.TryRemove(kvp.Key, out var picture))
                {
                    picture.Dispose();
                }
            }

            await TileRenderService.DisposeAsync();

            _mainCts.Dispose();
            
            GC.SuppressFinalize(this);
        }
    }
}
