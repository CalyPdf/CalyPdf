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

using Avalonia.Threading;

namespace Caly.Tests.Integration.Support;

/// <summary>
/// Ensures that <see cref="Dispatcher.UIThread"/> is bound to a dedicated background thread
/// before any service integration test runs. Without this, the first test thread to access
/// <see cref="Dispatcher.UIThread"/> becomes the UI thread, which causes
/// <c>Debug.ThrowOnUiThread()</c> guards in production code to throw.
/// <para>
/// The dedicated thread continuously pumps the dispatcher queue via
/// <see cref="Dispatcher.RunJobs()"/> so that synchronous <see cref="Dispatcher.Invoke"/>
/// calls made from test threads (e.g. inside <see cref="Core.ViewModels.DocumentViewModel"/>'s
/// constructor) complete without deadlocking.
/// </para>
/// </summary>
public sealed class AvaloniaDispatcherFixture : IDisposable
{
    private static readonly object _initLock = new();
    private static bool _initialized;

    public AvaloniaDispatcherFixture()
    {
        lock (_initLock)
        {
            if (_initialized)
                return;

            // Spin up a dedicated thread that claims the UIThread dispatcher for itself.
            // All subsequent threads (xUnit worker threads) will therefore NOT be the UI thread,
            // so Debug.ThrowOnUiThread() guards will not trip on test threads.
            var ready = new ManualResetEventSlim(false);
            var uiThread = new Thread(() =>
            {
                // Accessing UIThread on this thread, when s_uiThread is null, creates a new
                // Dispatcher for this thread and stores it as s_uiThread.
                _ = Dispatcher.UIThread;
                ready.Set();

                // Pump the dispatcher queue continuously so that Dispatcher.Invoke calls from
                // test threads (which enqueue work and block on completion) are serviced.
                // RunJobs() drains all currently queued items then returns; the outer loop
                // re-enters immediately to catch the next batch.  SpinWait avoids burning a
                // full CPU core while the queue is empty.
                var spin = new SpinWait();
                while (true)
                {
                    Dispatcher.UIThread.RunJobs();
                    spin.SpinOnce();
                }
            })
            {
                Name = "Avalonia-UIThread-TestStub",
                IsBackground = true
            };
            uiThread.Start();
            ready.Wait(TimeSpan.FromSeconds(5));

            _initialized = true;
        }
    }

    public void Dispose() { /* background thread is daemon and exits with the process */ }
}
