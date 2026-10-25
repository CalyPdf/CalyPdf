using Avalonia.Headless.XUnit;
using Caly.Core;
using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Core.Services.Interfaces;
using CommunityToolkit.Mvvm.Messaging;
using SkiaSharp;
using System.Reflection;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Outline.Destinations;
using UglyToad.PdfPig.Tokens;
using UglyToad.PdfPig.Writer;

namespace Caly.Tests;

/// <summary>
/// How <see cref="PdfPigDocumentService.GetRenderPageAsync"/> reports a page render that ends in an
/// <see cref="OperationCanceledException"/>: nothing when the caller cancelled, the timeout picture
/// only when the timeout fired, and an error otherwise.
/// </summary>
public class PdfPigDocumentServiceRenderPageTests
{
    private const string TimeoutMessage = "Could not display page after";

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
    /// Renders every page by running <paramref name="render"/> with the render's token.
    /// </summary>
    private sealed class DelegatePageFactory(Func<CancellationToken, SKPicture> render) : IPageFactory<SKPicture>
    {
        public SKPicture Create(int number, DictionaryToken dictionary, PageTreeMembers pageTreeMembers,
            NamedDestinations namedDestinations, CancellationToken token) => render(token);
    }

    private static readonly FieldInfo DocumentField =
        typeof(PdfPigDocumentService).GetField("_document", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo OpenDocumentTaskField =
        typeof(PdfPigDocumentService).GetField("_openDocumentTask", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo SemaphoreField =
        typeof(PdfPigDocumentService).GetField("_semaphore", BindingFlags.NonPublic | BindingFlags.Instance)!;

    /// <summary>
    /// A service holding a one-page document whose pages render with <paramref name="render"/>, in
    /// the state a successful <see cref="PdfPigDocumentService.OpenDocument"/> leaves it in. The
    /// service owns the document.
    /// </summary>
    private static PdfPigDocumentService OpenedWith(Func<CancellationToken, SKPicture> render)
    {
        var builder = new PdfDocumentBuilder();
        builder.AddPage(PageSize.A4);
        var document = PdfDocument.Open(builder.Build());
        document.AddPageFactory<SKPicture>(new DelegatePageFactory(render));

        var service = new PdfPigDocumentService(new FakeSettingsService());
        DocumentField.SetValue(service, document);
        OpenDocumentTaskField.SetValue(service, Task.FromResult(DocumentOpeningState.Success));

        // The lock starts taken; opening the document releases it.
        ((SemaphoreSlim)SemaphoreField.GetValue(service)!).Release();
        return service;
    }

    /// <summary>
    /// Renders page 1 and returns whether a picture came back, and the notifications sent meanwhile.
    /// </summary>
    private static async Task<(bool HasPicture, List<string?> Messages)> RenderPage1(PdfPigDocumentService service, CancellationToken token)
    {
        List<string?> messages = [];
        var recipient = new object();
        App.Messenger.Register<ShowNotificationMessage>(recipient, (_, m) =>
        {
            lock (messages)
            {
                messages.Add(m.Value.Message);
            }
        });

        try
        {
            using var picture = await service.GetRenderPageAsync(1, token);
            return (picture is not null, messages);
        }
        finally
        {
            App.Messenger.Unregister<ShowNotificationMessage>(recipient);
        }
    }

    [AvaloniaFact]
    public async Task GetRenderPageAsync_CancellationNobodyAskedFor_IsAnErrorNotATimeout()
    {
        // The render ends in an OperationCanceledException while the caller's token and the
        // timeout are both still live. The renderer did this by replaying a cancelled render's
        // exception from its document-wide Type 3 glyph cache. Reported as a timeout, it read as
        // "Could not display page after 30 seconds." the moment a tab was selected.
        await Task.Run(async () =>
        {
            await using var service = OpenedWith(_ => throw new OperationCanceledException());

            var (hasPicture, messages) = await RenderPage1(service, CancellationToken.None);

            Assert.True(hasPicture, "Expected the error picture.");
            Assert.DoesNotContain(messages, m => m?.Contains(TimeoutMessage) == true);
        });
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetRenderPageAsync_CallerCancelsMidRender_ReturnsNothingAndDoesNotNotify(bool wrapped)
    {
        // The user switched tabs while the page rendered. PdfPig wraps what a page of an encrypted
        // document throws, the cancellation included, in another exception.
        await Task.Run(async () =>
        {
            using var cts = new CancellationTokenSource();
            await using var service = OpenedWith(token =>
            {
                cts.Cancel();
                try
                {
                    token.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException e) when (wrapped)
                {
                    throw new InvalidOperationException("Wrapped", e);
                }

                throw new InvalidOperationException("The render's token should be cancelled.");
            });

            var (hasPicture, messages) = await RenderPage1(service, cts.Token);

            Assert.False(hasPicture);
            Assert.Empty(messages);
        });
    }
}
