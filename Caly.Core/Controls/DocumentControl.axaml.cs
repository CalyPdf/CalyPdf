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

using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Caly.Core.Models;
using Caly.Core.Utilities;
using Caly.Core.ViewModels;

namespace Caly.Core.Controls;

/*
 * Document state (pages, zoom, scroll, selection) is bound directly onto the
 * inner PageItemsControl by its control theme; this control only reacts to
 * navigation triggers (selected page, bookmark, search result) and clears the
 * text selection on background clicks.
 */

/// <summary>
/// Control that represents a PDF document.
/// </summary>
[TemplatePart("PART_PageItemsControl", typeof(PageItemsControl))]
[TemplatePart("PART_TextBoxPassword", typeof(TextBox))]
public sealed class DocumentControl : CalyTemplatedControl
{
    private PageItemsControl? _pageItemsControl;
    private TextBox? _textBoxPassword;

    /// <summary>
    /// Defines the <see cref="SelectedPageNumber"/> property. Starts at 1.
    /// </summary>
    public static readonly StyledProperty<int?> SelectedPageNumberProperty =
        AvaloniaProperty.Register<DocumentControl, int?>(nameof(SelectedPageNumber));

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
    /// Defines the <see cref="ClearSelection"/> property.
    /// </summary>
    public static readonly StyledProperty<ICommand?> ClearSelectionProperty =
        AvaloniaProperty.Register<DocumentControl, ICommand?>(nameof(ClearSelection));

    /// <summary>
    /// Defines the <see cref="IsAwaitingPassword"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsAwaitingPasswordProperty =
        AvaloniaProperty.Register<DocumentControl, bool>(nameof(IsAwaitingPassword));

    public ICommand? ClearSelection
    {
        get => GetValue(ClearSelectionProperty);
        set => SetValue(ClearSelectionProperty, value);
    }

    /// <summary>
    /// Whether the inline password prompt is currently shown. Mirrors
    /// <see cref="ViewModels.DocumentViewModel.IsAwaitingPassword"/> so the control can move
    /// focus into the password box as soon as it appears.
    /// </summary>
    public bool IsAwaitingPassword
    {
        get => GetValue(IsAwaitingPasswordProperty);
        set => SetValue(IsAwaitingPasswordProperty, value);
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

        if (change.Property == SelectedPageNumberProperty)
        {
            if (change.NewValue is int p)
            {
                GoToPage(p);
            }
        }
        else if (change.Property == IsAwaitingPasswordProperty)
        {
            if (change.NewValue is true)
            {
                _textBoxPassword?.Focus();
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
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _pageItemsControl = e.NameScope.FindFromNameScope<PageItemsControl>("PART_PageItemsControl");

        if (_textBoxPassword is not null)
        {
            _textBoxPassword.KeyDown -= PasswordTextBox_OnKeyDown;
        }

        _textBoxPassword = e.NameScope.FindFromNameScope<TextBox>("PART_TextBoxPassword");

        if (_textBoxPassword is not null)
        {
            _textBoxPassword.KeyDown += PasswordTextBox_OnKeyDown;
        }
    }

    private void PasswordTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not DocumentViewModel vm)
        {
            return;
        }

        if (e.Key == Key.Enter && vm.SubmitPasswordCommand.CanExecute(null))
        {
            vm.SubmitPasswordCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && vm.CancelPasswordCommand.CanExecute(null))
        {
            vm.CancelPasswordCommand.Execute(null);
            e.Handled = true;
        }
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
}
