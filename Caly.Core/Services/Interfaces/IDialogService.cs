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
using Caly.Core.Models;
using Caly.Core.ViewModels;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Caly.Core.Services.Interfaces
{
    public interface IDialogService
    {
        /// <summary>
        /// Show a notification in <paramref name="target"/>, or in the active window when the
        /// caller has no particular window in mind.
        /// <para>
        /// Callers that know which window the notification is about must say so: a drop, for
        /// one, does not activate the window it lands on, so "the active window" would report
        /// the failure in a window the user was not interacting with.
        /// </para>
        /// </summary>
        void ShowNotification(string? title, string? message, NotificationType type, MainViewModel? target = null);

        /// <inheritdoc cref="ShowNotification(string?, string?, NotificationType, MainViewModel?)" />
        void ShowNotification(CalyNotification notification, MainViewModel? target = null);

        /// <summary>
        /// Show an exception in a popup window.
        /// </summary>
        Task ShowExceptionWindowAsync(Exception exception);

        /// <summary>
        /// Show an exception in a popup window.
        /// </summary>
        Task ShowExceptionWindowAsync(ExceptionViewModel exception);

        /// <summary>
        /// Show an exception in a popup window.
        /// </summary>
        void ShowExceptionWindow(Exception exception);

        /// <summary>
        /// Show an exception in a popup window.
        /// </summary>
        void ShowExceptionWindow(ExceptionViewModel exception);

        /// <summary>
        /// Open the print dialog for the given document, pre-selecting
        /// <paramref name="currentPage"/> as the "current page" option.
        /// </summary>
        Task ShowPrintDialogAsync(
            IPdfDocumentService documentService,
            int currentPage,
            CancellationToken token);
    }
}
