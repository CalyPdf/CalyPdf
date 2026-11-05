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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.VisualTree;
using Caly.Core.Utilities;
using Caly.Pdf.Models;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Styling;
using SkiaSharp;
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

    private const string AnnotationFlyoutClass = "calyAnnotationFlyout";

    private const string AnnotationFlyoutBackgroundKey = "CalyAnnotationFlyoutBackground";

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
    public static readonly StyledProperty<IReadOnlyList<PdfRectangle[]>?> SelectedWordsProperty =
        AvaloniaProperty.Register<PageInteractiveLayerControl, IReadOnlyList<PdfRectangle[]>?>(nameof(SelectedWords));

    /// <summary>
    /// Defines the <see cref="SearchResults"/> property.
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<PdfRectangle[]>?> SearchResultsProperty =
        AvaloniaProperty.Register<PageInteractiveLayerControl, IReadOnlyList<PdfRectangle[]>?>(nameof(SearchResults));

    private IRef<HighlightPaths>? _selectedWordsPaths;
    private IRef<HighlightPaths>? _searchResultsPaths;

    static PageInteractiveLayerControl()
    {
        AffectsRender<PageInteractiveLayerControl>(PdfTextLayerProperty, VisibleAreaProperty,
            SelectedWordsProperty, SearchResultsProperty);
    }

    public PageInteractiveLayerControl()
    {
        Styles.Add(new Style(x => x.OfType<FlyoutPresenter>().Class(AnnotationFlyoutClass))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, new DynamicResourceExtension(AnnotationFlyoutBackgroundKey))
            }
        });
    }

    public IReadOnlyList<PdfRectangle[]>? SelectedWords
    {
        get => GetValue(SelectedWordsProperty);
        set => SetValue(SelectedWordsProperty, value);
    }

    public IReadOnlyList<PdfRectangle[]>? SearchResults
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

        attachedFlyout.FlyoutPresenterClasses.Remove(AnnotationFlyoutClass);
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

        if (annotation.Colour is not null)
        {
            var rgb = annotation.Colour.ToRGBValues();
            byte r = Convert.ToByte(rgb.r * 0.5 * 255);
            byte g = Convert.ToByte(rgb.g * 0.5 * 255);
            byte b = Convert.ToByte(rgb.b * 0.5 * 255);

            var color = new Color(byte.MaxValue, r, g, b);

            Resources[AnnotationFlyoutBackgroundKey] = new ImmutableSolidColorBrush(color);
            if (!attachedFlyout.FlyoutPresenterClasses.Contains(AnnotationFlyoutClass))
            {
                attachedFlyout.FlyoutPresenterClasses.Add(AnnotationFlyoutClass);
            }
        }
        else
        {
            attachedFlyout.FlyoutPresenterClasses.Remove(AnnotationFlyoutClass);
        }

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
            HighlightPaths.Replace(ref _selectedWordsPaths, change.NewValue as IReadOnlyList<PdfRectangle[]>);
        }
        else if (change.Property == SearchResultsProperty)
        {
            HighlightPaths.Replace(ref _searchResultsPaths, change.NewValue as IReadOnlyList<PdfRectangle[]>);
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

        if (_searchResultsPaths is not null || _selectedWordsPaths is not null)
        {
            // Each operation holds its own references: the paths stay alive while a frame still draws them
            context.Custom(new HighlightDrawOperation(new Rect(Bounds.Size), VisibleArea.Value.ToSKRect(),
                _searchResultsPaths?.Clone(), _selectedWordsPaths?.Clone()));
        }
    }

    /// <summary>
    /// The Skia paths of the highlighted lines, one per line. Disposed as soon as no frame uses them:
    /// the GPU back end keeps a cached tessellation per path until the path is deleted.
    /// </summary>
    internal sealed class HighlightPaths : IDisposable
    {
        private readonly PdfRectangle[][] _lines;
        private readonly IRef<SKPath>[] _paths;

        private HighlightPaths(PdfRectangle[][] lines, IRef<SKPath>[] paths)
        {
            _lines = lines;
            _paths = paths;
        }

        public int Count => _paths.Length;

        public SKPath this[int index] => _paths[index].Item;

        public static void Replace(ref IRef<HighlightPaths>? field, IReadOnlyList<PdfRectangle[]>? lines)
        {
            var old = field;
            field = lines is { Count: > 0 } ? RefCountable.Create(Create(lines, old?.Item)) : null;
            old?.Dispose(); // after Create: the reused paths have their own reference by now
        }

        /// <summary>
        /// Builds the paths of <paramref name="lines"/>, reusing those of <paramref name="old"/> for lines that did not change.
        /// </summary>
        private static HighlightPaths Create(IReadOnlyList<PdfRectangle[]> lines, HighlightPaths? old)
        {
            var newLines = new PdfRectangle[lines.Count][];
            var paths = new IRef<SKPath>[lines.Count];

            for (int i = 0; i < lines.Count; ++i)
            {
                var line = lines[i];
                newLines[i] = line;
                paths[i] = old?.FindPath(line, i, lines.Count)?.Clone() ??
                           RefCountable.Create(PdfWordHelpers.GetLinePath(line));
            }

            return new HighlightPaths(newLines, paths);
        }

        /// <summary>
        /// The path of the old line at the same index counted from the start (a forward drag changes the last
        /// line), else from the end (a backward drag changes the first line). <c>null</c> if neither matches.
        /// </summary>
        private IRef<SKPath>? FindPath(PdfRectangle[] line, int index, int count)
        {
            if (index < _lines.Length && _lines[index].AsSpan().SequenceEqual(line))
            {
                return _paths[index];
            }

            int fromEnd = _lines.Length - (count - index);
            if (fromEnd >= 0 && _lines[fromEnd].AsSpan().SequenceEqual(line))
            {
                return _paths[fromEnd];
            }

            return null;
        }

        public void Dispose()
        {
            foreach (var path in _paths)
            {
                path.Dispose();
            }
        }
    }

    private sealed class HighlightDrawOperation : ICustomDrawOperation
    {
        private static readonly SKPaint SearchPaint = new() { Color = SearchColor.ToSKColor(), IsAntialias = true };
        private static readonly SKPaint SelectionPaint = new() { Color = SelectionColor.ToSKColor(), IsAntialias = true };

        private readonly SKRect _visibleArea;
        private IRef<HighlightPaths>? _search;
        private IRef<HighlightPaths>? _selection;

        public HighlightDrawOperation(Rect bounds, SKRect visibleArea, IRef<HighlightPaths>? search,
            IRef<HighlightPaths>? selection)
        {
            Bounds = bounds;
            _visibleArea = visibleArea;
            _search = search;
            _selection = selection;
        }

        public Rect Bounds { get; }

        public bool HitTest(Point p) => false;

        public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);

        public void Render(ImmediateDrawingContext context)
        {
            if (!context.TryGetFeature(out ISkiaSharpApiLeaseFeature? leaseFeature) || leaseFeature is null)
            {
                return;
            }

            using var lease = leaseFeature.Lease();

            // Search results first, so the selection is drawn on top
            Draw(lease.SkCanvas, _search, SearchPaint);
            Draw(lease.SkCanvas, _selection, SelectionPaint);
        }

        private void Draw(SKCanvas canvas, IRef<HighlightPaths>? paths, SKPaint paint)
        {
            if (paths is null)
            {
                return;
            }

            var item = paths.Item;
            for (int i = 0; i < item.Count; ++i)
            {
                var path = item[i];
                if (path.Bounds.IntersectsWith(_visibleArea))
                {
                    canvas.DrawPath(path, paint);
                }
            }
        }

        public void Dispose()
        {
            _search?.Dispose();
            _search = null;
            _selection?.Dispose();
            _selection = null;
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