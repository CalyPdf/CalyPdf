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
using Caly.Core.ViewModels;

namespace Caly.Core.Services.Interfaces;

/// <summary>
/// Tracks the live Caly windows and resolves which one owns a given document.
/// </summary>
internal interface ICalyWindowRegistry
{
    /// <summary>
    /// Raised after a window has been removed from the registry, carrying the documents it
    /// still held.
    /// <para>
    /// Those documents have just lost their only view. They must be unloaded, or they stay in
    /// the opened-files map forever and the app silently refuses to reopen them.
    /// </para>
    /// </summary>
    event EventHandler<IReadOnlyList<DocumentViewModel>>? DocumentsOrphaned;

    /// <summary>
    /// Raised after a window has been added to the registry.
    /// <para>
    /// Lets services that must see every window - rather than only the one created at startup -
    /// hook each one as it appears. Subscribers built after startup must also walk
    /// <see cref="Windows"/> once, since windows already registered raise nothing.
    /// </para>
    /// </summary>
    event EventHandler<CalyWindowContext>? WindowRegistered;

    /// <summary>
    /// The window created at startup, or <c>null</c> once every window has closed.
    /// </summary>
    CalyWindowContext? Primary { get; }

    /// <summary>
    /// Most recently activated window; falls back to <see cref="Primary"/>, and to <c>null</c>
    /// once every window has closed.
    /// <para>
    /// Nullable rather than throwing: the last window closing is an ordinary user action, and
    /// callers reach for this from teardown paths - notifications, the bring-to-front pipe
    /// command, a queued open draining - where an exception would be raised exactly when
    /// nothing is left to report it to.
    /// </para>
    /// </summary>
    CalyWindowContext? Active { get; }

    IReadOnlyList<CalyWindowContext> Windows { get; }

    /// <summary>
    /// The window whose <see cref="MainViewModel.PdfDocuments"/> currently contains
    /// <paramref name="document"/>, or <c>null</c>. Resolved by scanning, never cached:
    /// Tabalonia moves models between collections with bare Remove/Add.
    /// </summary>
    CalyWindowContext? FindOwnerOf(DocumentViewModel? document);

    CalyWindowContext? FindContext(MainViewModel viewModel);

    /// <summary>
    /// Closes the window owning <paramref name="viewModel"/> if it has no documents left.
    /// <para>
    /// Call this only from a path that carries the intent to close - Tabalonia's
    /// <c>LastTabClosedAction</c> or Caly's own close-tab path. It must never be driven by
    /// the collection going empty: while a tab is dragged onto another window's strip
    /// Tabalonia empties the floating window's collection and merely <b>hides</b> the window,
    /// so it can show it again if the drag comes back out.
    /// </para>
    /// </summary>
    void CloseWindowIfEmpty(MainViewModel viewModel);

    void Register(CalyWindowContext context);

    /// <summary>
    /// Registers <paramref name="context"/> only once its window actually opens.
    /// <para>
    /// A window built for Tabalonia can still be abandoned after it has been asked for:
    /// <c>DetachItemToNewWindow</c> calls the host factory first, then gives up without showing
    /// anything if the returned strip has no <c>IList</c> items source. A context for a window
    /// that never opens would never come back out - it has no <c>Closed</c> event to fire - so
    /// it would sit in the registry making <see cref="Windows"/> look one larger than it is,
    /// and the last real window would close itself on its last tab instead of falling back to
    /// the splash screen.
    /// </para>
    /// </summary>
    void RegisterWhenOpened(CalyWindowContext context);

    void Unregister(CalyWindowContext context);
}
