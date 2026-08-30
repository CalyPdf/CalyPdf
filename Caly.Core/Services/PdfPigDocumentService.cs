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

using Avalonia.Controls.Notifications;
using Avalonia.Platform.Storage;
using Caly.Core.Models;
using Caly.Core.Services.Interfaces;
using Caly.Core.Utilities;
using Caly.Core.ViewModels;
using Caly.Pdf;
using Caly.Pdf.Models;
using Caly.Pdf.PageFactories;
using CommunityToolkit.Mvvm.Messaging;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Exceptions;
using UglyToad.PdfPig.Outline;
using UglyToad.PdfPig.Rendering.Skia;
using UglyToad.PdfPig.Rendering.Skia.Icc.Unicolour;
using UglyToad.PdfPig.Tokens;

namespace Caly.Core.Services;

/// <summary>
/// One instance per document.
/// </summary>
internal sealed partial class PdfPigDocumentService : IPdfDocumentService
{
    private const string PdfVersionFormat = "0.0";
    private const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss zzz";

    private readonly ISettingsService _settingsService;

    private IStorageFile? _storageFile;
    private Stream? _fileStream;
    private PdfDocument? _document;
    private Uri? _filePath;

    public string? LocalPath => _filePath?.LocalPath;

    public string? FileName => Path.GetFileNameWithoutExtension(LocalPath);

    public long? FileSize => _fileStream?.Length;

    public int NumberOfPages { get; private set; }

    public bool IsPasswordProtected { get; private set; } = false;

    private long _isActive = 0;
    public bool IsActive
    {
        // https://makolyte.com/csharp-thread-safe-primitive-properties-using-lock-vs-interlocked/
        get => Interlocked.Read(ref _isActive) == 1;
        set => Interlocked.Exchange(ref _isActive, Convert.ToInt64(value));
    }

    /// <summary>
    /// Gets the Pixel Per Inch (PPI) scaling factor used to convert measurements from PDF points (72 PPI is the default) to application pixels.
    /// </summary>
    /// <remarks>
    /// The application PPI is currently set to 144. We should make that configurable.
    /// </remarks>
    public double PpiScale => 144.0 / 72.0; // 72 should be document dependant, i.e. use PdfPig's UserSpaceUnit.

    public PdfPigDocumentService(ISettingsService settingsService)
    {
        _mainToken = _mainCts.Token;
        _settingsService = settingsService;
    }
    
    private Task<DocumentOpeningState>? _openDocumentTask;
    private readonly Lock _openDocumentLock = new();

    public Task<DocumentOpeningState> OpenDocument(IStorageFile? storageFile, string? password, CancellationToken token)
    {
        // Ensure method is called only once (one instance per document)
        lock (_openDocumentLock)
        {
            if (_openDocumentTask is not null)
            {
                throw new InvalidOperationException("Attempt to open a pdf document more than once with the same IPdfDocumentService.");
            }

            _openDocumentTask = OpenDocumentInternal(storageFile, password, token);
            return _openDocumentTask;
        }
    }

