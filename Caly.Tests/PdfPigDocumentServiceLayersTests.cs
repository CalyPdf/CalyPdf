using Avalonia.Headless.XUnit;
using Avalonia.Platform.Storage;
using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Core.Services.Interfaces;
using Moq;

namespace Caly.Tests;

public class PdfPigDocumentServiceLayersTests
{
    private sealed class FakeSettingsService : ISettingsService
    {
        public void SetProperty(CalySettings.CalySettingsProperty property, object value)
        {
        }

        public CalySettings GetSettings() => CalySettings.Default;

        public ValueTask<CalySettings> GetSettingsAsync() => ValueTask.FromResult(CalySettings.Default);

        public void Load()
        {
        }

        public Task LoadAsync() => Task.CompletedTask;

        public void Save()
        {
        }

        public Task SaveAsync() => Task.CompletedTask;
    }

    private static IStorageFile File(string name)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Documents", name);
        var file = new Mock<IStorageFile>();
        file.SetupGet(f => f.Path).Returns(new Uri(path));
        file.SetupGet(f => f.Name).Returns(name);
        file.Setup(f => f.OpenReadAsync()).Returns(() => Task.FromResult<Stream>(System.IO.File.OpenRead(path)));
        return file.Object;
    }

    [AvaloniaFact]
    public async Task GetLayersAsync_ListsTheDocumentsLayers()
    {
        await Task.Run(async () =>
        {
            await using var service = new PdfPigDocumentService(new FakeSettingsService());
            Assert.Equal(DocumentOpeningState.Success,
                await service.OpenDocument(File("GWG151_OptionalContent-RBGroup_X4.pdf"), null, CancellationToken.None));

            var layers = await service.GetLayersAsync(CancellationToken.None);

            Assert.NotNull(layers);
            Assert.Equal(new[] { "Default", "GWG View 1", "GWG View 2" }, layers.Select(l => l.Name));
        });
    }

    [AvaloniaFact]
    public async Task GetLayersAsync_IsNullWithoutLayers()
    {
        await Task.Run(async () =>
        {
            await using var service = new PdfPigDocumentService(new FakeSettingsService());
            Assert.Equal(DocumentOpeningState.Success,
                await service.OpenDocument(File("ICML03-081.pdf"), null, CancellationToken.None));

            Assert.Null(await service.GetLayersAsync(CancellationToken.None));
        });
    }

    [AvaloniaFact(Skip = "Re-enabled by Task 5 (tagged text layer)")]
    public async Task SetLayerVisibilityAsync_ReturnsRadioSiblingsAndChangesTheTextLayer()
    {
        await Task.Run(async () =>
        {
            await using var service = new PdfPigDocumentService(new FakeSettingsService());
            Assert.Equal(DocumentOpeningState.Success,
                await service.OpenDocument(File("GWG151_OptionalContent-RBGroup_X4.pdf"), null, CancellationToken.None));

            var layers = (await service.GetLayersAsync(CancellationToken.None))!;
            int viewOne = layers.Single(l => l.Name == "GWG View 1").GroupIndex!.Value;
            int defaultLayer = layers.Single(l => l.Name == "Default").GroupIndex!.Value;

            var before = (await service.GetPageTextLayerAsync(1, CancellationToken.None))!.Select(w => w.Value).ToArray();

            var states = await service.SetLayerVisibilityAsync(viewOne, true, CancellationToken.None);

            Assert.NotNull(states);
            Assert.True(states[viewOne]);
            Assert.False(states[defaultLayer]);

            var after = (await service.GetPageTextLayerAsync(1, CancellationToken.None))!.Select(w => w.Value).ToArray();
            Assert.NotEqual(before, after);
        });
    }

    [AvaloniaFact]
    public async Task SetLayerVisibilityAsync_IgnoresAnUnknownGroupIndex()
    {
        await Task.Run(async () =>
        {
            await using var service = new PdfPigDocumentService(new FakeSettingsService());
            Assert.Equal(DocumentOpeningState.Success,
                await service.OpenDocument(File("GWG151_OptionalContent-RBGroup_X4.pdf"), null, CancellationToken.None));

            Assert.Null(await service.SetLayerVisibilityAsync(999, true, CancellationToken.None));
            Assert.Null(await service.SetLayerVisibilityAsync(-1, true, CancellationToken.None));
        });
    }

    private static byte[] Pixels(SkiaSharp.SKPicture picture)
    {
        using var bitmap = new SkiaSharp.SKBitmap(200, 200);
        using var canvas = new SkiaSharp.SKCanvas(bitmap);
        canvas.Clear(SkiaSharp.SKColors.White);
        canvas.Scale(200f / picture.CullRect.Width, 200f / picture.CullRect.Height);
        canvas.DrawPicture(picture);
        return bitmap.Bytes;
    }

    [AvaloniaFact]
    public async Task SetLayerVisibilityAsync_RecomposesWithoutReprocessing()
    {
        await Task.Run(async () =>
        {
            await using var service = new PdfPigDocumentService(new FakeSettingsService());
            Assert.Equal(DocumentOpeningState.Success,
                await service.OpenDocument(File("GWG151_OptionalContent-RBGroup_X4.pdf"), null, CancellationToken.None));

            var layers = (await service.GetLayersAsync(CancellationToken.None))!;
            int viewOne = layers.Single(l => l.Name == "GWG View 1").GroupIndex!.Value;

            using var before = await service.GetRenderPageAsync(1, CancellationToken.None);
            int processed = service.LayeredPagesProcessed;
            Assert.True(processed > 0);

            await service.SetLayerVisibilityAsync(viewOne, true, CancellationToken.None);
            using var after = await service.GetRenderPageAsync(1, CancellationToken.None);

            Assert.Equal(processed, service.LayeredPagesProcessed); // composed from the cache
            Assert.NotEqual(Pixels(before!.Item), Pixels(after!.Item));
        });
    }

    [AvaloniaFact]
    public async Task ClearLayeredPages_ForcesReprocessingButAToggleDoesNot()
    {
        await Task.Run(async () =>
        {
            await using var service = new PdfPigDocumentService(new FakeSettingsService());
            Assert.Equal(DocumentOpeningState.Success,
                await service.OpenDocument(File("GWG151_OptionalContent-RBGroup_X4.pdf"), null, CancellationToken.None));

            var layers = (await service.GetLayersAsync(CancellationToken.None))!;
            int viewOne = layers.Single(l => l.Name == "GWG View 1").GroupIndex!.Value;

            using (await service.GetRenderPageAsync(1, CancellationToken.None)) { }
            int processed = service.LayeredPagesProcessed;

            await service.SetLayerVisibilityAsync(viewOne, true, CancellationToken.None);
            using (await service.GetRenderPageAsync(1, CancellationToken.None)) { }
            Assert.Equal(processed, service.LayeredPagesProcessed);

            service.ClearLayeredPages();
            using (await service.GetRenderPageAsync(1, CancellationToken.None)) { }
            Assert.Equal(processed + 1, service.LayeredPagesProcessed);
        });
    }
}
