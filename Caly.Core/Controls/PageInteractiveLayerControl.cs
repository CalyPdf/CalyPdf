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
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.VisualTree;
using Caly.Core.Utilities;
using Caly.Pdf.Models;
using System.Collections.Generic;
using System.Linq;
using UglyToad.PdfPig.Core;

namespace Caly.Core.Controls;

/// <summary>
/// Control that represents the text layer of a PDF page, handling text selection and interaction.
/// </summary>
public sealed class PageInteractiveLayerControl : Control
{
    // https://github.com/AvaloniaUI/Avalonia/blob/master/src/Avalonia.Controls/Primitives/TextSelectionCanvas.cs#L62
    // Check caret handle

    private static readonly Color SelectionColor = Color.FromArgb(169, 0x33, 0x99, 0xFF);
    private static readonly Color SearchColor = Color.FromArgb(120, 255, 0, 0);

    private static readonly ImmutableSolidColorBrush SelectionBrush = new(SelectionColor);
    private static readonly ImmutableSolidColorBrush SearchBrush = new(SearchColor);

    public static readonly StyledProperty<PdfTextLayer?> PdfTextLayerProperty =
        AvaloniaProperty.Register<PageInteractiveLayerControl, PdfTextLayer?>(nameof(PdfTextLayer));

    public static readonly StyledProperty<int?> PageNumberProperty =
        AvaloniaProperty.Register<PageInteractiveLayerControl, int?>(nameof(PageNumber));

    /// <summary>
    /// Defines the <see cref="VisibleArea"/> property.
    /// </summary>
    public static readonly StyledProperty<Rect?> VisibleAreaProperty =
        AvaloniaProperty.Register<PageInteractiveLayerControl, Rect?>(nameof(VisibleArea));

    /// <summary>
    /// Defines the <see cref="SelectedWords"/> property.
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<PdfRectangle>?> SelectedWordsProperty =
        AvaloniaProperty.Register<PageInteractiveLayerControl, IReadOnlyList<PdfRectangle>?>(nameof(SelectedWords));

    /// <summary>
    /// Defines the <see cref="SearchResults"/> property.
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<PdfRectangle>?> SearchResultsProperty =
        AvaloniaProperty.Register<PageInteractiveLayerControl, IReadOnlyList<PdfRectangle>?>(nameof(SearchResults));

    private StreamGeometry[]? _selectedWordsGeometry;
    private StreamGeometry[]? _searchResultsGeometry;

    static PageInteractiveLayerControl()
    {
        AffectsRender<PageInteractiveLayerControl>(PdfTextLayerProperty, VisibleAreaProperty,
            SelectedWordsProperty, SearchResultsProperty);
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

    public PdfTextLayer? PdfTextLayer
    {
        get => GetValue(PdfTextLayerProperty);
        set => SetValue(PdfTextLayerProperty, value);
    }

    public int? PageNumber
    {
        get => GetValue(PageNumberProperty);
        set => SetValue(PageNumberProperty, value);
    }

    public Rect? VisibleArea
    {
        get => GetValue(VisibleAreaProperty);
        set => SetValue(VisibleAreaProperty, value);
    }

    internal Matrix GetLayoutTransformMatrix()
    {
        return this.FindAncestorOfType<PageItemsControl>()?
            .LayoutTransform?
            .LayoutTransform?.Value ?? Matrix.Identity;
    }

    internal void SetIbeamCursor()
    {
        Debug.ThrowNotOnUiThread();

        var itemsControl = this.FindAncestorOfType<PageItemsControl>();
        if (itemsControl is not null && itemsControl.Cursor != App.IbeamCursor)
        {
            itemsControl.Cursor = App.IbeamCursor;
        }
    }

    internal void SetHandCursor()
    {
        Debug.ThrowNotOnUiThread();

        var itemsControl = this.FindAncestorOfType<PageItemsControl>();
        if (itemsControl is not null && itemsControl.Cursor != App.HandCursor)
        {
            itemsControl.Cursor = App.HandCursor;
        }
    }

    internal void SetDefaultCursor()
    {
        Debug.ThrowNotOnUiThread();

        var itemsControl = this.FindAncestorOfType<PageItemsControl>();
        if (itemsControl is not null && itemsControl.Cursor != App.DefaultCursor)
        {
            itemsControl.Cursor = App.DefaultCursor;
        }
    }

    internal void HideAnnotation()
    {
        if (FlyoutBase.GetAttachedFlyout(this) is not Flyout attachedFlyout)
        {
            return;
        }

        attachedFlyout.Hide();
        attachedFlyout.Content = null;
    }

    internal void ShowAnnotation(PdfAnnotation annotation)
    {
        if (FlyoutBase.GetAttachedFlyout(this) is not Flyout attachedFlyout)
        {
            return;
        }

        var contentText = new TextBlock()
        {
            MaxWidth = 200,
            TextWrapping = TextWrapping.Wrap,
            Text = annotation.Content
        };

        if (!string.IsNullOrEmpty(annotation.Date))
        {
            attachedFlyout.Content = new StackPanel()
            {
                Orientation = Orientation.Vertical,
                Children =
                {
                    new TextBlock()
                    {
                        Text = annotation.Date
                    },
                    contentText
                }
            };
        }
        else
        {
            attachedFlyout.Content = contentText;
        }

        attachedFlyout.ShowAt(this);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SelectedWordsProperty)
        {
            _selectedWordsGeometry = null;
            if (change.NewValue is IReadOnlyCollection<PdfRectangle> rects && rects.Count > 0)
            {
                _selectedWordsGeometry = rects.Select(r => PdfWordHelpers.GetGeometry(r, true)).ToArray();
            }
            else
            {
                _selectedWordsGeometry = null;
            }
        }
        else if (change.Property == SearchResultsProperty)
        {
            _searchResultsGeometry = null;
            if (change.NewValue is IReadOnlyCollection<PdfRectangle> rects && rects.Count > 0)
            {
                _searchResultsGeometry = rects.Select(r => PdfWordHelpers.GetGeometry(r, true)).ToArray();
            }
            else
            {
                _searchResultsGeometry = null;
            }
        }
#if DEBUG
        else if (change.Property == PdfTextLayerProperty)
        {
            _annotationsGeometry = null;
            _textBlockGeometry = null;
            _textLineGeometry = null;
            _wordsGeometry = null;

            if (change.NewValue is not PdfTextLayer textLayer)
            {
                return;
            }

            if (textLayer.Annotations?.Count > 0)
            {
                _annotationsGeometry = textLayer.Annotations
                    .Select(a => PdfWordHelpers.GetGeometry(a.BoundingBox, true)).ToArray();
            }

            if (textLayer.TextBlocks?.Count > 0)
            {
                var blockGeometries = new List<StreamGeometry>();
                var lineGeometries = new List<StreamGeometry>();
                var wordGeometries = new List<StreamGeometry>();
                foreach (var block in textLayer.TextBlocks)
                {
                    blockGeometries.Add(PdfWordHelpers.GetGeometry(block.BoundingBox, true));
                    foreach (var line in block.TextLines)
                    {
                        lineGeometries.Add(PdfWordHelpers.GetGeometry(line.BoundingBox, true));
                        foreach (var word in line.Words)
                        {
                            wordGeometries.Add(PdfWordHelpers.GetGeometry(word.BoundingBox, false));
                        }
                    }
                }

                _textBlockGeometry = blockGeometries.ToArray();
                _textLineGeometry = lineGeometries.ToArray();
                _wordsGeometry = wordGeometries.ToArray();
            }
        }
#endif
    }

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        if (!VisibleArea.HasValue || VisibleArea.Value.IsEmpty())
        {
            return;
        }

