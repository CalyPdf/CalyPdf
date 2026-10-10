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
using Avalonia.VisualTree;
using Caly.Core.Utilities;
using Caly.Pdf.Models;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Styling;
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

    private const string AnnotationFlyoutClass = "calyAnnotationFlyout";

    private const string AnnotationFlyoutBackgroundKey = "CalyAnnotationFlyoutBackground";

    /// <summary>
    /// Size of the "3D" badge drawn on 3D artwork, and its inset from the artwork's top-left corner, in
    /// screen pixels at 100% display scaling. The badge keeps this size at every page zoom.
    /// </summary>
    internal const double Artwork3DBadgeWidth = 30;
    internal const double Artwork3DBadgeHeight = 18;
    internal const double Artwork3DBadgeMargin = 4;

    private const double Artwork3DBadgeCornerRadius = 3;
    private const double Artwork3DBadgeFontSize = 11;
    private const int Artwork3DMaxViewNames = 10;

    private static readonly ImmutableSolidColorBrush Artwork3DBadgeBrush = new(Color.FromArgb(0xCC, 0x1F, 0x29, 0x37));
    private static FormattedText? _artwork3DBadgeText;

    private PdfArtwork3D? _shownArtwork3D;

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

    private StreamGeometry[]? _selectedWordsGeometry;
    private StreamGeometry[]? _searchResultsGeometry;

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

    /// <summary>
    /// The rectangle of the "3D" badge for artwork occupying <paramref name="box"/>, in layer coordinates:
    /// at the artwork's top-left corner, with a constant on-screen size whatever the page zoom.
    /// </summary>
    internal static Rect GetArtwork3DBadgeRect(PdfRectangle box, double zoom)
    {
        if (!(zoom > 0) || double.IsInfinity(zoom))
        {
            zoom = 1;
        }

        // The layer's y axis grows downwards: the smallest y is the top edge, whatever the corner order.
        double left = Math.Min(Math.Min(box.TopLeft.X, box.TopRight.X), Math.Min(box.BottomLeft.X, box.BottomRight.X));
        double top = Math.Min(Math.Min(box.TopLeft.Y, box.TopRight.Y), Math.Min(box.BottomLeft.Y, box.BottomRight.Y));

        return new Rect(
            left + Artwork3DBadgeMargin / zoom,
            top + Artwork3DBadgeMargin / zoom,
            Artwork3DBadgeWidth / zoom,
            Artwork3DBadgeHeight / zoom);
    }

    /// <summary>
    /// The text shown when hovering 3D artwork: its format and the views its author defined.
    /// </summary>
    internal static string GetArtwork3DDescription(string formatDisplayName, IReadOnlyList<string> viewNames)
    {
        var sb = new StringBuilder();
        sb.Append("3D model (").Append(formatDisplayName).AppendLine(")");

        if (viewNames.Count == 0)
        {
            sb.AppendLine("No authored views");
        }
        else
        {
            sb.Append("Views: ");
            int shown = Math.Min(viewNames.Count, Artwork3DMaxViewNames);
            for (int i = 0; i < shown; ++i)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(viewNames[i]);
            }

            if (viewNames.Count > shown)
            {
                sb.Append(" and ").Append(viewNames.Count - shown).Append(" more");
            }

            sb.AppendLine();
        }

        sb.Append("Interactive 3D viewing is not available yet.");
        return sb.ToString();
    }

    internal static string GetArtwork3DDescription(PdfArtwork3D artwork)
    {
        return GetArtwork3DDescription(artwork.FormatDisplayName, artwork.ViewNames);
    }

    internal void ShowArtwork3D(PdfArtwork3D artwork)
    {
        if (FlyoutBase.GetAttachedFlyout(this) is not Flyout attachedFlyout)
        {
            return;
        }

        if (ReferenceEquals(_shownArtwork3D, artwork) && attachedFlyout.IsOpen)
        {
            // Pointer moves over the same artwork: nothing to rebuild.
            return;
        }

        _shownArtwork3D = artwork;

        attachedFlyout.FlyoutPresenterClasses.Remove(AnnotationFlyoutClass);
        attachedFlyout.Content = new TextBlock()
        {
            MaxWidth = 280,
            TextWrapping = TextWrapping.Wrap,
            Text = GetArtwork3DDescription(artwork)
        };

        attachedFlyout.ShowAt(this);
    }

    internal void HideAnnotation()
    {
        _shownArtwork3D = null;

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
            _selectedWordsGeometry = null;
            if (change.NewValue is IReadOnlyList<PdfRectangle[]> lines && lines.Count > 0)
            {
                _selectedWordsGeometry = lines.Select(PdfWordHelpers.GetLineGeometry).ToArray();
            }
            else
            {
                _selectedWordsGeometry = null;
            }
        }
        else if (change.Property == SearchResultsProperty)
        {
            _searchResultsGeometry = null;
            if (change.NewValue is IReadOnlyList<PdfRectangle[]> lines && lines.Count > 0)
            {
                _searchResultsGeometry = lines.Select(PdfWordHelpers.GetLineGeometry).ToArray();
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

        if (PdfTextLayer.Artworks3D is { Count: > 0 } artworks)
        {
            RenderArtwork3DBadges(context, artworks, VisibleArea.Value);
        }
    }

    private void RenderArtwork3DBadges(DrawingContext context, IReadOnlyList<PdfArtwork3D> artworks, Rect visibleArea)
    {
        // The page zoom is a layout transform above this control: draw the badge at the inverse scale
        // so it keeps its size on screen.
        Matrix transform = GetLayoutTransformMatrix();
        double zoom = Math.Sqrt(transform.M11 * transform.M11 + transform.M12 * transform.M12);
        if (!(zoom > 0) || double.IsInfinity(zoom))
        {
            zoom = 1;
        }

        _artwork3DBadgeText ??= new FormattedText("3D", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold), Artwork3DBadgeFontSize, Brushes.White);

        foreach (PdfArtwork3D artwork in artworks)
        {
            Rect badge = GetArtwork3DBadgeRect(artwork.BoundingBox, zoom);
            if (!badge.Intersects(visibleArea))
            {
                continue;
            }

            context.DrawRectangle(Artwork3DBadgeBrush, null, badge, Artwork3DBadgeCornerRadius / zoom, Artwork3DBadgeCornerRadius / zoom);

            using (context.PushTransform(Matrix.CreateScale(1 / zoom, 1 / zoom) * Matrix.CreateTranslation(badge.X, badge.Y)))
            {
                context.DrawText(_artwork3DBadgeText, new Point(
                    (Artwork3DBadgeWidth - _artwork3DBadgeText.Width) / 2,
                    (Artwork3DBadgeHeight - _artwork3DBadgeText.Height) / 2));
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