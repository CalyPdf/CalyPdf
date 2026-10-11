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
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Caly.Core.Utilities;
using Caly.Core.ViewModels;
using SkiaSharp;
using System;

namespace Caly.Core.Controls;

/// <summary>
/// Control that represents a single page in a PDF document.
/// </summary>
[TemplatePart("PART_PageInteractiveLayerControl", typeof(PageInteractiveLayerControl))]
[TemplatePart("PART_PageLoadingHost", typeof(Panel))]
[TemplatePart("PART_Artwork3DHost", typeof(Panel))]
public sealed class PageItem : ContentControl
{
    /// <summary>
    /// Defines the <see cref="IsPageLoading"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsPageLoadingProperty =
        AvaloniaProperty.Register<PageItem, bool>(nameof(IsPageLoading));

    /// <summary>
    /// Defines the <see cref="Picture"/> property.
    /// </summary>
    public static readonly StyledProperty<IRef<SKPicture>?> PictureProperty =
        AvaloniaProperty.Register<PageItem, IRef<SKPicture>?>(nameof(Picture),
            defaultBindingMode: BindingMode.OneWay);

    /// <summary>
    /// Defines the <see cref="IsPageVisible"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsPageVisibleProperty =
        AvaloniaProperty.Register<PageItem, bool>(nameof(IsPageVisible));

