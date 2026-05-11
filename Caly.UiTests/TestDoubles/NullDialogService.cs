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

using Avalonia.Controls.Notifications;
using Caly.Core.Models;
using Caly.Core.Services.Interfaces;
using Caly.Core.ViewModels;
using System.Collections.Concurrent;

namespace Caly.UiTests.TestDoubles;

public sealed class NullDialogService : IDialogService
{
    private readonly ConcurrentQueue<Exception> _capturedExceptions = new();
    private readonly ConcurrentQueue<(string? Title, string? Message, NotificationType Type)> _notifications = new();
    private bool _acceptExceptions;

    public IReadOnlyList<Exception> CapturedExceptions => _capturedExceptions.ToArray();
    public IReadOnlyList<(string? Title, string? Message, NotificationType Type)> Notifications => _notifications.ToArray();

    public void Reset()
    {
        _capturedExceptions.Clear();
        _notifications.Clear();
        _acceptExceptions = false;
    }

    public void AcceptExceptions() => _acceptExceptions = true;

    public void Verify()
    {
        if (_acceptExceptions)
        {
            return;
        }
        
        if (_capturedExceptions.IsEmpty)
        {
            return;
        }
        
        var first = _capturedExceptions.First();
        throw new Xunit.Sdk.XunitException($"NullDialogService captured an unexpected exception during the test: {first}");
    }

    public Task<string?> ShowPdfPasswordDialogAsync() => Task.FromResult<string?>(null);

    public void ShowNotification(string? title, string? message, NotificationType type) =>
        _notifications.Enqueue((title, message, type));

    public void ShowNotification(CalyNotification notification) =>
        _notifications.Enqueue((notification.Title, notification.Message, notification.Type));

    public Task ShowExceptionWindowAsync(Exception exception)
    {
        _capturedExceptions.Enqueue(exception);
        return Task.CompletedTask;
    }

    public Task ShowExceptionWindowAsync(ExceptionViewModel exception)
    {
        if (exception.Exception is not null)
        {
            _capturedExceptions.Enqueue(exception.Exception);
        }
        
        return Task.CompletedTask;
    }

    public void ShowExceptionWindow(Exception exception) => _capturedExceptions.Enqueue(exception);

    public void ShowExceptionWindow(ExceptionViewModel exception)
    {
        if (exception.Exception is not null)
        {
            _capturedExceptions.Enqueue(exception.Exception);
        }
    }
}
