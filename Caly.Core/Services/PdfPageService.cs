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

        /// <summary>
        /// Single-page view prefetches live in their own generation, not the pages one: a page turn
        /// starts a new pages generation, and must not cancel the prefetch of the page it turns to.
        /// Only <see cref="CancelAndClear"/> and disposal end it; a prefetch whose page has left the
        /// cache window is skipped instead (see <see cref="_pictureKeepWindow"/>).
        /// </summary>
        private readonly CancellationGenerations _prefetchGenerations;
        private readonly Lock _prefetchTokenLock = new();
        private CancellationToken _prefetchToken = new(canceled: true);

        /// <summary>
        /// The [start, end) page window <see cref="_cachePictures"/> was last trimmed to, packed as
        /// start in the high 32 bits and end in the low 32 bits so it is read and written atomically.
        /// </summary>
        private long _pictureKeepWindow;

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

                            case RenderRequestTypes.PrefetchPicture:
                                await ProcessPrefetchPictureRequest(r);
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
            _prefetchGenerations = new CancellationGenerations(_mainToken);
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
        private const int ContinuousPageCacheBuffer = 1;

        /// <summary>
        /// <see cref="ContinuousPageCacheBuffer"/> for single-page view. Only one page is realised there, and
        /// evicting a picture also drops its tiles (<see cref="Rendering.TileRenderService.InvalidatePage"/>),
        /// so this wider window is what lets the tile cache serve a turn back to a recently read page.
        /// </summary>
        private const int SinglePageCacheBuffer = 3;

        private static int GetCacheBuffer(PageDisplayMode mode)
            => mode == PageDisplayMode.SinglePage ? SinglePageCacheBuffer : ContinuousPageCacheBuffer;

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

        private async Task SetThumbnail(PageViewModel vm, SKPicture picture, CancellationToken token)
        {
            Debug.ThrowOnUiThread();

            token.ThrowIfCancellationRequested();
            int tWidth = vm.ThumbnailSize.Width;
            int tHeight = vm.ThumbnailSize.Height;

            // A thumbnail's colour depth loss (Rgb565) is far less noticeable at that size.
            var skImageInfo = new SKImageInfo(tWidth, tHeight, SKColorType.Rgb565, SKAlphaType.Opaque);

            SKMatrix scale = SKMatrix.CreateScale(tWidth / (float)(vm.Size.Width / vm.PpiScale),
                tHeight / (float)(vm.Size.Height / vm.PpiScale));

            token.ThrowIfCancellationRequested();

            using (var surface = SKSurface.Create(skImageInfo))
            {
                var canvas = surface.Canvas;
                canvas.Clear(SKColors.White);
                canvas.DrawPicture(picture, in scale);

#if DEBUG
                using (var skFont = SKTypeface.Default.ToFont(tHeight * (float)_pdfDocumentService.PpiScale / 5f, 1f))
                using (var paint = new SKPaint())
                {
                    paint.Style = SKPaintStyle.Fill;
                    paint.Color = SKColors.Blue.WithAlpha(150);
                    canvas.DrawText(picture.UniqueId.ToString(), tWidth / 4f, tHeight / 2f, skFont, paint);
                }
#endif

                var thumbnail = new WriteableBitmap(
                    new PixelSize(tWidth, tHeight),
                    new Vector(96, 96),
                    PixelFormat.Rgb565,
                    AlphaFormat.Opaque);

                using (var fb = thumbnail.Lock())
                {
                    surface.ReadPixels(skImageInfo, fb.Address, fb.RowBytes, 0, 0);
                }

                await Dispatcher.UIThread.InvokeAsync(() => vm.Thumbnail = thumbnail, DispatcherPriority.Background);
            }
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

                UpdatePictureCache(m.RealisedPages, m.VisiblePages, GetCacheBuffer(m.DisplayMode));
            }, token);
        }

        private void UpdatePictureCache(Range? realisedPages, Range? visiblePages, int cacheBuffer)
        {
            if (!realisedPages.HasValue)
            {
                // TODO - Clear cache?
                return;
            }

            var realised = realisedPages.Value;
            int realisedStart = realised.Start.GetOffset(NumberOfPages);
            int realisedEnd = realised.End.GetOffset(NumberOfPages);

            int keepStart = Math.Max(1, realisedStart - cacheBuffer);
            int keepEnd = Math.Min(NumberOfPages + 1, realisedEnd + cacheBuffer);

            SetPictureKeepWindow(keepStart, keepEnd);
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

        private void UpdateTextLayerCache(Range? realisedPages, Range? visiblePages, int cacheBuffer)
        {
            if (!realisedPages.HasValue)
            {
                // TODO - Clear cache?
                return;
            }

            var realised = realisedPages.Value;
            int realisedStart = realised.Start.GetOffset(NumberOfPages);
            int realisedEnd = realised.End.GetOffset(NumberOfPages);

            int keepStart = Math.Max(1, realisedStart - cacheBuffer);
            int keepEnd = Math.Min(NumberOfPages + 1, realisedEnd + cacheBuffer);

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
                UpdatePictureCache(m.RealisedPages, m.VisiblePages, GetCacheBuffer(m.DisplayMode));

                // Text Cache
                UpdateTextLayerCache(m.RealisedPages, m.VisiblePages, GetCacheBuffer(m.DisplayMode));

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

                if (m.DisplayMode == PageDisplayMode.SinglePage)
                {
                    // Nothing off the current page is ever visible in single-page view, so
                    // without this every page turn would be a cold render.
                    var prefetchToken = await GetPrefetchToken();
                    EnqueuePrefetch(document, visibleStart - 1, prefetchToken);
                    EnqueuePrefetch(document, visibleEnd, prefetchToken);
                }
            }, token);
        }

        /// <summary>
        /// The live prefetch generation's token, starting one if none is live.
        /// </summary>
        private async Task<CancellationToken> GetPrefetchToken()
        {
            lock (_prefetchTokenLock)
            {
                if (!_prefetchToken.IsCancellationRequested)
                {
                    return _prefetchToken;
                }
            }

            var token = await _prefetchGenerations.BeginAsync();
            lock (_prefetchTokenLock)
            {
                _prefetchToken = token;
            }

            return token;
        }

        private void SetPictureKeepWindow(int keepStart, int keepEnd)
        {
            Interlocked.Exchange(ref _pictureKeepWindow, ((long)keepStart << 32) | (uint)keepEnd);
        }

        private bool IsInPictureKeepWindow(int pageNumber)
        {
            long window = Interlocked.Read(ref _pictureKeepWindow);
            int keepStart = (int)(window >> 32);
            int keepEnd = (int)(window & 0xFFFFFFFF);
            return pageNumber >= keepStart && pageNumber < keepEnd;
        }

        private void EnqueuePrefetch(DocumentViewModel document, int pageNumber, CancellationToken token)
        {
            if (pageNumber < 1 || pageNumber > NumberOfPages || _cachePictures.ContainsKey(pageNumber))
            {
                return;
            }

            var page = document.GetPage(pageNumber);
            if (page is null)
            {
                return; // Pages might still be loading
            }

            var request = new RenderRequest(page, RenderRequestTypes.PrefetchPicture, token);
            if (!_requestsWriter.TryWrite(request))
            {
                throw new Exception("Could not write request to channel."); // Should never happen as unbounded channel
            }
        }

        /// <summary>
        /// Renders the page's picture into <see cref="_cachePictures"/> without handing it to the
        /// page: <see cref="RefreshPages"/> picks it up from the cache when the page is turned to.
        /// </summary>
        private async Task ProcessPrefetchPictureRequest(RenderRequest renderRequest)
        {
            if (!IsInPictureKeepWindow(renderRequest.Page.PageNumber))
            {
                return; // Turned or jumped away since this was queued
            }

            using var picture = await GetPicture(renderRequest.Page.PageNumber, renderRequest.Token)
                .ConfigureAwait(false);
        }

        public async Task CancelAndClear()
        {
            await _pagesGenerations.CancelCurrentAsync();
            await _thumbnailsGenerations.CancelCurrentAsync();
            await _prefetchGenerations.CancelCurrentAsync();

            // Caches - full clear, no buffer retained.
            SetPictureKeepWindow(0, 0);
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
            await _prefetchGenerations.CancelCurrentAsync();

            _pagesGenerations.Dispose();
            _thumbnailsGenerations.Dispose();
            _prefetchGenerations.Dispose();

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
