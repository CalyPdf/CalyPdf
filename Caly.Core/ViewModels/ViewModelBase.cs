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

using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Caly.Core.Services;

namespace Caly.Core.ViewModels;

public partial class ViewModelBase : ObservableObject
{
    [ObservableProperty] private ExceptionViewModel? _exception;

    /// <summary>
    /// The window an error raised on this view model belongs to, or <c>null</c> when it cannot
    /// be told - the notification then goes to the window the user is working in.
    /// <para>
    /// Without this an error about a document reported itself in whichever window happened to
    /// be active, which is not the same window the document is in: a file dropped on an
    /// unfocused window fails there, not where the focus is.
    /// </para>
    /// </summary>
    private protected virtual MainViewModel? NotificationTarget => null;

    partial void OnExceptionChanging(ExceptionViewModel? value)
    {
        // Every caller assigns this from a dispatcher callback, so this runs on the UI thread -
        // which is also the only thread the window registry may be read from.
        MainViewModel? target = NotificationTarget;

        App.Messenger.Send(new ShowNotificationMessage(NotificationType.Error, "Critical error",
            value?.Message, target));
    }
}