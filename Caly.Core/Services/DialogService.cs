// Copyright (c) 2025 BobLd
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

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using Caly.Core.Models;
using Caly.Core.Services.Interfaces;
using Caly.Core.ViewModels;
using Caly.Core.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Caly.Core.Services;

internal sealed class DialogService : IDialogService
{
    private readonly TimeSpan _annotationExpiration = TimeSpan.FromSeconds(20);
    private readonly ICalyWindowRegistry _windowRegistry;
    private readonly TimeSpan _minDelay = TimeSpan.FromSeconds(3);

    /// <summary>
    /// The last notification shown on one window, so a repeat of it can be suppressed.
    /// </summary>
    private sealed class LastNotification
    {
        public string? Message { get; set; }

        public DateTime Time { get; set; } = DateTime.MinValue;
    }

    /// <summary>
    /// Per-window de-duplication state, keyed by the notification manager the message lands on.
    /// <para>
    /// One shared pair of fields would suppress across windows: the failure messages are
    /// constant strings, so two windows each failing to open their own file within
    /// <see cref="_minDelay"/> would show the error in the first and silently swallow it in the
    /// second - undoing the per-window routing this service exists to provide.
    /// </para>
    /// <para>
    /// Weak-keyed: a closed window's manager must not be held alive by this table.
    /// </para>
    /// </summary>
    private readonly ConditionalWeakTable<WindowNotificationManager, LastNotification> _lastNotifications = [];

    private string? _previousExceptionWindowMessage;
    private DateTime _previousExceptionWindowTime = DateTime.MinValue;

    public DialogService(ICalyWindowRegistry windowRegistry)
    {
        _windowRegistry = windowRegistry ?? throw new ArgumentNullException(nameof(windowRegistry));
    }

    /// <summary>
    /// The window the user is currently working in. Resolved per call, not cached: dialogs
    /// and notifications must land on the window that triggered them, which may be a
    /// detached one.
    /// </summary>
    private Window? ActiveWindow => _windowRegistry.Active?.Window;

    private WindowNotificationManager? NotificationManager => _windowRegistry.Active?.Notifications;

    /// <summary>
    /// The notification manager of <paramref name="target"/>, falling back to the active
    /// window's - the target may have closed between raising the notification and showing it.
    /// </summary>
    private WindowNotificationManager? NotificationManagerFor(MainViewModel? target) =>
        (target is not null ? _windowRegistry.FindContext(target)?.Notifications : null)
        ?? NotificationManager;

    /// <summary>
    /// Whether <paramref name="message"/> repeats what <paramref name="manager"/> showed less
    /// than <see cref="_minDelay"/> ago, and should therefore be dropped. Records it as the
    /// last message for that manager when it is not.
    /// <para>
    /// The window it lands on is part of the key. Suppressing per-window rather than globally
    /// is what stops one window's error from silencing a different window's first sighting of
    /// the same text - and the failure texts are constant strings, so that collision is the
    /// common case rather than a corner one.
    /// </para>
    /// </summary>
    internal bool ShouldSuppressNotification(WindowNotificationManager manager, string? message, DateTime now)
    {
        if (string.IsNullOrEmpty(message))
        {
            return true;
        }

        LastNotification last = _lastNotifications.GetOrCreateValue(manager);

        if (now - last.Time <= _minDelay && message.Equals(last.Message))
        {
            return true;
        }

        last.Time = now;
        last.Message = message;
        return false;
    }

    public void ShowNotification(CalyNotification notification, MainViewModel? target = null)
    {
        ShowNotification(notification.Title, notification.Message, notification.Type, target);
    }

    public void ShowNotification(string? title, string? message, NotificationType type, MainViewModel? target = null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Debug.ThrowNotOnUiThread();
            System.Diagnostics.Debug.WriteLine($"Annotation ({type}): {title}\n{message}");

            // Resolved inside the posted callback, on the UI thread, because the registry is
            // only safe to read there.
            if (NotificationManagerFor(target) is { } manager)
            {
                if (ShouldSuppressNotification(manager, message, DateTime.UtcNow))
                {
                    return;
                }

                manager.Show(new Notification(title, message, type, _annotationExpiration));
            }
            else
            {
                // TODO - we need a queue system to display the annotations when the manager is loaded
                System.Diagnostics.Debug.WriteLine($"Annotation (ERROR NOT LOADED) ({type}): {title}\n{message}");
            }
        }, DispatcherPriority.Loaded);
    }

    public Task ShowExceptionWindowAsync(Exception exception)
    {
        return ShowExceptionWindowAsync(new ExceptionViewModel(exception));
    }

    public async Task ShowExceptionWindowAsync(ExceptionViewModel exception)
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            Debug.ThrowNotOnUiThread();
            System.Diagnostics.Debug.WriteLine(exception.ToString());
            if (ActiveWindow is not { } w)
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            if (string.IsNullOrEmpty(exception.Message) ||
                (now - _previousExceptionWindowTime <= _minDelay &&
                 exception.Message.Equals(_previousExceptionWindowMessage)))
            {
                return;
            }

            // TODO - Improve to count same messages
            _previousExceptionWindowTime = now;
            _previousExceptionWindowMessage = exception.Message;
            var window = new MessageWindow { DataContext = exception };
            await window.ShowDialog(w);

        }, DispatcherPriority.Loaded);
    }

    public void ShowExceptionWindow(Exception exception)
    {
        ShowExceptionWindow(new ExceptionViewModel(exception));
    }

    public void ShowExceptionWindow(ExceptionViewModel exception)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Debug.ThrowNotOnUiThread();
            System.Diagnostics.Debug.WriteLine(exception.ToString());

            if (exception.Message != _previousExceptionWindowMessage) // TODO - Improve to count same messages
            {
                var window = new MessageWindow { DataContext = exception };
                window.Show();
                _previousExceptionWindowMessage = exception.Message;
            }
        }, DispatcherPriority.Loaded);
    }

    public async Task ShowPrintDialogAsync(
        IPdfDocumentService documentService,
        int currentPage,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            Debug.ThrowNotOnUiThread();

            if (ActiveWindow is not { } w)
            {
                return;
            }

            var printService = App.Current?.Services?.GetService<IPrintService>();
            if (printService is null)
            {
                return;
            }

            var vm = new PrintDialogViewModel(printService, documentService, currentPage);
            var window = new PrintDialogWindow { DataContext = vm };
            await window.ShowDialog(w);
        }, DispatcherPriority.Loaded);
    }
}
