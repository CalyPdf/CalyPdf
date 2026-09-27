using Avalonia.Platform.Storage;
using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Core.Services.Interfaces;
using Caly.Core.Utilities;
using Caly.Core.ViewModels;
using Caly.Pdf.Models;
using SkiaSharp;

namespace Caly.Tests;

/// <summary>
/// <see cref="CalySettings.TileCacheSizeMB"/> sets the budget of each document's tile cache.
/// </summary>
public class TileCacheSizeSettingTests
{
    /// <summary>
    /// <see cref="PdfPageService"/> only stores its document service when constructed, so the
    /// members throw.
    /// </summary>
    private sealed class UnopenedPdfDocumentService : IPdfDocumentService
    {
        public int NumberOfPages => 0;
        public string? FileName => "unopened.pdf";
        public bool IsActive { get; set; }

        public double PpiScale => throw new NotImplementedException();
        public long? FileSize => throw new NotImplementedException();
        public string? LocalPath => throw new NotImplementedException();
        public bool IsPasswordProtected => throw new NotImplementedException();
        public Func<CancellationToken, Task<string?>>? PasswordPrompt { get; set; }

        public string? Title => throw new NotImplementedException();

        public PdfPreferences? Preferences => throw new NotImplementedException();

        public Task<DocumentOpeningState> OpenDocument(IStorageFile? storageFile, string? password, CancellationToken token)
            => throw new NotImplementedException();

        public Task<DocumentPropertiesViewModel?> GetDocumentPropertiesAsync(CancellationToken token)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<PdfBookmarkNode>?> GetPdfBookmark(CancellationToken token)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<PdfEmbeddedFileViewModel>?> GetEmbeddedFileAsync(CancellationToken token)
            => throw new NotImplementedException();

        public Task<UglyToad.PdfPig.Rendering.Skia.PdfPageSize?> GetPageSizeAsync(int pageNumber, CancellationToken token)
            => throw new NotImplementedException();

        public Task<PdfTextLayer?> GetPageTextLayerAsync(int pageNumber, CancellationToken token)
            => throw new NotImplementedException();

        public Task<IRef<SKPicture>?> GetRenderPageAsync(int pageNumber, CancellationToken token)
            => throw new NotImplementedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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

    private static async Task<long> BudgetFor(CalySettings? settings)
    {
        var settingsService = settings is null ? null : new FixedSettingsService(settings);
        await using var pageService = new PdfPageService(new UnopenedPdfDocumentService(), settingsService);
        return pageService.TileRenderService.Cache.MaxMemoryBytes;
    }

    [Fact]
    public async Task DocumentTileCache_UsesTheConfiguredSize()
    {
        Assert.Equal(512L * 1024 * 1024, await BudgetFor(new CalySettings { TileCacheSizeMB = 512 }));
    }

    [Fact]
    public async Task DocumentTileCache_WithoutSettings_UsesTheDefaultSize()
    {
        Assert.Equal(CalySettings.DefaultTileCacheSizeMB * 1024L * 1024, await BudgetFor(null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(300)]
    [InlineData(100_000)]
    public async Task DocumentTileCache_WithASizeThatIsNotAnOption_UsesTheDefaultSize(int megabytes)
    {
        Assert.Equal(CalySettings.DefaultTileCacheSizeMB * 1024L * 1024,
            await BudgetFor(new CalySettings { TileCacheSizeMB = megabytes }));
    }

    [Fact]
    public void DefaultSize_IsOneOfTheOptions()
    {
        Assert.True(CalySettings.IsValidTileCacheSize(CalySettings.DefaultTileCacheSizeMB));
    }

    [Fact]
    public void DefaultSettings_UseTheDefaultSize()
    {
        Assert.Equal(CalySettings.DefaultTileCacheSizeMB, CalySettings.Default.TileCacheSizeMB);
    }
}
