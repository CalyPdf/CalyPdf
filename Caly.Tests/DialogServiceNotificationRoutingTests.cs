using Avalonia.Controls.Notifications;
using Avalonia.Headless.XUnit;
using Caly.Core.Services;
using Caly.Core.Services.Interfaces;
using Caly.Core.ViewModels;

namespace Caly.Tests;

/// <summary>
/// Notification de-duplication is per-window, because notification routing is.
/// <para>
/// <see cref="DialogService"/> suppresses a message that repeats within three seconds. While
/// there was one window that state could live in two fields on the singleton. Once a document
/// can be opened into any window, the destination varies per call and the state has to vary
/// with it: the failure texts are constant strings, so two windows each failing to open their
/// own file would otherwise show the error in the first and silently swallow it in the second.
/// </para>
/// </summary>
public class DialogServiceNotificationRoutingTests
{
    private const string Message = "Cannot load pages because something wrong happened while opening the document.";

    /// <summary>
    /// The registry is only reached through <c>FindContext</c>/<c>Active</c> here, and these
    /// tests drive the suppression decision directly, so an empty registry is enough.
    /// </summary>
    private static DialogService NewService() => new(new CalyWindowRegistry());

    /// <summary>
    /// Standalone managers, used only as identity keys. They are never shown, so they need no
    /// host window.
    /// </summary>
    private static WindowNotificationManager NewManager() => new();

    [AvaloniaFact]
    public void TheSameMessageTwiceInOneWindow_IsSuppressed()
    {
        var service = NewService();
        var window = NewManager();
        var now = DateTime.UtcNow;

        Assert.False(service.ShouldSuppressNotification(window, Message, now));
        Assert.True(service.ShouldSuppressNotification(window, Message, now.AddSeconds(1)));
    }

    /// <summary>
    /// The regression: before the fix a single pair of fields backed every window, so the
    /// second window's first sighting of the message was mistaken for a repeat and dropped.
    /// </summary>
    [AvaloniaFact]
    public void TheSameMessageInTwoWindows_IsShownInBoth()
    {
        var service = NewService();
        var first = NewManager();
        var second = NewManager();
        var now = DateTime.UtcNow;

        Assert.False(service.ShouldSuppressNotification(first, Message, now));

        // Same text, well within the suppression window, but a different window: this is that
        // window's first sighting and must not be swallowed.
        Assert.False(service.ShouldSuppressNotification(second, Message, now.AddSeconds(1)));
    }

    /// <summary>
    /// Suppression is still per-window after both have seen the message.
    /// </summary>
    [AvaloniaFact]
    public void EachWindowKeepsItsOwnSuppressionWindow()
    {
        var service = NewService();
        var first = NewManager();
        var second = NewManager();
        var now = DateTime.UtcNow;

        service.ShouldSuppressNotification(first, Message, now);
        service.ShouldSuppressNotification(second, Message, now.AddSeconds(1));

        Assert.True(service.ShouldSuppressNotification(first, Message, now.AddSeconds(2)));
        Assert.True(service.ShouldSuppressNotification(second, Message, now.AddSeconds(2)));
    }

    [AvaloniaFact]
    public void AMessageIsShownAgainOnceTheDelayHasPassed()
    {
        var service = NewService();
        var window = NewManager();
        var now = DateTime.UtcNow;

        Assert.False(service.ShouldSuppressNotification(window, Message, now));
        Assert.False(service.ShouldSuppressNotification(window, Message, now.AddSeconds(4)));
    }

    [AvaloniaFact]
    public void ADifferentMessageInTheSameWindow_IsNotSuppressed()
    {
        var service = NewService();
        var window = NewManager();
        var now = DateTime.UtcNow;

        Assert.False(service.ShouldSuppressNotification(window, Message, now));
        Assert.False(service.ShouldSuppressNotification(window, "Could not open password protected document.", now));
    }

    [AvaloniaFact]
    public void AnEmptyMessageIsAlwaysSuppressed()
    {
        var service = NewService();
        var window = NewManager();

        Assert.True(service.ShouldSuppressNotification(window, null, DateTime.UtcNow));
        Assert.True(service.ShouldSuppressNotification(window, string.Empty, DateTime.UtcNow));
    }
}
