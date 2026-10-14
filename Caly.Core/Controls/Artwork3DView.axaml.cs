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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Caly.Avalonia.Scene3D;
using Caly.Core.Models;
using Caly.Core.Services.Rendering;
using System;
using System.Diagnostics;
using System.Windows.Input;

namespace Caly.Core.Controls;

/// <summary>
/// A piece of 3D artwork being viewed in place on its page: the 3D control over the artwork's rectangle
/// and a small toolbar (authored views, reset, close) at its corner.
/// <para>
/// <see cref="PageItem"/> builds one while its page has an active artwork and drops it when the page
/// leaves the screen; the 3D control frees its GPU memory when dropped. What is shown comes from the
/// data context, the artwork's view model, through bindings; this class adds what is the view's own
/// business: where the control sits on a zoomed page, Caly's gestures, and the keyboard focus.
/// </para>
/// </summary>
public sealed partial class Artwork3DView : UserControl
{
    /// <summary>Defines the <see cref="VisibleArea"/> property.</summary>
    public static readonly StyledProperty<Rect?> VisibleAreaProperty =
        AvaloniaProperty.Register<Artwork3DView, Rect?>(nameof(VisibleArea));

    /// <summary>Defines the <see cref="ZoomLevel"/> property.</summary>
    public static readonly StyledProperty<double> ZoomLevelProperty =
        AvaloniaProperty.Register<Artwork3DView, double>(nameof(ZoomLevel), 1.0);

    /// <summary>Defines the <see cref="StatusCommand"/> property.</summary>
    public static readonly StyledProperty<ICommand?> StatusCommandProperty =
        AvaloniaProperty.Register<Artwork3DView, ICommand?>(nameof(StatusCommand));

    /// <summary>Defines the <see cref="ShowAxisGizmo"/> property.</summary>
    public static readonly StyledProperty<bool> ShowAxisGizmoProperty =
        AvaloniaProperty.Register<Artwork3DView, bool>(nameof(ShowAxisGizmo), true);

    private readonly Scene3DControl _view3D;
    private readonly Artwork3DPlacementPanel _panel;

    // Only with the render-timings debug setting on: when this view was built, until its model shows.
    private long _builtAt;

    public Artwork3DView()
    {
        InitializeComponent();

        _panel = this.FindControl<Artwork3DPlacementPanel>("PART_Panel")!;
        _view3D = this.FindControl<Scene3DControl>("PART_Scene3D")!;

        // The page's gestures come first. Its command key (Ctrl, or Cmd on macOS) pans and zooms the
        // page, also over a 3D view, so the model is panned with Shift; and Home is the document's.
        _view3D.ReservedModifiers = Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;
        _view3D.PanModifiers = KeyModifiers.Shift;
        _view3D.ResetKey = null;

        // Until there is a model to draw, the page (the artwork's poster) shows through.
        _view3D.DefaultBackground = _view3D.Scene is null ? Colors.Transparent : Colors.White;
        _view3D.PropertyChanged += OnView3DPropertyChanged;

        if (RenderTimings.IsEnabled)
        {
            _builtAt = Stopwatch.GetTimestamp();
            _view3D.FrameRendered += (_, _) =>
            {
                if (_view3D.Status != Scene3DStatus.Ready)
                {
                    return;
                }

                if (_builtAt != 0)
                {
                    RenderTimings.RecordView3DShown(Stopwatch.GetTimestamp() - _builtAt);
                    _builtAt = 0;
                }

                RenderTimings.RecordView3DFrame(_view3D.Statistics.FrameMilliseconds);
            };
        }

        this.FindControl<Button>("PART_Reset")!.Click += OnResetClick;
        this.FindControl<ComboBox>("PART_Views")!.DropDownClosed += OnViewsDropDownClosed;
    }

    /// <summary>The part of the page on screen, in page units; null when none is.</summary>
    public Rect? VisibleArea
    {
        get => GetValue(VisibleAreaProperty);
        set => SetValue(VisibleAreaProperty, value);
    }

    /// <summary>The page zoom.</summary>
    public double ZoomLevel
    {
        get => GetValue(ZoomLevelProperty);
        set => SetValue(ZoomLevelProperty, value);
    }

    /// <summary>
    /// Executed with an <see cref="Artwork3DControlStatus"/> each time the 3D control's status changes
    /// while this view is on a page.
    /// </summary>
    public ICommand? StatusCommand
    {
        get => GetValue(StatusCommandProperty);
        set => SetValue(StatusCommandProperty, value);
    }

    /// <summary>Gets or sets whether the 3D axis gizmo is shown.</summary>
    public bool ShowAxisGizmo
    {
        get => GetValue(ShowAxisGizmoProperty);
        set => SetValue(ShowAxisGizmoProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // Before InitializeComponent has run there is no panel yet; the constructor's values are the
        // properties' defaults, which are the panel's too.
        if (_panel is null)
        {
            return;
        }

        if (change.Property == VisibleAreaProperty)
        {
            _panel.VisibleArea = VisibleArea;
        }
        else if (change.Property == ZoomLevelProperty)
        {
            _panel.ZoomLevel = ZoomLevel;
        }
    }

    private void OnView3DPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Scene3DControl.SceneProperty)
        {
            _view3D.DefaultBackground = _view3D.Scene is null ? Colors.Transparent : Colors.White;
        }
        else if (e.Property == Scene3DControl.StatusProperty)
        {
            // The status changes while a frame is drawn, and acting on it may take this view off its
            // page: not from inside the handler. And once off the page, what the control reports (it
            // loses its GPU objects there) concerns nobody.
            Scene3DStatus status = _view3D.Status;
            Dispatcher.UIThread.Post(() =>
            {
                if (!this.IsAttachedToVisualTree() || StatusCommand is not { } command)
                {
                    return;
                }

                var parameter = new Artwork3DControlStatus(status, _view3D.Status == status ? _view3D.StatusMessage : null);
                if (command.CanExecute(parameter))
                {
                    command.Execute(parameter);
                }
            });
        }
    }

    private void OnResetClick(object? sender, RoutedEventArgs e)
    {
        _view3D.ResetView();
        _view3D.Focus();
    }

    private void OnViewsDropDownClosed(object? sender, EventArgs e)
    {
        // A view was chosen: the pointer goes back to the model, and so does the wheel.
        _view3D.Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // The wheel zooms the model only while the 3D control has the focus. Escape hands the focus
        // back to what holds the page (the pages control), as a click outside the view does.
        if (e.Key == Key.Escape && !e.Handled && _view3D.IsFocused)
        {
            foreach (Visual ancestor in this.GetVisualAncestors())
            {
                if (ancestor is InputElement { Focusable: true, IsEffectivelyEnabled: true } element && element.Focus())
                {
                    break;
                }
            }

            e.Handled = true;
        }
    }
}
