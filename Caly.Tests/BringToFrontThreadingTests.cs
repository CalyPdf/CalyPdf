using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Caly.Core;
using Caly.Core.Services;
using Caly.Core.Services.Interfaces;
using Caly.Core.ViewModels;

namespace Caly.Tests;

/// <summary>
/// A second Caly instance hands its file over down a named pipe and asks the running one to
/// come forward. That request arrives on the pipe listener's background thread
/// (<c>App.ListenToIncomingFiles</c> asserts it is off the UI thread), and answering it means
/// reading the window registry and activating a window - neither of which is safe there.
/// <para>
/// The registry is backed by a plain list mutated as windows open and close, so enumerating it
/// from another thread can throw "Collection was modified". The lookup used to sit outside the
/// method's <c>try</c>, so that throw unwound into the listener, which reports and rethrows -
/// leaving the app unable to accept any further handoff for the rest of the session.
/// </para>
/// </summary>
public class BringToFrontThreadingTests
{
    /// <summary>
    /// Records the thread each registry read happened on, and can be made to throw the way a
    /// real concurrent mutation would.
    /// </summary>
    private sealed class ThreadRecordingWindowRegistry : ICalyWindowRegistry
    {
        private readonly CalyWindowContext? _active;
        private readonly bool _throwOnRead;

        public ThreadRecordingWindowRegistry(CalyWindowContext? active, bool throwOnRead = false)
        {
            _active = active;
            _throwOnRead = throwOnRead;
        }

        public List<bool> ActiveReadOnUiThread { get; } = [];

        public event EventHandler<IReadOnlyList<DocumentViewModel>>? DocumentsOrphaned;

        public event EventHandler<CalyWindowContext>? WindowRegistered;

        public CalyWindowContext? Active
        {
            get
            {
                ActiveReadOnUiThread.Add(Dispatcher.UIThread.CheckAccess());

                if (_throwOnRead)
                {
                    // What List<T>'s enumerator raises when the collection changes under it.
                    throw new InvalidOperationException("Collection was modified");
                }

                return _active;
            }
        }

        public CalyWindowContext? Primary => Active;

        public IReadOnlyList<CalyWindowContext> Windows => _active is null ? [] : [_active];

        public CalyWindowContext? FindOwnerOf(DocumentViewModel? document) => null;

        public CalyWindowContext? FindContext(MainViewModel viewModel) => null;

        public void CloseWindowIfEmpty(MainViewModel viewModel) { }

        public void Register(CalyWindowContext context) => WindowRegistered?.Invoke(this, context);

        public void RegisterWhenOpened(CalyWindowContext context) { }

        public void Unregister(CalyWindowContext context) => DocumentsOrphaned?.Invoke(this, []);
    }

    private static CalyWindowContext NewContext(Window window)
    {
        var viewModel = new MainViewModel();
        viewModel.Dispose();
        return new CalyWindowContext { ViewModel = viewModel, Window = window, IsPrimary = true };
    }

    /// <summary>
    /// The regression: called from the pipe listener's thread, the registry must still be read
    /// on the UI thread.
    /// </summary>
    [AvaloniaFact]
    public async Task FromABackgroundThread_ReadsTheRegistryOnTheUiThread()
    {
        var window = new Window { Width = 200, Height = 200 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var registry = new ThreadRecordingWindowRegistry(NewContext(window));

            bool raised = await Task.Run(() => App.BringToFront(registry));

            Assert.True(raised);
            Assert.NotEmpty(registry.ActiveReadOnUiThread);
            Assert.All(registry.ActiveReadOnUiThread, Assert.True);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// The consequence that made this worth fixing: whatever the lookup throws must not escape
    /// to the caller, because the pipe listener rethrows and stops accepting handoffs.
    /// </summary>
    [AvaloniaFact]
    public async Task AThrowingRegistryIsContained()
    {
        var window = new Window { Width = 200, Height = 200 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var registry = new ThreadRecordingWindowRegistry(NewContext(window), throwOnRead: true);

            Assert.False(await Task.Run(() => App.BringToFront(registry)));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Every window has closed - a second instance can send this while the app is shutting
    /// down. Reported as "nothing to raise", not as a failure to contain.
    /// </summary>
    [AvaloniaFact]
    public async Task WithNoWindowLeft_ReportsNothingToRaise()
    {
        var registry = new ThreadRecordingWindowRegistry(active: null);

        Assert.False(await Task.Run(() => App.BringToFront(registry)));
        Assert.All(registry.ActiveReadOnUiThread, Assert.True);
    }

    [AvaloniaFact]
    public void WithNoRegistry_ReportsNothingToRaise()
    {
        Assert.False(App.BringToFront(null));
    }

    /// <summary>
    /// A minimised window is restored, which is the other half of "bring to front".
    /// </summary>
    [AvaloniaFact]
    public async Task AMinimisedWindowIsRestored()
    {
        var window = new Window { Width = 200, Height = 200 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            window.WindowState = WindowState.Minimized;
            Dispatcher.UIThread.RunJobs();

            Assert.True(await Task.Run(() => App.BringToFront(new ThreadRecordingWindowRegistry(NewContext(window)))));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(WindowState.Normal, window.WindowState);
        }
        finally
        {
            window.Close();
        }
    }
}
