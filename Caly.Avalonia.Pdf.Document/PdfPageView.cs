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
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;
using Caly.Avalonia.Pdf.Document.Internal;
using Caly.Avalonia.Pdf.Rendering;
using Caly.Avalonia.Pdf.Rendering.Tiling;
using Caly.Avalonia.Pdf.Text;
using Caly.Pdf.Models;
using SkiaSharp;
using UglyToad.PdfPig.Core;

namespace Caly.Avalonia.Pdf.Document;

/// <summary>How a <see cref="PdfPageView"/> rasterises its page.</summary>
public enum PdfRenderMode
{
    /// <summary>Pre-rendered bitmap tiles via a background <see cref="TileRenderService"/>.</summary>
    Tiled,
    /// <summary>Direct <c>SKPicture</c> draw onto the canvas.</summary>
    Direct
}

/// <summary>
/// Control that represents a single page in a PDF document. Composes a render control and the
/// interactive text layer; driven by styled properties (no view-model coupling).
/// </summary>
[TemplatePart("PART_PageInteractiveLayerControl", typeof(PageInteractiveLayerControl))]
public sealed class PdfPageView : ContentControl
{
    public static readonly StyledProperty<bool> IsPageRenderingProperty =
        AvaloniaProperty.Register<PdfPageView, bool>(nameof(IsPageRendering));

    public static readonly StyledProperty<IRef<SKPicture>?> PictureProperty =
        AvaloniaProperty.Register<PdfPageView, IRef<SKPicture>?>(nameof(Picture),
            defaultBindingMode: BindingMode.OneWay);

    public static readonly StyledProperty<bool> IsPageVisibleProperty =
        AvaloniaProperty.Register<PdfPageView, bool>(nameof(IsPageVisible));

    public static readonly StyledProperty<Rect?> VisibleAreaProperty =
        AvaloniaProperty.Register<PdfPageView, Rect?>(nameof(VisibleArea),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<object?> ExceptionProperty =
        AvaloniaProperty.Register<PdfPageView, object?>(nameof(Exception),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<int> RotationProperty =
        AvaloniaProperty.Register<PdfPageView, int>(nameof(Rotation));

    public static readonly StyledProperty<bool> IsRotatingProperty =
        AvaloniaProperty.Register<PdfPageView, bool>(nameof(IsRotating));

    public static readonly StyledProperty<Size> PageSizeProperty =
        AvaloniaProperty.Register<PdfPageView, Size>(nameof(PageSize));

    public static readonly StyledProperty<double> PpiScaleProperty =
        AvaloniaProperty.Register<PdfPageView, double>(nameof(PpiScale));

    public static readonly StyledProperty<double> ZoomLevelProperty =
        AvaloniaProperty.Register<PdfPageView, double>(nameof(ZoomLevel), 1.0);

    public static readonly StyledProperty<TileRenderService?> TileRenderServiceProperty =
        AvaloniaProperty.Register<PdfPageView, TileRenderService?>(nameof(TileRenderService));

    public static readonly StyledProperty<PdfTextLayer?> PdfTextLayerProperty =
        AvaloniaProperty.Register<PdfPageView, PdfTextLayer?>(nameof(PdfTextLayer));

    public static readonly StyledProperty<IReadOnlyList<PdfRectangle>?> SelectedWordsProperty =
        AvaloniaProperty.Register<PdfPageView, IReadOnlyList<PdfRectangle>?>(nameof(SelectedWords));

    public static readonly StyledProperty<IReadOnlyList<PdfRectangle>?> SearchResultsProperty =
        AvaloniaProperty.Register<PdfPageView, IReadOnlyList<PdfRectangle>?>(nameof(SearchResults));

    public static readonly StyledProperty<int> PageNumberProperty =
        AvaloniaProperty.Register<PdfPageView, int>(nameof(PageNumber));

    public static readonly StyledProperty<PdfRenderMode> RenderModeProperty =
        AvaloniaProperty.Register<PdfPageView, PdfRenderMode>(nameof(RenderMode), PdfRenderMode.Tiled);

    static PdfPageView()
    {
        AffectsRender<PdfPageView>(PictureProperty, IsPageVisibleProperty,
            WidthProperty, HeightProperty);
    }

    /// <summary>Defines the <see cref="BeforeRotation"/> routed event.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> BeforeRotationEvent =
        RoutedEvent.Register<PdfPageView, RoutedEventArgs>(nameof(BeforeRotation), RoutingStrategies.Bubble);

    /// <summary>
    /// Raised synchronously just before a page rotation action executes, so the host
    /// can capture its scroll state before the layout changes.
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

    public bool IsPageRendering
    {
        get => GetValue(IsPageRenderingProperty);
        set => SetValue(IsPageRenderingProperty, value);
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

    /// <summary>Optional error payload to surface for this page (host-defined type).</summary>
    public object? Exception
    {
        get => GetValue(ExceptionProperty);
        set => SetValue(ExceptionProperty, value);
    }

    /// <summary>Page size in unscaled document coordinates.</summary>
    public Size PageSize
    {
        get => GetValue(PageSizeProperty);
        set => SetValue(PageSizeProperty, value);
    }

    public double PpiScale
    {
        get => GetValue(PpiScaleProperty);
        set => SetValue(PpiScaleProperty, value);
    }

    public double ZoomLevel
    {
        get => GetValue(ZoomLevelProperty);
        set => SetValue(ZoomLevelProperty, value);
    }

    public TileRenderService? TileRenderService
    {
        get => GetValue(TileRenderServiceProperty);
        set => SetValue(TileRenderServiceProperty, value);
    }

    public PdfTextLayer? PdfTextLayer
    {
        get => GetValue(PdfTextLayerProperty);
        set => SetValue(PdfTextLayerProperty, value);
    }

    public IReadOnlyList<PdfRectangle>? SelectedWords
    {
        get => GetValue(SelectedWordsProperty);
        set => SetValue(SelectedWordsProperty, value);
    }

    public IReadOnlyList<PdfRectangle>? SearchResults
    {
        get => GetValue(SearchResultsProperty);
        set => SetValue(SearchResultsProperty, value);
    }

    public int PageNumber
    {
        get => GetValue(PageNumberProperty);
        set => SetValue(PageNumberProperty, value);
    }

    public PdfRenderMode RenderMode
    {
        get => GetValue(RenderModeProperty);
        set => SetValue(RenderModeProperty, value);
    }

    /// <summary>
    /// Gets the interactive (text/selection) layer for this page.
    /// </summary>
    public PageInteractiveLayerControl? InteractiveLayer { get; set; }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        InteractiveLayer = e.NameScope.FindFromNameScope<PageInteractiveLayerControl>("PART_PageInteractiveLayerControl");
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsRotatingProperty)
        {
            if (!change.GetOldValue<bool>() && change.GetNewValue<bool>())
            {
                RaiseEvent(new RoutedEventArgs(BeforeRotationEvent));
            }
        }
    }
}
