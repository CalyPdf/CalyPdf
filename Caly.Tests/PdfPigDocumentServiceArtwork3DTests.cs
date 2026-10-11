using Avalonia.Headless.XUnit;
using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Core.Services.Interfaces;
using Caly.Pdf.Artwork;

namespace Caly.Tests;

public class PdfPigDocumentServiceArtwork3DTests
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

    /// <summary>
    /// A click on a badge can come after its document failed to open or was closed: the service then
    /// answers "nothing", and does not touch the artwork.
    /// </summary>
    [AvaloniaFact]
    public async Task ADocumentThatIsNotOpenGivesNoContent()
    {
        using Artwork3DTestDocument source = Artwork3DTestDocument.OpenBox();

        await Task.Run(async () =>
        {
            await using var service = new PdfPigDocumentService(new FakeSettingsService());
            Assert.Equal(DocumentOpeningState.FileNotFound, await service.OpenDocument(null, null, CancellationToken.None));

            Artwork3DContent? content = await service.GetArtwork3DContentAsync(source.Artworks[0], CancellationToken.None);

            Assert.Null(content);
        });
    }

    [AvaloniaFact]
    public async Task ACancelledRequestGivesNoContent()
    {
        using Artwork3DTestDocument source = Artwork3DTestDocument.OpenBox();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Task.Run(async () =>
        {
            await using var service = new PdfPigDocumentService(new FakeSettingsService());
            await service.OpenDocument(null, null, CancellationToken.None);

            Assert.Null(await service.GetArtwork3DContentAsync(source.Artworks[0], cancelled.Token));
        });
    }
}
