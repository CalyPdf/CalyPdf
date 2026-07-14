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
using Avalonia.Input;
using Avalonia.VisualTree;
using Caly.Core.Utilities;
using Caly.Core.ViewModels;
using CommunityToolkit.Mvvm.Input;
using System;

namespace Caly.Core.Controls;

/// <summary>
/// Control that represents all open PDF documents, together with the top and left navigation panels.
/// Each PDF document is displayed in a tab.
/// </summary>
public sealed partial class DocumentsTabsControl : UserControl
{
    private const int MaxPaneLength = 500;
    private const int MinPaneLength = 200;

    private Point? _lastPoint;
    private double _originalPaneLength;

    private SplitView? _splitView;
    private TextBox? _textBoxPageNumber;

    public DocumentsTabsControl()
    {
        InitializeComponent();
        KeyBindings.Add(new KeyBinding
        {
            Gesture = CalyHotkeyConfiguration.DocumentGoToGesture,
            Command = new RelayCommand(() =>
            {
                var textBox = GetTextBoxPageNumber();
                if (textBox is null)
                {
                    return;
                }

                textBox.Focus();
                textBox.SelectAll();
            })
        });
    }

    private TextBox? GetTextBoxPageNumber()
    {
        if (_textBoxPageNumber is null)
        {
            _textBoxPageNumber = this.FindDescendantOfType<TextBox>(false, tb => tb.Name == "PART_TextBoxPageNumber");
        }
        return _textBoxPageNumber;
    }

    private SplitView? GetSplitView()
    {
        if (_splitView is null)
        {
            _splitView = this.FindDescendantOfType<SplitView>();
            if (_splitView is null)
            {
                return null;
            }

            if (string.IsNullOrEmpty(_splitView.Name) || !_splitView.Name.Equals("PART_SplitView"))
            {
                throw new Exception("The found split view does not have the correct name.");
            }
        }

        return _splitView;
    }

    #region Resize SplitView.Pane
    private void Resize_OnPointerEntered(object? sender, PointerEventArgs e)
    {
        Debug.ThrowNotOnUiThread();
        Cursor = App.SizeWestEastCursor;
    }

    private void Resize_OnPointerExited(object? sender, PointerEventArgs e)
    {
        Debug.ThrowNotOnUiThread();
        Cursor = App.DefaultCursor;
    }

    private void Resize_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Grid)
        {
            return;
        }

        SplitView? splitView = GetSplitView();

        if (splitView is null)
        {
            return;
        }

        if (!splitView.IsPaneOpen)
        {
            return;
        }

        _lastPoint = e.GetPosition(null);
        _originalPaneLength = splitView.OpenPaneLength;
        e.Handled = true;
        e.PreventGestureRecognition();
    }

    private void Resize_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_lastPoint.HasValue || sender is not Grid)
        {
            return;
        }

        SplitView? splitView = GetSplitView();

        if (splitView is null || !splitView.IsPaneOpen)
        {
            return;
        }

        Point mouseMovement = (e.GetPosition(null) - _lastPoint).Value;
        splitView.OpenPaneLength = Math.Max(Math.Min(_originalPaneLength + mouseMovement.X, MaxPaneLength), MinPaneLength);
    }

    private void Resize_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_lastPoint.HasValue || sender is not Grid)
        {
            return;
        }

        _lastPoint = null;
        _originalPaneLength = 0;
        e.Handled = true;
    }
    #endregion

    private void PageNumberTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        if (sender is not TextBox textBox)
        {
            return;
        }

        BindingOperations.GetBindingExpressionBase(textBox, TextBox.TextProperty)?.UpdateSource();

        // Try send back focus to the PageItemsControl
        this.FindDescendantOfType<PageItemsControl>()?.Focus();
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged ||
            sender is not DocumentsTabsControl tabsControl ||
            e.NewSize.Width > e.PreviousSize.Width)
        {
            return;
        }

        var splitView = GetSplitView();
        if (splitView is null)
        {
            return;
        }

        if (splitView.IsPaneOpen && tabsControl.Bounds.Width < splitView.OpenPaneLength * 2)
        {
            splitView.SetCurrentValue(SplitView.IsPaneOpenProperty, false);
        }
    }

    private void EmbeddedFiles_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.Properties.IsLeftButtonPressed || e.ClickCount != 2)
        {
            return;
        }

        if (sender is not Control ctrl || ctrl.DataContext is not PdfEmbeddedFileViewModel vm)
        {
            return;
        }
        
        vm.OpenCommand.Execute(null);
        e.Handled = true;
    }
}