using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace Caly.Tests;

/// <summary>
/// Pins the behaviour of <see cref="Caly.Core.Debug.ThrowOnUiThread"/> under the headless test
/// host, where <c>Avalonia.Headless.HeadlessUnitTestSession</c> starts its dispatcher loop with
/// <c>Task.Run</c> — so <see cref="Dispatcher.UIThread"/> is bound to a *thread-pool* thread.
/// <para>
/// That makes the pool's own threads indistinguishable from the UI thread: measured on this
/// machine, roughly 9% of ordinary pool work items report <c>CheckAccess() == true</c>. Background
/// work guarded by <c>ThrowOnUiThread</c> would therefore throw at random — which is exactly how
/// <c>TileRenderService.RenderTile</c> used to lose tiles, since the render loop swallows the
/// exception and the tile silently never reaches the cache.
/// </para>
/// </summary>
public class DebugThreadGuardTests
{
    [Fact]
    public void ThrowOnUiThread_OnAPlainBackgroundThread_DoesNotThrow()
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                Core.Debug.ThrowOnUiThread();
            }
            catch (Exception e)
            {
                captured = e;
            }
        });
        thread.Start();
        thread.Join();

        Assert.Null(captured);
    }

    [AvaloniaFact]
    public void ThrowOnUiThread_OnTheHeadlessDispatcherThread_DoesNotThrow()
    {
        // An [AvaloniaFact] body runs on the session's dispatcher thread, which the headless host
        // takes from the thread pool. The guard exists to catch rendering work accidentally queued
        // to a real, dedicated UI thread; it must not fire on a pooled thread, because the pool is
        // free to hand that same thread to any background work item.
        Assert.True(Dispatcher.UIThread.CheckAccess());
        Assert.True(Thread.CurrentThread.IsThreadPoolThread);

        Core.Debug.ThrowOnUiThread();
    }
}
