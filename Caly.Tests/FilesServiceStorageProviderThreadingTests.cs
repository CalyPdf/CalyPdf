using System.IO;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Caly.Core.Services;
using Caly.Core.Services.Interfaces;
using Caly.Core.ViewModels;

namespace Caly.Tests;

/// <summary>
/// <see cref="FilesService"/> resolves its storage provider through the window registry, so
/// pickers open over the window the user is working in rather than always over the one created
/// at startup. The registry is UI-thread-only, and not every caller is on it.
/// <para>
/// <c>SaveFileAsync</c> is reached from a <c>Task.Run</c> in
/// <c>PdfDocumentsManagerService.HandleSaveEmbeddedFileRequestMessage</c>, so the lookup ran on
/// a thread-pool thread and enumerated the registry's <c>List</c> while the UI thread could be
/// registering or unregistering a window. The dispatch cannot be left to the caller.
/// </para>
/// </summary>
public class FilesServiceStorageProviderThreadingTests
{
    /// <summary>
    /// Records the thread every registry read happened on. <c>Active</c> is the member
    /// <c>GetStorageProvider</c> reaches for.
    /// </summary>
    private sealed class ThreadRecordingWindowRegistry : ICalyWindowRegistry
    {
        private readonly CalyWindowContext? _active;

        public ThreadRecordingWindowRegistry(CalyWindowContext? active) => _active = active;

        public List<bool> ActiveReadOnUiThread { get; } = [];

        public event EventHandler<IReadOnlyList<DocumentViewModel>>? DocumentsOrphaned;

        public event EventHandler<CalyWindowContext>? WindowRegistered;

        public CalyWindowContext? Active
        {
            get
            {
                ActiveReadOnUiThread.Add(Dispatcher.UIThread.CheckAccess());
                return _active;
            }
        }

        public CalyWindowContext? Primary => Active;

        public IReadOnlyList<CalyWindowContext> Windows => _active is null ? [] : [_active];

        public CalyWindowContext? FindOwnerOf(DocumentViewModel? document) => null;

        public CalyWindowContext? FindContext(MainViewModel viewModel) => null;

        public void CloseWindowIfEmpty(MainViewModel viewModel) { }

        public void Register(CalyWindowContext context)
            => WindowRegistered?.Invoke(this, context);

        public void RegisterWhenOpened(CalyWindowContext context) { }

        public void Unregister(CalyWindowContext context)
            => DocumentsOrphaned?.Invoke(this, []);
    }

    /// <summary>
    /// The regression: called from a thread-pool thread, the registry must still be read on the
    /// UI thread.
    /// </summary>
    [AvaloniaFact]
    public async Task SaveFileAsync_FromABackgroundThread_ReadsTheRegistryOnTheUiThread()
    {
        var window = new Window { Width = 200, Height = 200 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var viewModel = new MainViewModel();
            viewModel.Dispose();

            var registry = new ThreadRecordingWindowRegistry(new CalyWindowContext
            {
                ViewModel = viewModel,
                Window = window,
                IsPrimary = true
            });

            var service = new FilesService(window.StorageProvider, registry);

            // Exactly how HandleSaveEmbeddedFileRequestMessage reaches it.
            await Task.Run(() => service.SaveFileAsync(new byte[] { 1, 2, 3 }, "attachment.bin"));

            Assert.NotEmpty(registry.ActiveReadOnUiThread);
            Assert.All(registry.ActiveReadOnUiThread, Assert.True);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// <c>SaveTempFileAsync</c> backs "open an embedded attachment". It used to reach for the
    /// injected provider directly, which is the one belonging to the window created at startup -
    /// a window that can now close while the app runs on in a detached one, leaving its
    /// <c>TopLevel</c> disposed. It must go through the same resolution as the other pickers.
    /// </summary>
    [AvaloniaFact]
    public async Task SaveTempFileAsync_ResolvesThroughTheRegistryToo()
    {
        var window = new Window { Width = 200, Height = 200 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var viewModel = new MainViewModel();
            viewModel.Dispose();

            var registry = new ThreadRecordingWindowRegistry(new CalyWindowContext
            {
                ViewModel = viewModel,
                Window = window,
                IsPrimary = true
            });

            var service = new FilesService(window.StorageProvider, registry);

            // This really does write to the temp directory, so the file is named uniquely and
            // deleted below: SaveTempFileAsync uniquifies around an existing name, so a leftover
            // would silently change what the next run exercises.
            string name = $"caly-test-{Guid.NewGuid():N}.bin";
            IStorageFile? written = await Task.Run(() => service.SaveTempFileAsync(new byte[] { 1, 2, 3 }, name));

            try
            {
                Assert.NotEmpty(registry.ActiveReadOnUiThread);
                Assert.All(registry.ActiveReadOnUiThread, Assert.True);
            }
            finally
            {
                if (written?.TryGetLocalPath() is { } path && File.Exists(path))
                {
                    File.Delete(path);
                }

                written?.Dispose();
            }
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// The UI-thread callers must not pay for the dispatch by deadlocking or re-entering:
    /// <c>Invoke</c> runs inline when the caller already holds the UI thread.
    /// </summary>
    [AvaloniaFact]
    public async Task OpenPdfFileAsync_OnTheUiThread_StillResolvesInline()
    {
        var window = new Window { Width = 200, Height = 200 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var viewModel = new MainViewModel();
            viewModel.Dispose();

            var registry = new ThreadRecordingWindowRegistry(new CalyWindowContext
            {
                ViewModel = viewModel,
                Window = window,
                IsPrimary = true
            });

            var service = new FilesService(window.StorageProvider, registry);

            // An explicit owner short-circuits the registry read, so this asserts the call
            // completes rather than counting reads.
            Assert.Null(await service.OpenPdfFileAsync(window));
        }
        finally
        {
            window.Close();
        }
    }
}
