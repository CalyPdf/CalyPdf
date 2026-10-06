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
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Caly.Core.Utilities;

namespace Caly.Core.Controls;

/// <summary>
/// Control that represents the full search panel for searching text within a PDF document.
/// </summary>
[TemplatePart("PART_TextBoxSearch", typeof(TextBox))]
[TemplatePart("PART_TreeDataGrid", typeof(TreeDataGrid))]
public sealed class FullSearchPanelControl : TemplatedControl
{
    private TextBox? _textBoxSearch;
    private TreeDataGrid? _resultsDataGrid;
    private TopLevel? _topLevel;

    public FullSearchPanelControl()
    {
#if DEBUG
        if (Design.IsDesignMode)
        {
            DataContext = new ViewModels.DocumentViewModel();
        }
#endif
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_textBoxSearch is not null)
        {
            _textBoxSearch.KeyDown -= TextBoxSearch_OnKeyDown;
            _textBoxSearch.Loaded -= TextBox_Loaded;
        }

        _resultsDataGrid = e.NameScope.FindFromNameScope<TreeDataGrid>("PART_TreeDataGrid");
        _textBoxSearch = e.NameScope.FindFromNameScope<TextBox>("PART_TextBoxSearch");
        _textBoxSearch.KeyDown += TextBoxSearch_OnKeyDown;
        _textBoxSearch.Loaded += TextBox_Loaded;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        _topLevel?.AddHandler(KeyDownEvent, TopLevel_KeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _topLevel?.RemoveHandler(KeyDownEvent, TopLevel_KeyDown);
        _topLevel = null;
    }

    private void TopLevel_KeyDown(object? sender, KeyEventArgs e)
    {
        if (_textBoxSearch is null || !CalyHotkeyConfiguration.DocumentSearchGesture.Matches(e))
        {
            return;
        }

        // The same gesture may also be opening the side pane: let layout make the search box
        // visible before focusing it.
        TextBox textBox = _textBoxSearch;
        Dispatcher.UIThread.Post(() => FocusSearchBox(textBox), DispatcherPriority.Loaded);
    }

    private static void TextBox_Loaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        FocusSearchBox(textBox);
    }

    /// <summary>
    /// Focuses the search box and selects its text, so typing replaces the previous query.
    /// </summary>
    private static void FocusSearchBox(TextBox textBox)
    {
        if (!textBox.Focus())
        {
            System.Diagnostics.Debug.WriteLine("Something wrong happened while setting focus on search box.");
            return;
        }

        textBox.SelectAll();
    }

    private void TextBoxSearch_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }
        
        switch (e.Key)
        {
            case Key.Escape:
                textBox.Clear();
                break;

            case Key.Enter:
            case Key.Down:
                if (_resultsDataGrid is not null && _resultsDataGrid.IsVisible)
                {
                    _ = _resultsDataGrid.Focus(NavigationMethod.Pointer);
                    _resultsDataGrid.RowSelection?.SelectedIndex = 0;
                }
                break;
        }
    }
}