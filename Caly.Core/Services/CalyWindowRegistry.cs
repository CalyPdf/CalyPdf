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

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Caly.Core.Services.Interfaces;
using Caly.Core.ViewModels;
using Caly.Core.Views;

namespace Caly.Core.Services;

/// <summary>
/// One Caly window: its view model, its platform window (null on single-view lifetimes)
/// and whether it is the window created at startup.
/// </summary>
internal sealed class CalyWindowContext
{
    public required MainViewModel ViewModel { get; init; }

    public Window? Window { get; init; }

    public bool IsPrimary { get; init; }

    /// <summary>
    /// Read lazily: <see cref="MainWindow.NotificationManager"/> is only created in
    /// <c>OnLoaded</c>, which happens after the context is registered.
    /// </summary>
    public WindowNotificationManager? Notifications => (Window as MainWindow)?.NotificationManager;
}

/// <summary>
/// Tracks the live Caly windows. Document ownership is resolved by scanning rather than
/// cached: Tabalonia moves models between collections with bare Remove/Add, so a stored
/// owner would desync on every tab drag.
/// </summary>
internal sealed class CalyWindowRegistry : ICalyWindowRegistry
{
    private readonly List<CalyWindowContext> _windows = [];

    /// <inheritdoc cref="ICalyWindowRegistry.DocumentsOrphaned" />
    public event EventHandler<IReadOnlyList<DocumentViewModel>>? DocumentsOrphaned;

    /// <inheritdoc cref="ICalyWindowRegistry.WindowRegistered" />
    public event EventHandler<CalyWindowContext>? WindowRegistered;

    private CalyWindowContext? _active;

    public IReadOnlyList<CalyWindowContext> Windows => _windows.AsReadOnly();

    public CalyWindowContext? Primary =>
        _windows.FirstOrDefault(w => w.IsPrimary) ?? _windows.FirstOrDefault();

    public CalyWindowContext? Active =>
        _active is not null && _windows.Contains(_active) ? _active : Primary;

    public CalyWindowContext? FindOwnerOf(DocumentViewModel? document)
    {
        if (document is null)
        {
            return null;
        }

        foreach (CalyWindowContext context in _windows)
        {
            if (context.ViewModel.PdfDocuments.Contains(document))
            {
                return context;
            }
        }

        return null;
    }

    public CalyWindowContext? FindContext(MainViewModel viewModel) =>
        _windows.FirstOrDefault(w => ReferenceEquals(w.ViewModel, viewModel));

    public void CloseWindowIfEmpty(MainViewModel viewModel)
    {
        if (ShouldCloseWhenEmpty(viewModel) && FindContext(viewModel)?.Window is { } window)
        {
            window.Close();
        }
    }

    /// <summary>
    /// Whether the window owning <paramref name="viewModel"/> should close now that it may be
    /// empty.
    /// <para>
    /// An empty window stays open only when it is the last one left - there it falls back to
    /// the splash screen, because closing it would exit the app
    /// (<c>ShutdownMode.OnLastWindowClose</c>) and leave the user with no way back in. The
    /// window created at startup gets no special treatment: once another window exists it
    /// closes when emptied, like any other.
    /// </para>
    /// </summary>
    internal bool ShouldCloseWhenEmpty(MainViewModel viewModel)
    {
        if (FindContext(viewModel) is not { } context)
        {
            return false;
        }

        return _windows.Count > 1 && context.ViewModel.PdfDocuments.Count == 0;
    }

    public void Register(CalyWindowContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_windows.Contains(context))
        {
            return;
        }

        _windows.Add(context);
        _active ??= context;

        if (context.Window is { } window)
        {
            window.Activated += OnWindowActivated;
            window.Closed += OnWindowClosed;
        }

        WindowRegistered?.Invoke(this, context);
    }

    /// <inheritdoc cref="ICalyWindowRegistry.RegisterWhenOpened" />
    public void RegisterWhenOpened(CalyWindowContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Nothing to wait for: a context with no window (single-view lifetimes) cannot be
        // abandoned, and one whose window is already up would never see another Opened.
        if (context.Window is not { IsVisible: false } window)
        {
            Register(context);
            return;
        }

        window.Opened += OnOpened;

        void OnOpened(object? sender, EventArgs e)
        {
            window.Opened -= OnOpened;
            Register(context);
        }
    }

    public void Unregister(CalyWindowContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!_windows.Remove(context))
        {
            return;
        }

        // Captured before disposing: these documents have just lost their only view.
        DocumentViewModel[] orphaned = [.. context.ViewModel.PdfDocuments];

        if (context.Window is { } window)
        {
            window.Activated -= OnWindowActivated;
            window.Closed -= OnWindowClosed;
        }

        if (ReferenceEquals(_active, context))
        {
            _active = null;
        }

        context.ViewModel.Dispose();

        if (orphaned.Length > 0)
        {
            DocumentsOrphaned?.Invoke(this, orphaned);
        }
    }

    /// <summary>
    /// Exposed for tests; production code goes through <see cref="OnWindowActivated"/>.
    /// </summary>
    internal void SetActive(CalyWindowContext context)
    {
        if (_windows.Contains(context))
        {
            _active = context;
        }
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        if (FindByWindow(sender) is { } context)
        {
            _active = context;
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (FindByWindow(sender) is { } context)
        {
            Unregister(context);
        }
    }

    private CalyWindowContext? FindByWindow(object? window) =>
        _windows.FirstOrDefault(w => ReferenceEquals(w.Window, window));
}
