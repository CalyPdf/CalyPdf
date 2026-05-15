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
using UglyToad.PdfPig.Tokens;

namespace Caly.Core.Services;

/// <summary>
/// One instance per document.
/// </summary>
internal sealed partial class PdfPigDocumentService : IPdfDocumentService
{
    private const string PdfVersionFormat = "0.0";
    private const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss zzz";

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

    public async Task<int> OpenDocument(IStorageFile? storageFile, string? password, CancellationToken token)
    {
        Debug.ThrowOnUiThread();

        // TODO - Ensure method is called only once (one instance per document)

        return await GuardDispose(async ct =>
        {
            try
            {
                if (storageFile is null)
                {
                    return 0; // no pdf loaded
                }

                if (!storageFile.Path.LocalPath.IsPdf() && !CalyExtensions.IsMobilePlatform())
                {
                    // TODO - Need to handle Mobile
                    throw new ArgumentOutOfRangeException($"The loaded file '{Path.GetFileName(storageFile.Path.LocalPath)}' is not a pdf document.");
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
                        Logger = CalyPdfPigLogger.Instance
                    };

                    if (!string.IsNullOrEmpty(password))
                    {
                        pdfParsingOptions.Password = password;
                    }

                    _document = PdfDocument.Open(_fileStream, pdfParsingOptions);

                    // We store the PPI as an indirect object so that it can be accessed in the TextLayerFactory.
                    // This is very hacky but PdfPig does not provide a better way to pass such information
                    // to the PageFactory for the moment.
                    // TODO - to remove.
                    _document.Advanced.ReplaceIndirectObject(CalyPdfHelper.FakePpiReference, new NumericToken(PpiScale));

                    _document.AddPageFactory<PdfPageSize, PageSizeFactory>();
                    _document.AddPageFactory<SKPicture, SkiaPageFactory>();
                    _document.AddPageFactory<PageTextLayerContent, TextLayerFactory>();

                    NumberOfPages = _document.NumberOfPages;

                    return NumberOfPages;
                }, ct);
            }
            catch (PdfDocumentEncryptedException)
            {
                IsPasswordProtected = true;

                if (!string.IsNullOrEmpty(password))
                {
                    // Only stay at first level, do not recurse: If password is NOT null, this is recursion
                    return 0;
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

                    var pageCount = await OpenDocument(_storageFile, pw, ct).ConfigureAwait(false);
                    if (pageCount > 0)
                    {
                        // Password OK and document opened
                        return pageCount;
                    }
                }

                return 0;
            }
            catch (OperationCanceledException)
            {
                return 0;
            }
            finally
            {
                // Only release on first pass
                if (string.IsNullOrEmpty(password))
                {
                    // The _semaphore starts with initial count set to 0 and maxCount to 1.
                    // By releasing here we allow _semaphore.Wait() in other methods.
                    _semaphore.Release();
                }
            }
        }, token);
    }
    
    public async Task<PdfPageSize?> GetPageSizeAsync(int pageNumber, CancellationToken token)
    {
        Debug.ThrowOnUiThread();

        return await GuardDispose(async ct =>
        {
            return await ExecuteWithLockAsync(
                _ => _document?.GetPage<PdfPageSize>(pageNumber),
                ct);
        }, token);
    }

    public async Task<PdfTextLayer?> GetPageTextLayerAsync(int pageNumber, CancellationToken token)
    {
        Debug.ThrowOnUiThread();

        return await GuardDispose(async ct =>
        {
            var pageTextLayer = await ExecuteWithLockAsync(
                    _ => _document?.GetPage<PageTextLayerContent>(pageNumber),
                    ct)
                .ConfigureAwait(false);

            if (pageTextLayer is null)
            {
                return null;
            }

            return PdfTextLayerHelper.GetTextLayer(pageTextLayer, ct);
        }, token);
    }

    public async Task<IReadOnlyList<PdfEmbeddedFileViewModel>?> GetEmbeddedFileAsync(CancellationToken token)
    {
        Debug.ThrowOnUiThread();

        return await GuardDispose(async ct =>
        {
            var files = await ExecuteWithLockAsync(
                _ => _document?.Advanced.TryGetEmbeddedFiles(out var files) == true ? files : null,
                ct);

            if (files is null || files.Count == 0 || ct.IsCancellationRequested)
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

        return GuardDispose(async ct =>
        {
            await Task.Yield();

            var info = _document?.Information;

            var others =
                _document?.Information.DocumentInformationDictionary?.Data?
                    .Where(x => x.Value is not null)
                    .ToDictionary(x => x.Key,
                        x => x.Value.ToString()!);

            if (info is null || others is null || ct.IsCancellationRequested)
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
            
            return new DocumentPropertiesViewModel()
            {
                FileName = FileName,
                FileSize = Helpers.FormatSizeBytes(FileSize.Value),
                PageCount = NumberOfPages,
                PdfVersion = _document?.Version.ToString(PdfVersionFormat) ?? string.Empty,
                Title = info.Title,
                Author = info.Author,
                CreationDate = FormatPdfDate(info.CreationDate),
                Creator = info.Creator,
                Keywords = info.Keywords,
                ModifiedDate = FormatPdfDate(info.ModifiedDate),
                Producer = info.Producer,
                Subject = info.Subject,
                Others = others
            };
        }, token);
    }

    public string? GetLogFileName()
    {
        const int length = 15;

        string? v = FileName;
        if (string.IsNullOrEmpty(v))
        {
            return v;
        }

        if (v.Length == length)
        {
            return v;
        }

        if (v.Length > length)
        {
            return v[..length];
        }

        return v.PadRight(length);
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
        return await GuardDispose(async ct =>
        {
            if (_document is null)
            {
                return [];
            }
            
            Bookmarks? bookmarks = await ExecuteWithLockAsync(_ =>
            {
                if (_document!.TryGetBookmarks(out var b, true))
                {
                    return b;
                }

                return null;
            }, ct);

            if (bookmarks is null || bookmarks.Roots.Count == 0 || ct.IsCancellationRequested)
            {
                return null;
            }

            var bookmarksItems = new List<PdfBookmarkNode>();
            foreach (BookmarkNode node in bookmarks.Roots)
            {
                var n = BuildPdfBookmarkNode(node, ct);
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
            if (IsDisposed())
            {
                System.Diagnostics.Debug.WriteLine($"[WARN] Trying to dispose but already disposed for {FileName}.");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[INFO] Disposing document async for {FileName}.");

            Interlocked.Increment(ref _isDisposed); // Flag as disposed

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
