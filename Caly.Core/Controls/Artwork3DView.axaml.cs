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
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Reactive;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Caly.Core.Models;
using Caly.Core.Services.Rendering;
using Caly.Rendering.Scene3D;
using Caly.Scene3D.Avalonia;
using System;
using System.Diagnostics;
using System.Windows.Input;

namespace Caly.Core.Controls;

/// <summary>
/// A piece of 3D artwork being viewed in place on its page: the 3D control over the artwork's rectangle
/// and a small toolbar (authored views, animation, reset, close) at its corner.
/// <para>
/// <see cref="PageItem"/> builds one while its page has an active artwork and drops it when the page
/// leaves the screen; the 3D control frees its GPU memory when dropped. What is shown comes from the
/// data context, the artwork's view model, through bindings; this class adds what is the view's own
/// business: Caly's gestures, the keyboard focus, and telling the view model what the control reports.
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

    /// <summary>Defines the <see cref="CameraState"/> property.</summary>
    public static readonly StyledProperty<Scene3DCameraState?> CameraStateProperty =
        AvaloniaProperty.Register<Artwork3DView, Scene3DCameraState?>(nameof(CameraState), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// The 3D control's background for its scene: transparent while there is none, so that the page (the
    /// artwork's poster) shows through until the model is read; white once there is one.
    /// </summary>
    public static readonly IValueConverter BackgroundForScene =
        new FuncValueConverter<Scene?, Color>(scene => scene is null ? Colors.Transparent : Colors.White);

    // Only with the render-timings debug setting on: when this view was built, until its model shows.
    private long _builtAt;

    // The camera the previous view of this artwork was left with, until this one has drawn with it.
    private Scene3DCameraState? _cameraToRestore;
    private bool _isLeaving;

    public Artwork3DView()
    {
        InitializeComponent();

        // The page's gestures come first. Its command key (Ctrl, or Cmd on macOS) pans and zooms the
        // page, also over a 3D view, so the model is panned with Shift (set in XAML), and Home is the
        // document's. The command key depends on the platform, hence here.
        View3D.ReservedModifiers = Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;

        View3D.GetPropertyChangedObservable(Scene3DControl.StatusProperty)
            .Subscribe(new AnonymousObserver<AvaloniaPropertyChangedEventArgs>(_ => OnStatusChanged()));

        // A new scene or view resets the control's camera (its own handler runs first). While this view is
        // being built the scene comes twice (the one whose geometry went to the previous control, then
        // the one read again) and the view may come after it: each time, the camera is put back.
        View3D.GetPropertyChangedObservable(Scene3DControl.SceneProperty)
            .Subscribe(new AnonymousObserver<AvaloniaPropertyChangedEventArgs>(_ => RestoreCamera()));
        View3D.GetPropertyChangedObservable(Scene3DControl.ViewProperty)
            .Subscribe(new AnonymousObserver<AvaloniaPropertyChangedEventArgs>(_ => RestoreCamera()));
        View3D.FrameRendered += OnFirstFramesRendered;

        if (RenderTimings.IsEnabled)
        {
            _builtAt = Stopwatch.GetTimestamp();
            View3D.FrameRendered += OnFrameRendered;
        }
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
    /// while this view is on a page, and once more when it leaves the page.
    /// </summary>
    public ICommand? StatusCommand
    {
        get => GetValue(StatusCommandProperty);
        set => SetValue(StatusCommandProperty, value);
    }

    /// <summary>
    /// The camera and animation playhead the user left the view with; written when this view leaves the
    /// page, read by the view built when the page comes back (a tab switch, a scroll away and back).
    /// </summary>
    public Scene3DCameraState? CameraState
    {
        get => GetValue(CameraStateProperty);
        set => SetValue(CameraStateProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CameraStateProperty && !_isLeaving)
        {
            // Given by the binding as this view is built: to be put back once the control has its scene.
            _cameraToRestore = CameraState;
            RestoreCamera();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // The next view of this artwork starts where this one is left; or, if this one never drew with
        // the camera it was given, from that camera.
        _isLeaving = true;
        SetCurrentValue(CameraStateProperty, _cameraToRestore ?? View3D.CaptureCameraState());

        // What the control said while on the page no longer stands; and what it says now that it is off
        // (it loses its GPU objects) concerns nobody.
        Execute(Artwork3DControlStatus.LeftPage);
        base.OnDetachedFromVisualTree(e);
    }

    private void RestoreCamera()
    {
        if (_cameraToRestore is { } state)
        {
            View3D.RestoreCameraState(state);
        }
    }

    private void OnFirstFramesRendered(object? sender, EventArgs e)
    {
        // Once the model is drawn the camera is the user's again: a view chosen from now on starts afresh.
        if (View3D.Status == Scene3DStatus.Ready)
        {
            _cameraToRestore = null;
            View3D.FrameRendered -= OnFirstFramesRendered;
        }
    }

    private void OnStatusChanged()
    {
        // The status changes while a frame is drawn, and acting on it may take this view off its page:
        // not from inside the handler. And once off the page, what the control reports concerns nobody.
        Scene3DStatus status = View3D.Status;
        Dispatcher.UIThread.Post(() =>
        {
            if (this.IsAttachedToVisualTree())
            {
                Execute(new Artwork3DControlStatus(status, View3D.Status == status ? View3D.StatusMessage : null));
            }
        });
    }

    private void Execute(Artwork3DControlStatus status)
    {
        if (StatusCommand is { } command && command.CanExecute(status))
        {
            command.Execute(status);
        }
    }

    private void OnFrameRendered(object? sender, EventArgs e)
    {
        if (View3D.Status != Scene3DStatus.Ready)
        {
            return;
        }

        if (_builtAt != 0)
        {
            RenderTimings.RecordView3DShown(Stopwatch.GetTimestamp() - _builtAt);
            _builtAt = 0;
        }

        RenderTimings.RecordView3DFrame(View3D.Statistics.FrameMilliseconds);
    }

    private void OnPlayPauseClick(object? sender, RoutedEventArgs e)
    {
        if (View3D.IsAnimationPlaying)
        {
            View3D.PauseAnimation();
        }
        else
        {
            View3D.PlayAnimation();
        }

        View3D.Focus();
    }

    private void OnRestartAnimationClick(object? sender, RoutedEventArgs e)
    {
        View3D.RestartAnimation();
        View3D.Focus();
    }

    private void OnResetClick(object? sender, RoutedEventArgs e)
    {
        View3D.ResetView();
        View3D.Focus();
    }

    private void OnViewListDropDownClosed(object? sender, EventArgs e)
    {
        // A view was chosen: the pointer goes back to the model, and so does the wheel.
        View3D.Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // The wheel zooms the model only while the 3D control has the focus. Escape hands the focus
        // back to what holds the page (the pages control), as a click outside the view does.
        if (e.Key == Key.Escape && !e.Handled && View3D.IsFocused)
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
