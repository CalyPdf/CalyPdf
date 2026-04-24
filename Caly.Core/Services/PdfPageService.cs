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
        private enum RenderRequestTypes : byte
        {
            PageSize = 0,
            Picture = 1,
            Thumbnail = 2,
            TextLayer = 3
        }

        private sealed class RenderRequestComparer : IComparer<RenderRequest>
        {
            public static readonly RenderRequestComparer Instance = new();

            public int Compare(RenderRequest? x, RenderRequest? y)
            {
                if (ReferenceEquals(x, y)) return 0;
                if (y is null) return 1;
                if (x is null) return -1;

                if (x.Page.PageNumber.Equals(y.Page.PageNumber))
                {
                    return x.Type.CompareTo(y.Type);
                }

                return x.Page.PageNumber.CompareTo(y.Page.PageNumber);
            }
        }

        private sealed class RenderRequest : IEquatable<RenderRequest>
        {
            public PageViewModel Page { get; }

            public RenderRequestTypes Type { get; }

            public CancellationToken Token { get; }

            public RenderRequest(PageViewModel page, RenderRequestTypes type, CancellationToken token)
            {
                Page = page;
                Type = type;
                Token = token;
            }

            public bool Equals(RenderRequest? other)
            {
                if (other is null) return false;
                if (ReferenceEquals(this, other)) return true;
                return Page.Equals(other.Page) && Type == other.Type;
            }

            public override bool Equals(object? obj)
            {
                return ReferenceEquals(this, obj) || obj is RenderRequest other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(Page, (byte)Type);
            }
        }

        private readonly Task _processingLoopTask;

        private readonly IPdfDocumentService _pdfDocumentService;

        private readonly ChannelWriter<RenderRequest> _requestsWriter;
        private readonly ChannelReader<RenderRequest> _requestsReader;
        private readonly CancellationTokenSource _mainCts = new();
        private readonly CancellationToken _mainToken;
        private CancellationTokenSource _thumbnailsCts = new();
        private CancellationTokenSource _pagesCts = new();

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
                            System.Diagnostics.Debug.WriteLine($"[TBD] CANCELED (dequeued): Page {r.Page.PageNumber}, type {r.Type}.");
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
                    {
                        System.Diagnostics.Debug.WriteLine($"[TBD] CANCELED: Page {r.Page.PageNumber}, type {r.Type}.");
                    }
                    catch (Exception e)
                    {
                        // We just ignore for the moment
                        Debug.WriteExceptionToFile(e);
                    }
                });
            }
            catch (OperationCanceledException) { }
        }

        public PdfPageService(IPdfDocumentService pdfDocumentService)
        {
            TileRenderService = new TileRenderService();
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
                    token.ThrowIfCancellationRequested();
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

            try
            {
                renderRequest.Page.IsPageRendering = true;

                var picture = await GetPicture(renderRequest.Page.PageNumber, renderRequest.Token)
                    .ConfigureAwait(false);

                Size? pageSize = null;
                if (!renderRequest.Page.IsSizeSet())
                {
                    pageSize = await GetPageSize(renderRequest.Page.PageNumber, renderRequest.Token)
                        .ConfigureAwait(false);
                }
                
                if (picture is not null)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        renderRequest.Page.PdfPicture = picture;

                        if (pageSize.HasValue)
                        {
                            renderRequest.Page.SetSize(pageSize.Value);
                        }
                    });
                }
            }
            finally
            {
                renderRequest.Page.IsPageRendering = false;
            }
        }

        private async Task ProcessThumbnailRequest(RenderRequest renderRequest)
        {
            if (renderRequest.Page.Thumbnail is not null)
            {
                return;
            }

            var picture = await GetPicture(renderRequest.Page.PageNumber, renderRequest.Token)
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

            using (picture)
            {
                if (picture is not null)
                {
                    await SetThumbnail(renderRequest.Page, picture.Item, renderRequest.Token)
                        .ConfigureAwait(false);
                }
            }
        }

        private async Task SetThumbnail(PageViewModel vm, SKPicture picture, CancellationToken token)
        {
            Debug.ThrowOnUiThread();

            token.ThrowIfCancellationRequested();
            int tWidth = vm.ThumbnailSize.Width;
            int tHeight = vm.ThumbnailSize.Height;

            var skImageInfo = new SKImageInfo(tWidth, tHeight, SKColorType.Bgra8888, SKAlphaType.Premul);

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
                    PixelFormat.Bgra8888,
                    AlphaFormat.Premul);

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

            var currentCts = CancellationTokenSource.CreateLinkedTokenSource(_mainToken);
            var token = currentCts.Token;
            var oldCts = Interlocked.Exchange(ref _thumbnailsCts, currentCts);

            await oldCts.CancelAsync();
            oldCts.Dispose();

            await Task.Run(async () =>
            {
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

            foreach (var kvp in _cachePictures)
            {
                if (kvp.Key >= realisedStart && kvp.Key < realisedEnd)
                {
                    continue;
                }

                // Page is not realised anymore
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

            foreach (var kvp in _cacheTextLayers)
            {
                if (kvp.Key >= realisedStart && kvp.Key < realisedEnd)
                {
                    continue;
                }

                // Page is not realised anymore
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

            var currentCts = CancellationTokenSource.CreateLinkedTokenSource(_mainToken);
            var token = currentCts.Token;
            var oldCts = Interlocked.Exchange(ref _pagesCts, currentCts);

            await oldCts.CancelAsync();
            oldCts.Dispose();

            await Task.Run(async () =>
            {
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
                            
                            await Dispatcher.UIThread.InvokeAsync(() => page.PdfPicture = clone);
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

        public static Range Empty => new Range(Index.Start, Index.Start);

        public async Task CancelAndClear()
        {
            try
            {
                await _pagesCts.CancelAsync();
            }
            catch (ObjectDisposedException)
            {
                // No op
            }

            try
            {
                await _thumbnailsCts.CancelAsync();
            }
            catch (ObjectDisposedException)
            {
                // No op
            }

            // Picture Cache
            UpdatePictureCache(Empty, null);

            // Text Cache
            UpdateTextLayerCache(Empty, null);
        }

        public async ValueTask DisposeAsync()
        {
            await _mainCts.CancelAsync();

            await _pagesCts.CancelAsync();
            _pagesCts.Dispose();
            
            await _thumbnailsCts.CancelAsync();
            _thumbnailsCts.Dispose();

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