    /// <summary>
    /// Defines the <see cref="VisibleArea"/> property.
    /// </summary>
    public static readonly StyledProperty<Rect?> VisibleAreaProperty =
        AvaloniaProperty.Register<PageItem, Rect?>(nameof(VisibleArea),
            defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Defines the <see cref="Exception"/> property.
    /// </summary>
    public static readonly StyledProperty<ExceptionViewModel?> ExceptionProperty =
        AvaloniaProperty.Register<PageItem, ExceptionViewModel?>(nameof(Exception),
            defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Defines the <see cref="Rotation"/> property.
    /// </summary>
    public static readonly StyledProperty<int> RotationProperty =
        AvaloniaProperty.Register<PageItem, int>(nameof(Rotation));

    /// <summary>
    /// Defines the <see cref="IsRotating"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsRotatingProperty =
        AvaloniaProperty.Register<PageItem, bool>(nameof(IsRotating));

    /// <summary>
    /// Defines the <see cref="ZoomLevel"/> property.
    /// </summary>
    public static readonly StyledProperty<double> ZoomLevelProperty =
        AvaloniaProperty.Register<PageItem, double>(nameof(ZoomLevel), 1.0);

    /// <summary>
    /// Defines the <see cref="ActiveArtwork3D"/> property.
    /// </summary>
    public static readonly StyledProperty<object?> ActiveArtwork3DProperty =
        AvaloniaProperty.Register<PageItem, object?>(nameof(ActiveArtwork3D));

    static PageItem()
    {
        AffectsRender<PageItem>(PictureProperty, IsPageVisibleProperty,
            WidthProperty, HeightProperty);
    }

    /// <summary>
    /// Defines the <see cref="BeforeRotation"/> event.
    /// </summary>
    public static readonly RoutedEvent<RoutedEventArgs> BeforeRotationEvent =
        RoutedEvent.Register<PageItem, RoutedEventArgs>(nameof(BeforeRotation), RoutingStrategies.Bubble);

    /// <summary>
    /// Raised synchronously just before a page rotation action executes, so the view
    /// can capture its scroll state before the layout changes. Bubbles, so ancestors
    /// (e.g. <see cref="PageItemsControl"/>) can subscribe once instead of wiring each
    /// container across load/unload cycles.
    /// </summary>
    public event EventHandler<RoutedEventArgs>? BeforeRotation
    {
        add => AddHandler(BeforeRotationEvent, value);
        remove => RemoveHandler(BeforeRotationEvent, value);
    }

    public int Rotation
    {
        get => GetValue(RotationProperty);
        set => SetValue(RotationProperty, value);
    }

    public bool IsRotating
    {
        get => GetValue(IsRotatingProperty);
        set => SetValue(IsRotatingProperty, value);
    }

    public bool IsPageLoading
    {
        get => GetValue(IsPageLoadingProperty);
        set => SetValue(IsPageLoadingProperty, value);
    }

    public IRef<SKPicture>? Picture
    {
        get => GetValue(PictureProperty);
        set => SetValue(PictureProperty, value);
    }

    public bool IsPageVisible
    {
        get => GetValue(IsPageVisibleProperty);
        set => SetValue(IsPageVisibleProperty, value);
    }

    public Rect? VisibleArea
    {
        get => GetValue(VisibleAreaProperty);
        set => SetValue(VisibleAreaProperty, value);
    }

    public ExceptionViewModel? Exception
    {
        get => GetValue(ExceptionProperty);
        set => SetValue(ExceptionProperty, value);
    }

    /// <summary>
    /// The zoom the page is shown at. Only what is laid out against the screen rather than the page
    /// needs it (the 3D view).
    /// </summary>
    public double ZoomLevel
    {
        get => GetValue(ZoomLevelProperty);
        set => SetValue(ZoomLevelProperty, value);
    }

    /// <summary>
    /// The 3D artwork of this page that is being viewed in place, as the data context of its view;
    /// <c>null</c> when there is none, which is the case of nearly every page.
    /// </summary>
    public object? ActiveArtwork3D
    {
        get => GetValue(ActiveArtwork3DProperty);
        set => SetValue(ActiveArtwork3DProperty, value);
    }

    /// <summary>
    /// Gets the interactive layer. Set in OnApplyTemplate.
    /// </summary>
    public PageInteractiveLayerControl? InteractiveLayer { get; private set; }

    /// <summary>
    /// Host for the page-loading skeleton, and the skeleton currently inside it. The host comes
    /// from the template; the skeleton is built on demand and dropped when this container is
    /// recycled.
    /// </summary>
    private Panel? _pageLoadingHost;
    private PageLoadingSkeleton? _pageLoadingSkeleton;

    /// <summary>
    /// Host for the 3D view, and the view currently inside it. Like the skeleton, the view is built on
    /// demand and dropped when this container leaves the visual tree: it holds an OpenGL surface.
    /// </summary>
    private Panel? _artwork3DHost;
    private Artwork3DView? _artwork3DView;

    public PageItem()
    {
#if DEBUG
        if (Design.IsDesignMode)
        {
            // Only if in design mode
            DataContext = new PageViewModel();
        }
#endif
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        InteractiveLayer = e.NameScope.FindFromNameScope<PageInteractiveLayerControl>("PART_PageInteractiveLayerControl");
        // Optional: minimal templates (tests, design surfaces) need not provide the host.
        _pageLoadingHost = e.NameScope.Find<Panel>("PART_PageLoadingHost");
        SyncPageLoadingSkeleton();

        DropArtwork3DView();
        _artwork3DHost = e.NameScope.Find<Panel>("PART_Artwork3DHost");
        SyncArtwork3DView();
    }
    
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsPageLoadingProperty)
        {
            SyncPageLoadingSkeleton();
        }

        if (change.Property == ActiveArtwork3DProperty)
        {
            SyncArtwork3DView();
        }
        else if (_artwork3DView is not null)
        {
            if (change.Property == VisibleAreaProperty)
            {
                _artwork3DView.VisibleArea = VisibleArea;
            }
            else if (change.Property == ZoomLevelProperty)
            {
                _artwork3DView.ZoomLevel = ZoomLevel;
            }
        }

        if (change.Property == IsRotatingProperty)
        {
            if (!change.GetOldValue<bool>() && change.GetNewValue<bool>())
            {
                RaiseEvent(new RoutedEventArgs(BeforeRotationEvent));
            }
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // A pooled container can come back while its page is already loading, in which
        // case there is no IsPageLoading change left to build the skeleton off.
        SyncPageLoadingSkeleton();

        // Likewise a page that comes back on screen with its 3D artwork still being viewed.
        SyncArtwork3DView();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        // This container goes back in the pool to be reused for another page, possibly in another
        // document. The skeleton animates from its own frame loop, which it starts on attach, so it
        // would survive reuse, but it is cheap to rebuild and holding one costs a control tree per
        // idle container, so drop it and let SyncPageLoadingSkeleton rebuild it if needed.
        DisposePageLoadingSkeleton();

        // The 3D view frees its GPU memory by leaving the tree. Its artwork stays active: the view
        // is built again when the page comes back.
        DropArtwork3DView();
    }

    private void DropArtwork3DView()
    {
        if (_artwork3DView is null)
        {
            return;
        }

        _artwork3DHost?.Children.Remove(_artwork3DView);
        _artwork3DView.DataContext = null;
        _artwork3DView = null;
    }

    /// <summary>
    /// Shows the 3D view while this page has an artwork being viewed and is on screen, and takes it
    /// down otherwise.
    /// </summary>
    private void SyncArtwork3DView()
    {
        object? artwork = ActiveArtwork3D;
        if (_artwork3DHost is null || artwork is null || !this.IsAttachedToVisualTree())
        {
            DropArtwork3DView();
            return;
        }

        if (_artwork3DView is not null && ReferenceEquals(_artwork3DView.DataContext, artwork))
        {
            return;
        }

        DropArtwork3DView();
        _artwork3DView = new Artwork3DView
        {
            DataContext = artwork,
            VisibleArea = VisibleArea,
            ZoomLevel = ZoomLevel
        };
        _artwork3DHost.Children.Add(_artwork3DView);
    }

    private void DisposePageLoadingSkeleton()
    {
        _pageLoadingHost?.Children.Clear();
        _pageLoadingSkeleton = null;
    }

    /// <summary>
    /// Shows the fake page while the real one renders, and takes it down again afterward.
    /// </summary>
    /// <remarks>
    /// Built on demand rather than declared in the template so a container that is reused without
    /// ever rendering never pays for one, and so the skeleton's frame loop always starts fresh.
    /// </remarks>
    private void SyncPageLoadingSkeleton()
    {
        if (_pageLoadingHost is null)
        {
            // Minimal templates (tests, design surfaces) need not provide the host.
            return;
        }

        if (!IsPageLoading)
        {
            DisposePageLoadingSkeleton();
            return;
        }

        if (_pageLoadingSkeleton is not null)
        {
            return;
        }

        _pageLoadingSkeleton = new PageLoadingSkeleton();
        _pageLoadingHost.Children.Add(_pageLoadingSkeleton);
    }
}