    private async Task<DocumentOpeningState> OpenDocumentInternal(IStorageFile? storageFile, string? password, CancellationToken token)
    {
        Debug.ThrowOnUiThread();

        return await GuardDispose(async ct =>
        {
            try
            {
                if (storageFile is null)
                {
                    return DocumentOpeningState.FileNotFound;
                }

                if (!storageFile.Path.LocalPath.IsPdf() && !Globals.IsMobilePlatform())
                {
                    // TODO - Need to handle Mobile
                    throw new ArgumentOutOfRangeException(
                        $"The loaded file '{Path.GetFileName(storageFile.Path.LocalPath)}' is not a pdf document.");
                }

                _storageFile = storageFile;
                _filePath = _storageFile.Path;
                System.Diagnostics.Debug.WriteLine($"[INFO] Opening {FileName}...");

                _fileStream = await _storageFile.OpenReadAsync().ConfigureAwait(false);

                if (!_fileStream.CanSeek)
                {
                    var ms = new MemoryStream((int)_fileStream.Length);
                    await _fileStream.CopyToAsync(ms, ct).ConfigureAwait(false);
                    ms.Position = 0;
                    await _fileStream.DisposeAsync().ConfigureAwait(false);
                    _fileStream = ms;
                }

                return await Task.Run(() =>
                {
                    var pdfParsingOptions = new ParsingOptions()
                    {
                        SkipMissingFonts = true,
                        FilterProvider = SkiaRenderingFilterProvider.Instance,
                        IccProfileService = UnicolourIccProfileService.Instance
                    };

                    if (_settingsService.GetSettings().ShowPdfLogs)
                    {
                        pdfParsingOptions.Logger = CalyPdfPigLogger.Instance;
                    }

                    if (!string.IsNullOrEmpty(password))
                    {
                        pdfParsingOptions.Password = password;
                    }

                    _document = PdfDocument.Open(_fileStream, pdfParsingOptions);

                    token.ThrowIfCancellationRequested();

                    // We store the PPI as an indirect object so that it can be accessed in the TextLayerFactory.
                    // This is very hacky but PdfPig does not provide a better way to pass such information
                    // to the PageFactory for the moment.
                    // TODO - to remove.
                    _document.Advanced.ReplaceIndirectObject(CalyPdfHelper.FakePpiReference,
                        new NumericToken(PpiScale));

                    _document.AddPageFactory<PdfPageSize, PageSizeFactory>();
                    _document.AddPageFactory<SKPicture, SkiaPageFactory>();
                    _document.AddPageFactory<PageTextLayerContent, TextLayerFactory>();

                    NumberOfPages = _document.NumberOfPages;

                    return DocumentOpeningState.Success;
                }, ct);
            }
            catch (PdfDocumentEncryptedException)
            {
                IsPasswordProtected = true;

                if (!string.IsNullOrEmpty(password))
                {
                    // Only stay at first level, do not recurse: If password is NOT null, this is recursion
                    return DocumentOpeningState.Password;
                }

                bool shouldContinue = true;
                while (shouldContinue)
                {
                    string? pw = await App.Messenger.Send(new ShowPdfPasswordDialogRequestMessage());
                    Debug.ThrowOnUiThread();

                    shouldContinue = !string.IsNullOrEmpty(pw);
                    if (!shouldContinue)
                    {
                        continue;
                    }

                    var state = await OpenDocumentInternal(_storageFile, pw, ct).ConfigureAwait(false);
                    if (state == DocumentOpeningState.Success)
                    {
                        // Password OK and document opened
                        return state;
                    }
                }

                return DocumentOpeningState.Password;
            }
            catch (OperationCanceledException)
            {
                return DocumentOpeningState.Canceled;
            }
            finally
            {
                // Only release on first pass
                if (string.IsNullOrEmpty(password) && !IsDisposed())
                {
                    // The _semaphore starts with initial count set to 0 and maxCount to 1.
                    // By releasing here we allow _semaphore.Wait() in other methods.
                    try
                    {
                        _semaphore.Release();
                    }
                    catch (ObjectDisposedException)
                    { }
                }
            }
        }, () => DocumentOpeningState.Error, () => DocumentOpeningState.Canceled, token);
    }

    /// <summary>
    /// Wait for document to finish opening, or being cancelled.
    /// </summary>
    private async Task WaitForDocumentToOpen(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        if (_openDocumentTask is null)
        {
            // This should not happen, as OpenDocument should be called before any operation that requires it.
            throw new InvalidOperationException("Document has not been opened yet.");
        }

        var state = await _openDocumentTask.WaitAsync(token).ConfigureAwait(false);
        if (state != DocumentOpeningState.Success)
        {
            // We consider the operation was cancelled because we don't want to throw.
            throw new OperationCanceledException("WaitForDocumentToOpen");
        }
    }
    
