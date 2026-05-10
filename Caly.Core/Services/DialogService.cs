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
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
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
    private readonly Visual _target;
    private readonly TimeSpan _minDelay = TimeSpan.FromSeconds(3);

    private string? _previousNotificationMessage;
    private DateTime _previousNotificationTime = DateTime.MinValue;
    private string? _previousExceptionWindowMessage;
    private DateTime _previousExceptionWindowTime = DateTime.MinValue;

    private WindowNotificationManager? _windowNotificationManager;

    public DialogService(Visual target)
    {
        _target = target;
        if (_target is Window w)
        {
            w.Loaded += _window_Loaded;
        }
    }

    private void _window_Loaded(object? sender, RoutedEventArgs e)
    {
        if (_target is Window w)
        {
            w.Loaded -= _window_Loaded;
        }

        if (sender is MainWindow mw)
        {
            _windowNotificationManager = mw.NotificationManager;
            System.Diagnostics.Debug.Assert(_windowNotificationManager is not null);
        }
        else
        {
            throw new InvalidOperationException($"Expecting '{typeof(MainWindow)}' but got '{sender?.GetType()}'.");
        }
    }

    public async Task<string?> ShowPdfPasswordDialogAsync()
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_target is Window w)
            {
                return new PdfPasswordWindow().ShowDialog<string?>(w);
            }
            return Task.FromResult<string?>(string.Empty);
        });
    }

    public void ShowNotification(CalyNotification notification)
    {
        ShowNotification(notification.Title, notification.Message, notification.Type);
    }

    public void ShowNotification(string? title, string? message, NotificationType type)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Debug.ThrowNotOnUiThread();
            System.Diagnostics.Debug.WriteLine($"Annotation ({type}): {title}\n{message}");
            if (_windowNotificationManager is not null)
            {
                DateTime now = DateTime.UtcNow;
                if (string.IsNullOrEmpty(message) ||
                    (now - _previousNotificationTime <= _minDelay &&
                    message.Equals(_previousNotificationMessage)))
                {
                    return;
                }

                _previousNotificationTime = now;
                _previousNotificationMessage = message;
                _windowNotificationManager.Show(new Notification(title, message, type, _annotationExpiration));
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
            if (_target is not Window w)
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

            if (_target is not Window w)
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
