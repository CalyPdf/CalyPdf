using Avalonia.Headless.XUnit;
using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Core.Services.Interfaces;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;

namespace Caly.Tests;

/// <summary>
/// Tests for the once-only contract of <see cref="PdfPigDocumentService.OpenDocument"/>:
/// the open task (<c>_openDocumentTask</c>) is set exactly once per instance, and every
/// call after the first throws.
/// </summary>
public class PdfPigDocumentServiceOpenDocumentTests
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

    private static readonly FieldInfo ActiveOperationsField =
        typeof(PdfPigDocumentService).GetField("_activeOperations", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo ResourcesReleasedField =
        typeof(PdfPigDocumentService).GetField("_resourcesReleased", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static bool ResourcesReleased(PdfPigDocumentService service) =>
        (long)ResourcesReleasedField.GetValue(service)! != 0;

    private static readonly FieldInfo OpenDocumentTaskField =
        typeof(PdfPigDocumentService).GetField("_openDocumentTask", BindingFlags.NonPublic | BindingFlags.Instance)!;

    [AvaloniaFact]
    public async Task OpenDocument_CalledAgain_ThrowsAndKeepsFirstTask()
    {
        // The open/dispose paths assert they do not run on the UI thread; with a headless
        // session active, Task.Run reliably lands off it.
        await Task.Run(async () =>
        {
            await using var service = new PdfPigDocumentService(new FakeSettingsService());

            // A null storage file is a valid first call: the open completes with FileNotFound.
            Task<DocumentOpeningState> first = service.OpenDocument(null, null, CancellationToken.None);

            Assert.NotNull(first);
            Assert.Same(first, OpenDocumentTaskField.GetValue(service));
            Assert.Equal(DocumentOpeningState.FileNotFound, await first);

            for (int i = 0; i < 3; ++i)
            {
                await Assert.ThrowsAnyAsync<Exception>(() => service.OpenDocument(null, null, CancellationToken.None));

                // _openDocumentTask is set exactly once: still the task from the first call.
                Assert.Same(first, OpenDocumentTaskField.GetValue(service));
            }
        });
    }

    [AvaloniaFact]
    public async Task OpenDocument_ConcurrentCalls_ExactlyOneWins()
    {
        await Task.Run(async () =>
        {
            await using var service = new PdfPigDocumentService(new FakeSettingsService());

            var winners = new ConcurrentBag<Task<DocumentOpeningState>>();
            int throwers = 0;

            await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            {
                try
                {
                    winners.Add(service.OpenDocument(null, null, CancellationToken.None));
                }
                catch (Exception)
                {
                    Interlocked.Increment(ref throwers);
                }
            })));

            var winner = Assert.Single(winners);
            Assert.Equal(7, throwers);
            Assert.Same(winner, OpenDocumentTaskField.GetValue(service));
        });
    }

    [AvaloniaFact]
    public async Task GetPageSizeAsync_OpenInFlight_CanBeCanceled()
    {
        await Task.Run(async () =>
        {
            await using var service = new PdfPigDocumentService(new FakeSettingsService());

            var openingTcs = new TaskCompletionSource<DocumentOpeningState>(TaskCreationOptions.RunContinuationsAsynchronously);
            OpenDocumentTaskField.SetValue(service, openingTcs.Task);

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            var stopwatch = Stopwatch.StartNew();
            var result = await service.GetPageSizeAsync(1, cts.Token);
            stopwatch.Stop();

            Assert.Null(result);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2),
                $"Operation should cancel quickly while waiting for open, but took {stopwatch.Elapsed}.");
        });
    }

    /// <summary>
    /// Regression: closing a window (or tab) while a document is still parsing used to tear the
    /// file stream down under <c>PdfDocument.Open</c>, which then kept seeking into a closed
    /// file — <c>ObjectDisposedException: Cannot access a closed file</c>, repeatedly.
    /// <para>
    /// <c>Open</c> is a long synchronous parse that cancellation cannot interrupt, so it can
    /// outlive any bounded wait. Teardown is therefore owned by whoever finishes last.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public async Task DisposeAsync_DefersTeardownWhileAnOperationIsStillRunning()
    {
        await Task.Run(async () =>
        {
            var service = new PdfPigDocumentService(new FakeSettingsService());

            // Stand in for a parse still running inside Task.Run.
            ActiveOperationsField.SetValue(service, 1);

            await service.DisposeAsync();

            Assert.False(ResourcesReleased(service),
                "Resources were torn down while an operation was still using them.");

            // The operation finishes and hands the resources back.
            ActiveOperationsField.SetValue(service, 0);
            await service.ReleaseResourcesIfIdleAsync();

            Assert.True(ResourcesReleased(service));
        });
    }

    [AvaloniaFact]
    public async Task DisposeAsync_ReleasesImmediatelyWhenNoOperationIsRunning()
    {
        await Task.Run(async () =>
        {
            var service = new PdfPigDocumentService(new FakeSettingsService());

            await service.DisposeAsync();

            Assert.True(ResourcesReleased(service));
        });
    }

    /// <summary>
    /// Both the dispose path and the last in-flight operation call the release; it must run once.
    /// </summary>
    [AvaloniaFact]
    public async Task ReleaseResources_IsIdempotent()
    {
        await Task.Run(async () =>
        {
            var service = new PdfPigDocumentService(new FakeSettingsService());

            await service.DisposeAsync();
            Assert.True(ResourcesReleased(service));

            // Must not throw - the semaphore and CTS have already been disposed.
            await service.ReleaseResourcesIfIdleAsync();
            await service.DisposeAsync();

            Assert.True(ResourcesReleased(service));
        });
    }
}
