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
using System.Collections;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.VisualTree;
using Caly.Core.Models;
using Caly.Core.Utilities;

namespace Caly.Core.Controls;

/// <summary>
/// Control that represents a PDF document.
/// </summary>
[TemplatePart("PART_PageItemsControl", typeof(PageItemsControl))]
public sealed class DocumentControl : CalyTemplatedControl
{
    private PageItemsControl? _pageItemsControl;

    /// <summary>
    /// Defines the <see cref="ItemsSource"/> property.
    /// </summary>
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<DocumentControl, IEnumerable?>(nameof(ItemsSource));

    /// <summary>
    /// Defines the <see cref="PageCount"/> property.
    /// </summary>
    public static readonly StyledProperty<int> PageCountProperty =
        AvaloniaProperty.Register<DocumentControl, int>(nameof(PageCount), 0);

    /// <summary>
    /// Defines the <see cref="ZoomLevel"/> property.
    /// </summary>
    public static readonly StyledProperty<double> ZoomLevelProperty =
        AvaloniaProperty.Register<DocumentControl, double>(nameof(ZoomLevel), 1,
            defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Defines the <see cref="SelectedPageNumber"/> property. Starts at 1.
    /// </summary>
    public static readonly StyledProperty<int?> SelectedPageNumberProperty =
        AvaloniaProperty.Register<DocumentControl, int?>(nameof(SelectedPageNumber),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<ICommand?> RefreshPagesProperty =
        AvaloniaProperty.Register<DocumentControl, ICommand?>(nameof(RefreshPages));

    /// <summary>
    /// Defines the <see cref="VisiblePages"/> property. Starts at 1.
    /// </summary>
    public static readonly StyledProperty<Range?> VisiblePagesProperty =
        AvaloniaProperty.Register<DocumentControl, Range?>(nameof(VisiblePages),
            defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Defines the <see cref="RealisedPages"/> property. Starts at 1.
    /// </summary>
    public static readonly StyledProperty<Range?> RealisedPagesProperty =
        AvaloniaProperty.Register<DocumentControl, Range?>(nameof(RealisedPages),
            defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Defines the <see cref="SelectedBookmark"/> property.
    /// </summary>
    public static readonly StyledProperty<PdfBookmarkNode?> SelectedBookmarkProperty =
        AvaloniaProperty.Register<DocumentControl, PdfBookmarkNode?>(nameof(SelectedBookmark));

    /// <summary>
    /// Defines the <see cref="SelectedTextSearchResult"/> property.
    /// </summary>
    public static readonly StyledProperty<TextSearchResult?> SelectedTextSearchResultProperty =
        AvaloniaProperty.Register<DocumentControl, TextSearchResult?>(nameof(SelectedTextSearchResult));

    /// <summary>
    /// Defines the <see cref="TextSelection"/> property.
    /// </summary>
    public static readonly StyledProperty<TextSelection?> TextSelectionProperty =
        AvaloniaProperty.Register<DocumentControl, TextSelection?>(nameof(TextSelection));

    /// <summary>
    /// Defines the <see cref="DocumentChanged"/> property.
    /// </summary>
    public static readonly StyledProperty<ICommand?> DocumentChangedProperty =
        AvaloniaProperty.Register<DocumentControl, ICommand?>(nameof(DocumentChanged));

    /// <summary>
    /// Defines the <see cref="ClearSelection"/> property.
    /// </summary>
    public static readonly StyledProperty<ICommand?> ClearSelectionProperty =
        AvaloniaProperty.Register<DocumentControl, ICommand?>(nameof(ClearSelection));

    /// <summary>
    /// Starts at 1.
    /// </summary>
    public Range? VisiblePages
    {
        get => GetValue(VisiblePagesProperty);
        set => SetValue(VisiblePagesProperty, value);
    }

    /// <summary>
    /// Starts at 1.
    /// </summary>
    public Range? RealisedPages
    {
        get => GetValue(RealisedPagesProperty);
        set => SetValue(RealisedPagesProperty, value);
    }

    public ICommand? RefreshPages
    {
        get => GetValue(RefreshPagesProperty);
        set => SetValue(RefreshPagesProperty, value);
    }
    
    public TextSelection? TextSelection
    {
        get => GetValue(TextSelectionProperty);
        set => SetValue(TextSelectionProperty, value);
    }

    public ICommand? DocumentChanged
    {
        get => GetValue(DocumentChangedProperty);
        set => SetValue(DocumentChangedProperty, value);
    }
    
    public ICommand? ClearSelection
    {
        get => GetValue(ClearSelectionProperty);
        set => SetValue(ClearSelectionProperty, value);
    }

    public int PageCount
    {
        get => GetValue(PageCountProperty);
        set => SetValue(PageCountProperty, value);
    }

    public double ZoomLevel
    {
        get => GetValue(ZoomLevelProperty);
        set => SetValue(ZoomLevelProperty, value);
    }

    /// <summary>
    /// Starts at 1.
    /// </summary>
    public int? SelectedPageNumber
    {
        get => GetValue(SelectedPageNumberProperty);
        set => SetValue(SelectedPageNumberProperty, value);
    }

    public PdfBookmarkNode? SelectedBookmark
    {
        get => GetValue(SelectedBookmarkProperty);
        set => SetValue(SelectedBookmarkProperty, value);
    }

    public TextSearchResult? SelectedTextSearchResult
    {
        get => GetValue(SelectedTextSearchResultProperty);
        set => SetValue(SelectedTextSearchResultProperty, value);
    }

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public DocumentControl()
    {
#if DEBUG
        if (Design.IsDesignMode)
        {
            DataContext = new ViewModels.DocumentViewModel();
        }
#endif
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var pointer = e.GetCurrentPoint(this);

        if (pointer.Properties.IsLeftButtonPressed &&
            e.Source is not PageInteractiveLayerControl)
        {
            ClearSelection?.Execute(null);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DocumentChangedProperty)
        {
            System.Diagnostics.Debug.WriteLine("Active Document changed.");
            DocumentChanged?.Execute(null);
        }
        else if (change.Property == SelectedPageNumberProperty)
        {
            if (change.NewValue is int p)
            {
                GoToPage(p);
            }
        }
        else if (change.Property == SelectedBookmarkProperty)
        {
            if (SelectedBookmark?.PageNumber.HasValue == true)
            {
                if (SelectedBookmark.OffsetY.HasValue)
                {
                    GoToPage(SelectedBookmark.PageNumber.Value, SelectedBookmark.OffsetY.Value, true);
                }
                else
                {
                    GoToPage(SelectedBookmark.PageNumber.Value);
                }
            }
        }
        else if (change.Property == SelectedTextSearchResultProperty)
        {
            if (change.NewValue is TextSearchResult { PageNumber: > 0 } r)
            {
                if (r.WordIndex.HasValue)
                {
                    _pageItemsControl?.GoToWord(r.PageNumber, r.WordIndex.Value);
                }
                else
                {
                    GoToPage(r.PageNumber);
                }
            }
        }
        else if (change.Property == ZoomLevelProperty)
        {
            if (_pageItemsControl?.LayoutTransform is null || change.NewValue is not double newZoom)
            {
                return;
            }

            if (!_pageItemsControl.LayoutTransform.IsAttachedToVisualTree())
            {
                return;
            }
            
            var currentScale = _pageItemsControl.LayoutTransform.LayoutTransform?.Value.M11;
            if (currentScale.HasValue && Math.Abs(currentScale.Value - newZoom) < 1e-9)
            {
                return; // Ignore as no change in zoom level
            }

            double dZoom = newZoom / (double?)change.OldValue ?? 1.0;

            double w = 0, h = 0;
            if (!_pageItemsControl.DesiredSize.IsEmpty())
            {
                _pageItemsControl.DesiredSize.Deconstruct(out w, out h);
            }
            else if (!_pageItemsControl.Bounds.Size.IsEmpty())
            {
                _pageItemsControl.Bounds.Size.Deconstruct(out w, out h);
            }

            var pixelPoint = this.PointToScreen(new Point((int)(w / 2.0), (int)(h / 2.0)));
            var point = _pageItemsControl.LayoutTransform.PointToClient(pixelPoint);
            _pageItemsControl.ZoomTo(dZoom, point);
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _pageItemsControl = e.NameScope.FindFromNameScope<PageItemsControl>("PART_PageItemsControl");
    }

    /// <summary>
    /// Scrolls to the page number.
    /// </summary>
    /// <param name="pageNumber">The page number.<para>Starts at 1.</para></param>
    /// <param name="yOffset">Optional Y offset within the page.<para>Default is 0.</para></param>
    /// <param name="offsetPdfCoord"><c>true</c> if the offset is in PDF coordinates (bottom = 0, increasing upward).
    /// <para><c>false</c> if the offset is in Avalonia coordinates (top = 0, increasing downward, unscaled pixels).</para>
    /// Default is <c>false</c>.</param>
    public void GoToPage(int pageNumber, double yOffset = 0, bool offsetPdfCoord = false)
    {
        _pageItemsControl?.GoToPage(pageNumber, yOffset, offsetPdfCoord);
    }

    /// <summary>
    /// Get the page control for the page number.
    /// </summary>
    /// <param name="pageNumber">The page number. Starts at 1.</param>
    /// <returns>The page control, or <c>null</c> if not found.</returns>
    public PageItem? GetPageItem(int pageNumber)
    {
        return _pageItemsControl?.GetPageItem(pageNumber);
    }

    public PageItem? GetPageItemOver(PointerEventArgs e)
    {
        return _pageItemsControl?.GetPageItemOver(e);
    }
}
