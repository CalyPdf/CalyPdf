using System.ComponentModel;
using System.IO;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Platform.Storage;
using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Core.Services.Interfaces;
using Caly.Core.Utilities;
using Caly.Core.ViewModels;
using Caly.Pdf.Models;
using Caly.Tests.Fakes;
using SkiaSharp;
using UglyToad.PdfPig.Rendering.Skia;
using static Caly.Tests.Fakes.DocumentTestHarness;

namespace Caly.Tests;

/// <summary>
/// The document's <see cref="DocumentViewModel.ZoomMode"/>: the menu and shortcuts pick a mode, a
/// hand-picked zoom leaves it, and a new document opens in the mode chosen in Settings.
/// </summary>
public class DocumentViewModelZoomModeTests
{
    private static async Task<(DocumentViewModel Document, PdfPageService PageService)> NewDocument()
    {
        var pdfService = new RenderingPdfDocumentService();
        var pageService = new PdfPageService(pdfService);
        return (NewLoadedDocument(pdfService, pageService), pageService);
    }

    [AvaloniaFact]
    public async Task SetZoomMode_SetsTheModeAndItsToggle()
    {
        var (document, pageService) = await NewDocument();
        await using var _ = pageService;

        document.SetZoomModeCommand.Execute(ZoomMode.FitWidth);

        Assert.Equal(ZoomMode.FitWidth, document.ZoomMode);
        Assert.True(document.IsFitWidthMode);
        Assert.False(document.IsFitPageMode);
        Assert.False(document.IsFitContentMode);
        Assert.False(document.IsFitContentWidthMode);
    }

    [AvaloniaFact]
    public async Task SetZoomMode_TheActiveMode_ReappliesIt()
    {
        var (document, pageService) = await NewDocument();
        await using var _ = pageService;
        var modes = new List<ZoomMode>();
        ((INotifyPropertyChanged)document).PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DocumentViewModel.ZoomMode))
            {
                modes.Add(document.ZoomMode);
            }
        };

        document.SetZoomModeCommand.Execute(ZoomMode.FitPage);
        document.SetZoomModeCommand.Execute(ZoomMode.FitPage);

        // The view fits on every change into a fit mode: going through Manual is what makes the
        // second pick snap the page back into view.
        Assert.Equal([ZoomMode.FitPage, ZoomMode.Manual, ZoomMode.FitPage], modes);
    }

    [AvaloniaFact]
    public async Task ZoomIn_LeavesTheFitMode()
    {
        var (document, pageService) = await NewDocument();
        await using var _ = pageService;
        document.ZoomMode = ZoomMode.FitPage;

        document.ZoomInCommand.Execute(null);

        Assert.Equal(ZoomMode.Manual, document.ZoomMode);
        Assert.Equal(1.25, document.ZoomLevel);
    }

    [AvaloniaFact]
    public async Task ZoomOut_LeavesTheFitMode()
    {
        var (document, pageService) = await NewDocument();
        await using var _ = pageService;
        document.ZoomMode = ZoomMode.FitContent;

        document.ZoomOutCommand.Execute(null);

        Assert.Equal(ZoomMode.Manual, document.ZoomMode);
        Assert.Equal(0.75, document.ZoomLevel);
    }

    [AvaloniaFact]
    public async Task ActualSize_ResetsTheZoomAndLeavesTheFitMode()
    {
        var (document, pageService) = await NewDocument();
        await using var _ = pageService;
        document.ZoomLevel = 3.83;
        document.ZoomMode = ZoomMode.FitWidth;

        document.ActualSizeCommand.Execute(null);

        Assert.Equal(1, document.ZoomLevel);
        Assert.Equal(ZoomMode.Manual, document.ZoomMode);
    }

    [AvaloniaFact]
    public async Task LoadDocument_AppliesTheDefaultZoomMode()
    {
        // The fake service never reads the file: it only has to exist for the storage provider.
        string path = Path.Combine(Path.GetTempPath(), $"caly-zoom-{Guid.NewGuid():N}.pdf");
        await File.WriteAllBytesAsync(path, "%PDF"u8.ToArray());
        var window = new Window();
        window.Show();

        try
        {
            using var file = await window.StorageProvider.TryGetFileFromPathAsync(path);
            Assert.NotNull(file);

            var pdfService = new OpeningPdfDocumentService(pageCount: 3);
            await using var pageService = new PdfPageService(pdfService);
            var settings = new FixedSettingsService(new CalySettings { DefaultZoomMode = ZoomMode.FitWidth });
            var document = new DocumentViewModel(pdfService, pageService, new NoopTextSearchService(), settings);

            var state = await document.LoadDocument(file, null, CancellationToken.None);
            await document.LoadPagesTask;

            Assert.Equal(DocumentOpeningState.Success, state);
            Assert.Equal(ZoomMode.FitWidth, document.ZoomMode);
        }
        finally
        {
            window.Close();
            File.Delete(path);
        }
    }

    private sealed class FixedSettingsService(CalySettings settings) : ISettingsService
    {
        public CalySettings GetSettings() => settings;
        public ValueTask<CalySettings> GetSettingsAsync() => ValueTask.FromResult(settings);
        public void SetProperty(CalySettings.CalySettingsProperty property, object value) => throw new NotImplementedException();
        public void Load() => throw new NotImplementedException();
        public Task LoadAsync() => throw new NotImplementedException();
        public void Save() => throw new NotImplementedException();
        public Task SaveAsync() => throw new NotImplementedException();
    }

    /// <summary>
    /// Opens successfully with <c>pageCount</c> pages that have no size, picture or text yet.
    /// </summary>
    private sealed class OpeningPdfDocumentService(int pageCount) : IPdfDocumentService
    {
        public double PpiScale => 1.0;
        public bool IsActive { get; set; }
        public int NumberOfPages { get; private set; }
        public string? FileName => "opening.pdf";
        public string? Title => null;
        public long? FileSize => null;
        public string? LocalPath { get; private set; }
        public bool IsPasswordProtected => false;
        public PdfPreferences? Preferences { get; } = new();
        public Func<CancellationToken, Task<string?>>? PasswordPrompt { get; set; }

        public Task<DocumentOpeningState> OpenDocument(IStorageFile? storageFile, string? password, CancellationToken token)
        {
            LocalPath = storageFile?.Path.LocalPath;
            NumberOfPages = pageCount;
            return Task.FromResult(DocumentOpeningState.Success);
        }

        public Task<PdfPageSize?> GetPageSizeAsync(int pageNumber, CancellationToken token)
            => Task.FromResult<PdfPageSize?>(null);

        public Task<PdfTextLayer?> GetPageTextLayerAsync(int pageNumber, CancellationToken token)
            => Task.FromResult<PdfTextLayer?>(null);

        public Task<IRef<SKPicture>?> GetRenderPageAsync(int pageNumber, CancellationToken token)
            => Task.FromResult<IRef<SKPicture>?>(null);

        public Task<DocumentPropertiesViewModel?> GetDocumentPropertiesAsync(CancellationToken token)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<PdfBookmarkNode>?> GetPdfBookmark(CancellationToken token)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<PdfEmbeddedFileViewModel>?> GetEmbeddedFileAsync(CancellationToken token)
            => throw new NotImplementedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
