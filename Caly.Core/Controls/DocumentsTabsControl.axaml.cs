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
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Core.Services.Interfaces;
using Caly.Core.ViewModels;
using Caly.Core.Views;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Tabalonia.Controls;

namespace Caly.Core.Controls;

/// <summary>
/// Hosts the tab strip of open PDF documents. Each tab's UI lives in
/// <see cref="DocumentTabView"/>.
/// </summary>
public sealed partial class DocumentsTabsControl : UserControl
{
    public DocumentsTabsControl()
    {
        InitializeComponent();

        // x:Name in the same XAML file generates a compile-checked field.
        ConfigureTabsControl(PART_TabsControl);
    }

    /// <summary>
    /// This control's tab strip, with the templates, commands and selection binding declared in
    /// <c>DocumentsTabsControl.axaml</c>. A torn-off tab is handed to the strip of the window it
    /// lands in, so that window behaves exactly like any other.
    /// </summary>
    internal TabsControl TabsControl => PART_TabsControl;

    private static void ConfigureTabsControl(TabsControl tabsControl)
    {
        bool canDetach = Globals.IsDesktopLifetime();

        tabsControl.EnableTabDetaching = canDetach;
        tabsControl.EnableTabAttaching = canDetach;
        tabsControl.DetachedHostFactory = canDetach ? CreateDetachedHost : null;

        // Tabalonia raises this only when it genuinely wants the source window gone. While a
        // tab is dragged onto another window's strip it passes suppressEmptySourceAction: true
        // and merely *hides* the floating window, so it can Show() it again if the drag comes
        // back out - which is why window closing must be driven by this event rather than by
        // the collection going empty.
        tabsControl.LastTabClosedAction = static (sender, _) =>
        {
            if (sender is TabsControl { DataContext: MainViewModel viewModel } &&
                App.Current?.Services?.GetService<ICalyWindowRegistry>() is { } registry)
            {
                registry.CloseWindowIfEmpty(viewModel);
            }
        };

        // Live crash: "TabPreviewControl already has a visual parent... while trying to add it
        // as a child" from ToolTip.Open, hit by rapidly sweeping the pointer across tabs. The
        // base tabalonia|DragTabItem style's ToolTip.Tip Setter materialises one TabPreviewControl
        // per tab and ToolTip.IsOpenChanged then caches and reuses that same instance (and its
        // ToolTip wrapper) across every hover, closing then immediately reopening it when a fast
        // sweep re-hovers a tab within ToolTip.BetweenShowDelay. A control that has only just
        // begun detaching from its closing popup can still be attached when that reopen tries to
        // reparent it. Handled here rather than removing the XAML Setter: ToolTipOpeningEvent
        // fires - and a handler can still replace ToolTip.Tip - before IsOpenChanged reads it, so
        // a control that has never been parented is what actually opens each time.
        tabsControl.ContainerPrepared += (_, e) =>
        {
            if (e.Container is DragTabItem tabItem)
            {
                ToolTip.AddToolTipOpeningHandler(tabItem, OnTabTooltipOpening);
            }
        };

        tabsControl.ContainerClearing += (_, e) =>
        {
            if (e.Container is DragTabItem tabItem)
            {
                ToolTip.RemoveToolTipOpeningHandler(tabItem, OnTabTooltipOpening);
            }
        };
    }

    /// <summary>
    /// Swaps in a fresh <see cref="TabPreviewControl"/> right before the tooltip opens, so the
    /// popup never reattaches an instance that may still be detaching from a previous one.
    /// Skipped for the selected tab, whose <c>ToolTip.Tip</c> is a plain file-name string (see
    /// the <c>:selected</c> style below) that this must not clobber.
    /// <para>
    /// <see cref="AvaloniaObject.SetCurrentValue(AvaloniaProperty, object?)"/>, not
    /// <see cref="ToolTip.SetTip"/>: that sets a local value, which outranks every style -
    /// including the <c>:selected</c> style below - and would permanently pin this tab's tip to
    /// a <see cref="TabPreviewControl"/> even after it becomes selected. SetCurrentValue updates
    /// today's effective value without acquiring that priority, so the base style's Setter (and
    /// the higher-priority <c>:selected</c> one, when it applies) still govern normally.
    /// </para>
    /// </summary>
    private static void OnTabTooltipOpening(object? sender, CancelRoutedEventArgs e)
    {
        if (sender is Control control && ToolTip.GetTip(control) is TabPreviewControl)
        {
            control.SetCurrentValue(ToolTip.TipProperty, new TabPreviewControl());
        }
    }

    /// <summary>
    /// Builds the window a torn-off tab moves into, and hands Tabalonia that window's own tab
    /// strip.
    /// <para>
    /// Returning a XAML-built strip is the point: it already carries the item and content
    /// templates, the add and close commands, and the selection binding, all compiled. The
    /// alternative - letting Tabalonia build a bare strip and re-applying that in code - meant
    /// duplicating what the XAML already declares, and code-built bindings resolve their paths
    /// by reflection, which Native AOT trimming strips.
    /// </para>
    /// <para>
    /// Runs before the tab leaves its old strip, so returning <c>null</c> on failure leaves the
    /// tab where it was and lets Tabalonia fall back to its own plain window.
    /// </para>
    /// </summary>
    private static (TabsControl Host, Window Window)? CreateDetachedHost(TabsControl sourceTabsControl)
    {
        try
        {
            IServiceProvider services = App.Current?.Services
                ?? throw new InvalidOperationException("Services are not available.");

            var registry = services.GetRequiredService<ICalyWindowRegistry>();

            // The strip the tab is leaving, so its window is known exactly
            var sourceViewModel = sourceTabsControl.DataContext as MainViewModel;
            Window? sourceWindow = sourceViewModel is not null
                ? registry.FindContext(sourceViewModel)?.Window
                : null;

            var viewModel = new MainViewModel()
            {
                // Inherit the sidebar
                IsDocumentPaneOpen = sourceViewModel?.IsDocumentPaneOpen ?? true,
                PaneSize = sourceViewModel?.PaneSize
                           ?? services.GetService<ISettingsService>()?.GetSettings().PaneSize
                           ?? CalySettings.Default.PaneSize
            };

            var window = new MainWindow
            {
                DataContext = viewModel,
                // Tabalonia assigns Position after this returns; CenterScreen would override it.
                WindowStartupLocation = WindowStartupLocation.Manual,
                Width = sourceWindow?.Bounds.Width is > 0 ? sourceWindow.Bounds.Width : 900,
                Height = sourceWindow?.Bounds.Height is > 0 ? sourceWindow.Bounds.Height : 600
            };

            // Deferred to the window actually opening, because Tabalonia asks for the host
            // before it commits: it can still bail out after this returns, leaving a window it
            // never shows and never closes. Registering that eagerly left a context nothing
            // could ever remove. Show() happens before anything consults the registry - the
            // selection change the transfer raises is posted, not synchronous.
            registry.RegisterWhenOpened(new CalyWindowContext
            {
                ViewModel = viewModel,
                Window = window,
                IsPrimary = false
            });

            return (window.TabsControl, window);
        }
        catch (Exception ex)
        {
            Debug.WriteExceptionToFile(ex);
            App.Messenger.Send(new ShowNotificationMessage(NotificationType.Error,
                "Could not detach tab", "The tab was left where it was."));

            return null;
        }
    }
}
