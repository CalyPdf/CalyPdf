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

using Avalonia.Platform.Storage;
using Caly.Core.Models;
using Caly.Core.Utilities;
using Caly.Core.ViewModels;
using Caly.Pdf.Models;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UglyToad.PdfPig.Rendering.Skia;

namespace Caly.Core.Services.Interfaces;

public enum DocumentOpeningState : byte
{
    /// <summary>
    /// Failed due to generic error (default state).
    /// </summary>
    Error = 0,
    
    /// <summary>
    /// Successfully opened.
    /// </summary>
    Success = 1,

    /// <summary>
    /// Failed because the file was not found.
    /// </summary>
    FileNotFound = 2,

    /// <summary>
    /// Failed because opening was canceled.
    /// </summary>
    Canceled = 3,

    /// <summary>
    /// Failed because of password.
    /// </summary>
    Password = 4,
}

public interface IPdfDocumentService : IAsyncDisposable
{
    double PpiScale { get; }

    bool IsActive { get; internal set; }

    int NumberOfPages { get; }

    string? FileName { get; }

    long? FileSize { get; }

    string? LocalPath { get; }

    bool IsPasswordProtected { get; }
    
    /// <summary>
    /// Open the pdf document.
    /// <para>Can only be called once per instance.</para>
    /// </summary>
    Task<DocumentOpeningState> OpenDocument(IStorageFile? storageFile, string? password, CancellationToken token);

    Task<DocumentPropertiesViewModel?> GetDocumentPropertiesAsync(CancellationToken token);

    Task<IReadOnlyList<PdfBookmarkNode>?> GetPdfBookmark(CancellationToken token);

    Task<IReadOnlyList<PdfEmbeddedFileViewModel>?> GetEmbeddedFileAsync(CancellationToken token);

    Task<PdfPageSize?> GetPageSizeAsync(int pageNumber, CancellationToken token);

    Task<PdfTextLayer?> GetPageTextLayerAsync(int pageNumber, CancellationToken token);

    Task<IRef<SKPicture>?> GetRenderPageAsync(int pageNumber, CancellationToken token);
}