    public async Task<PdfPageSize?> GetPageSizeAsync(int pageNumber, CancellationToken token)
    {
        Debug.ThrowOnUiThread();

        return await GuardDispose<PdfPageSize?>(async guardCt =>
        {
            await WaitForDocumentToOpen(guardCt);
            var document = _document;
            if (document is null)
            {
                return null;
            }

            return await ExecuteWithLockAsync(
                _ => document.GetPage<PdfPageSize>(pageNumber),
                guardCt);
        }, token);
    }

    public async Task<PdfTextLayer?> GetPageTextLayerAsync(int pageNumber, CancellationToken token)
    {
        Debug.ThrowOnUiThread();

        return await GuardDispose(async guardCt =>
        {
            await WaitForDocumentToOpen(guardCt);
            var document = _document;
            if (document is null)
            {
                return null;
            }

            var pageTextLayer = await ExecuteWithLockAsync(lockCt =>
                    {
                        try
                        {
                            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(lockCt);
                            linkedCts.CancelAfter(PageTimeOut);
                            return document.GetPageTextLayerContent(pageNumber, linkedCts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            if (!lockCt.IsCancellationRequested)
                            {
                                App.Messenger.Send(new ShowNotificationMessage(NotificationType.Error,
                                    $"Error in page {pageNumber}",
                                    $"Could not get text after {PageTimeOut.TotalSeconds} seconds."));
                            }
                            
                            return null;
                        }
                    }, guardCt)
                    .ConfigureAwait(false);

            if (pageTextLayer is null)
            {
                return null;
            }

            return PdfTextLayerHelper.GetTextLayer(pageTextLayer, guardCt);
        }, token);
    }

    public async Task<IReadOnlyList<PdfEmbeddedFileViewModel>?> GetEmbeddedFileAsync(CancellationToken token)
    {
        Debug.ThrowOnUiThread();

        return await GuardDispose(async guardCt =>
        {
            await WaitForDocumentToOpen(guardCt);
            var document = _document;
            if (document is null)
            {
                return null;
            }

            var files = await ExecuteWithLockAsync(
                _ => document.Advanced.TryGetEmbeddedFiles(out var files) ? files : null,
                guardCt);

            if (files is null || files.Count == 0 || guardCt.IsCancellationRequested)
            {
                return null;
            }

            var result = new PdfEmbeddedFileViewModel[files.Count];
            for (var i = 0; i < files.Count; i++)
            {
                var f = files[i];
                result[i] = new PdfEmbeddedFileViewModel(f.Name, f.Memory);
            }

            return result;
        }, token);
    }

    public Task<DocumentPropertiesViewModel?> GetDocumentPropertiesAsync(CancellationToken token)
    {
        Debug.ThrowOnUiThread();

        return GuardDispose(async guardCt =>
        {
            await WaitForDocumentToOpen(guardCt);
            var document = _document;
            if (document is null)
            {
                return null;
            }

            if (string.IsNullOrEmpty(FileName))
            {
                throw new InvalidOperationException("FileName should not be null or empty at this stage.");
            }

            if (!FileSize.HasValue)
            {
                throw new InvalidOperationException("FileSize should have a value at this stage.");
            }

            var info = document.Information;

            var others =
                document.Information.DocumentInformationDictionary?.Data?
                    .Where(x => x.Value is not null)
                    .ToDictionary(x => x.Key,
                        x => x.Value.ToString()!);

            if (guardCt.IsCancellationRequested)
            {
                return null;
            }
            
            return new DocumentPropertiesViewModel()
            {
                FileName = FileName,
                FileSize = Helpers.FormatSizeBytes(FileSize.Value),
                PageCount = NumberOfPages,
                PdfVersion = document.Version.ToString(PdfVersionFormat),
                Title = info?.Title,
                Author = info?.Author,
                CreationDate = FormatPdfDate(info?.CreationDate),
                Creator = info?.Creator,
                Keywords = info?.Keywords,
                ModifiedDate = FormatPdfDate(info?.ModifiedDate),
                Producer = info?.Producer,
                Subject = info?.Subject,
                Others = others ?? []
            };
        }, token);
    }

    private static string? FormatPdfDate(string? rawDate)
    {
        if (string.IsNullOrEmpty(rawDate))
        {
            return rawDate;
        }

        if (rawDate.StartsWith("D:"))
        {
            rawDate = rawDate[2..];
        }

        if (UglyToad.PdfPig.Util.DateFormatHelper.TryParseDateTimeOffset(rawDate, out DateTimeOffset offset))
        {
            return offset.ToString(DateTimeFormat);
        }

        return rawDate;
    }

    public async Task<IReadOnlyList<PdfBookmarkNode>?> GetPdfBookmark(CancellationToken token)
    {
        Debug.ThrowOnUiThread();
        return await GuardDispose(async guardCt =>
        {
            await WaitForDocumentToOpen(guardCt);
            var document = _document;
            if (document is null)
            {
                return null;
            }

            Bookmarks? bookmarks = await ExecuteWithLockAsync(_ =>
            {
                if (document.TryGetBookmarks(out var b, true))
                {
                    return b;
                }

                return null;
            }, guardCt);

            if (bookmarks is null || bookmarks.Roots.Count == 0 || guardCt.IsCancellationRequested)
            {
                return null;
            }

            var bookmarksItems = new List<PdfBookmarkNode>();
            foreach (BookmarkNode node in bookmarks.Roots)
            {
                var n = BuildPdfBookmarkNode(node, guardCt);
                if (n is not null)
                {
                    bookmarksItems.Add(n);
                }
            }

            return bookmarksItems;
        }, token);
    }

    private PdfBookmarkNode BuildPdfBookmarkNode(BookmarkNode node, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        int? pageNumber = null;
        double? offsetY = null;
        if (node is DocumentBookmarkNode bookmarkNode)
        {
            pageNumber = bookmarkNode.PageNumber;
            offsetY = bookmarkNode.Destination?.Coordinates?.Top * PpiScale;
        }

        if (node.IsLeaf)
        {
            return new PdfBookmarkNode(node.Title, pageNumber, offsetY, null);
        }

        var children = new List<PdfBookmarkNode>();
        foreach (var child in node.Children)
        {
            var n = BuildPdfBookmarkNode(child, token);
            System.Diagnostics.Debug.Assert(n is not null);
            children.Add(n);
        }

        return new PdfBookmarkNode(node.Title, pageNumber, offsetY, children.Count == 0 ? null : children);
    }

    public async ValueTask DisposeAsync()
    {
        Debug.ThrowOnUiThread();

        try
        {
            if (Interlocked.CompareExchange(ref _isDisposed, 1, 0) != 0)
            {
                System.Diagnostics.Debug.WriteLine($"[WARN] Trying to dispose but already disposed for {FileName}.");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[INFO] Disposing document async for {FileName}.");
            
            await _mainCts.CancelAsync();

            // Wait for in-flight operations (with timeout)
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            {
                while (_activeOperations > 0 && !cts.Token.IsCancellationRequested)
                {
                    System.Diagnostics.Debug.WriteLine($"DisposeAsync: '{FileName}' waiting for {_activeOperations} active operations to finish.");
                    await Task.Delay(50, CancellationToken.None);
                }
            }

            _semaphore.Dispose();

            if (_fileStream is not null)
            {
                await _fileStream.DisposeAsync();
                _fileStream = null;
            }

            _storageFile?.Dispose();
            _storageFile = null;

            if (_document is not null)
            {
                _document.Dispose();
                _document = null;
            }

            _mainCts.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteExceptionToFile(ex);
            System.Diagnostics.Debug.WriteLine($"[INFO] ERROR DisposeAsync for {FileName}: {ex.Message}");
        }
    }
}
