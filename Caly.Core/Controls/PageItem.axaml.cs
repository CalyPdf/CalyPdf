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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Caly.Core.Utilities;
using Caly.Core.ViewModels;
using SkiaSharp;
using System;

namespace Caly.Core.Controls;

/// <summary>
/// Control that represents a single page in a PDF document.
/// </summary>
[TemplatePart("PART_PageInteractiveLayerControl", typeof(PageInteractiveLayerControl))]
public sealed class PageItem : ContentControl
{
    /// <summary>
    /// Defines the <see cref="IsPageRendering"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsPageRenderingProperty =
        AvaloniaProperty.Register<PageItem, bool>(nameof(IsPageRendering));

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

    static PageItem()
    {
        AffectsRender<PageItem>(PictureProperty, IsPageVisibleProperty,
            WidthProperty, HeightProperty);
    }

    /// <summary>
    /// Raised synchronously just before a page rotation action executes, so the view
    /// can capture its scroll state before the layout changes.
    /// </summary>
    public event EventHandler? BeforeRotation;

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

    public ExceptionViewModel? Exception
    {
        get => GetValue(ExceptionProperty);
        set => SetValue(ExceptionProperty, value);
    }

    /// <summary>
    /// Gets the interactive layer. Set when the template is applied.
    /// </summary>
    public PageInteractiveLayerControl? InteractiveLayer { get; private set; }

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
    }
    
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsRotatingProperty)
        {
            if (!change.GetOldValue<bool>() && change.GetNewValue<bool>())
            {
                BeforeRotation?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
