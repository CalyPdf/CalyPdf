using Avalonia.Platform.Storage;
using Caly.Core.Models;
using Caly.Core.Services.Interfaces;
using Caly.Core.Utilities;
using Caly.Core.ViewModels;
using Caly.Pdf.Models;
using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Rendering.Skia;
using UglyToad.PdfPig.Writer;

namespace Caly.Printing.Tests;

/// <summary>
/// The document the spooler tests print: a real A4 PDF built in memory, one framed page per
/// requested orientation, exposing only what the print services use (file name, page sizes,
/// page pictures).
/// <para>
/// It is a real PDF rather than a hand-written fake because <see cref="PdfPageSize"/> can only
/// be produced by PdfPig's <see cref="PageSizeFactory"/>.
/// </para>
/// </summary>
internal sealed class PrintTestDocument : IPdfDocumentService
{
    private readonly PdfDocument _document;

    private PrintTestDocument(PdfDocument document, string fileName)
    {
        _document = document;
        FileName = fileName;
    }

    /// <param name="fileName">Reported as <see cref="FileName"/>; the print services use it as the job name.</param>
    /// <param name="portraitPages">One entry per page: <c>true</c> for portrait, <c>false</c> for landscape.</param>
    public static PrintTestDocument Create(string fileName, params bool[] portraitPages)
    {
        var builder = new PdfDocumentBuilder();
        foreach (bool portrait in portraitPages)
        {
            PdfPageBuilder page = builder.AddPage(PageSize.A4, portrait);
            page.SetStrokeColor(0, 0, 0);
            page.DrawRectangle(new PdfPoint(36, 36), page.PageSize.Width - 72, page.PageSize.Height - 72, 4);
        }

        var document = PdfDocument.Open(builder.Build(), new ParsingOptions
        {
            SkipMissingFonts = true,
            FilterProvider = SkiaRenderingFilterProvider.Instance
        });

        document.AddPageFactory<PdfPageSize, PageSizeFactory>();
        document.AddPageFactory<SKPicture, SkiaPageFactory>();

        return new PrintTestDocument(document, fileName);
    }

    /// <summary>Every page, unrotated, in order.</summary>
    public PrintPageInfo[] AllPages => Enumerable.Range(1, NumberOfPages).Select(n => new PrintPageInfo(n)).ToArray();

    public double PpiScale => 1.0;
    public bool IsActive { get; set; }
    public int NumberOfPages => _document.NumberOfPages;
    public string? FileName { get; }

    public Task<PdfPageSize?> GetPageSizeAsync(int pageNumber, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult<PdfPageSize?>(_document.GetPage<PdfPageSize>(pageNumber));
    }

    public Task<IRef<SKPicture>?> GetRenderPageAsync(int pageNumber, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult<IRef<SKPicture>?>(RefCountable.Create(_document.GetPage<SKPicture>(pageNumber)));
    }

    public ValueTask DisposeAsync()
    {
        _document.Dispose();
        return ValueTask.CompletedTask;
    }

    // Not used by the print services.
    public string? Title => throw new NotSupportedException();
    public long? FileSize => throw new NotSupportedException();
    public string? LocalPath => throw new NotSupportedException();
    public bool IsPasswordProtected => throw new NotSupportedException();
    public PdfPreferences? Preferences => throw new NotSupportedException();
    public Func<CancellationToken, Task<string?>>? PasswordPrompt { get; set; }

    public Task<DocumentOpeningState> OpenDocument(IStorageFile? storageFile, string? password, CancellationToken token)
        => throw new NotSupportedException();

    public Task<DocumentPropertiesViewModel?> GetDocumentPropertiesAsync(CancellationToken token)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<PdfBookmarkNode>?> GetPdfBookmark(CancellationToken token)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<PdfEmbeddedFileViewModel>?> GetEmbeddedFileAsync(CancellationToken token)
        => throw new NotSupportedException();

    public Task<PageTextLayerContent?> GetPageTextLayerAsync(int pageNumber, CancellationToken token)
        => throw new NotSupportedException();

    public Task<Caly.Pdf.Artwork.Artwork3DContent?> GetArtwork3DContentAsync(PdfArtwork3D artwork, CancellationToken token)
        => throw new NotSupportedException();
}

/// <summary>
/// Records progress reports synchronously. <see cref="Progress{T}"/> posts them to the thread
/// pool, so a test could assert before the last report had arrived.
/// </summary>
internal sealed class RecordingProgress(Action<int>? onReport = null) : IProgress<int>
{
    private readonly List<int> _values = [];

    public IReadOnlyList<int> Values
    {
        get
        {
            lock (_values)
            {
                return _values.ToArray();
            }
        }
    }

    public void Report(int value)
    {
        lock (_values)
        {
            _values.Add(value);
        }

        onReport?.Invoke(value);
    }
}