        // We need to fill to get Pointer events
        context.FillRectangle(Brushes.Transparent, Bounds);

        if (PdfTextLayer?.TextBlocks is null)
        {
            return;
        }

#if DEBUG
        RenderDebug(context);
#endif

        // Draw search results first
        if (_searchResultsGeometry?.Length > 0)
        {
            foreach (var geometry in _searchResultsGeometry)
            {
                if (!geometry.Bounds.Intersects(VisibleArea.Value))
                {
                    continue;
                }

                context.DrawGeometry(SearchBrush, null, geometry);
            }
        }

        // Render Selection
        if (_selectedWordsGeometry?.Length > 0)
        {
            foreach (var geometry in _selectedWordsGeometry)
            {
                if (!geometry.Bounds.Intersects(VisibleArea.Value))
                {
                    continue;
                }

                context.DrawGeometry(SelectionBrush, null, geometry);
            }
        }
    }

#if DEBUG
    private static readonly ImmutableSolidColorBrush AnnotationsBrush = new(Colors.Purple, 0.4);
    private static readonly ImmutablePen AnnotationsPen = new(AnnotationsBrush, 0.5);
    private static readonly ImmutableSolidColorBrush YellowBrush = new(Colors.Yellow, 0.4);
    private static readonly ImmutableSolidColorBrush GreenBrush = new(Colors.Green, 0.4);
    private static readonly ImmutableSolidColorBrush RedBrush = new(Colors.Red, 0.4);
    private static readonly ImmutablePen RedPen = new(RedBrush, 0.5);

    private StreamGeometry[]? _annotationsGeometry;
    private StreamGeometry[]? _textBlockGeometry;
    private StreamGeometry[]? _textLineGeometry;
    private StreamGeometry[]? _wordsGeometry;

    private void RenderDebug(DrawingContext context)
    {
        if (!VisibleArea.HasValue || VisibleArea.Value.IsEmpty())
        {
            return;
        }

        // Render Annotations
        if (_annotationsGeometry?.Length > 0)
        {
            foreach (var geometry in _annotationsGeometry)
            {
                if (!geometry.Bounds.Intersects(VisibleArea.Value))
                {
                    continue;
                }

                context.DrawGeometry(AnnotationsBrush, AnnotationsPen, geometry);
            }
        }

        // Render Text Blocks
        if (_textBlockGeometry?.Length > 0)
        {
            foreach (var geometry in _textBlockGeometry)
            {
                if (!geometry.Bounds.Intersects(VisibleArea.Value))
                {
                    continue;
                }

                context.DrawGeometry(GreenBrush, null, geometry);
            }
        }

        // Render Text Lines
        if (_textLineGeometry?.Length > 0)
        {
            foreach (var geometry in _textLineGeometry)
            {
                if (!geometry.Bounds.Intersects(VisibleArea.Value))
                {
                    continue;
                }

                context.DrawGeometry(YellowBrush, null, geometry);
            }
        }

        // Render Words
        if (_wordsGeometry?.Length > 0)
        {
            foreach (var geometry in _wordsGeometry)
            {
                if (!geometry.Bounds.Intersects(VisibleArea.Value))
                {
                    continue;
                }

                context.DrawGeometry(RedBrush, RedPen, geometry);
            }
        }
    }
#endif
}