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
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Caly.Core.ViewModels;

namespace Caly.Core.Views;

public sealed partial class PrintDialogWindow : Window
{
    private PrintDialogViewModel? _subscribedVm;

    public PrintDialogWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_subscribedVm is not null)
        {
            _subscribedVm.PrintCompleted -= OnPrintCompleted;
            _subscribedVm = null;
        }

        if (DataContext is PrintDialogViewModel vm)
        {
            _subscribedVm = vm;
            vm.PrintCompleted += OnPrintCompleted;
            // Kick off printer enumeration as soon as the ViewModel is attached.
            vm.LoadPrintersCommand.Execute(null);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_subscribedVm is not null)
        {
            _subscribedVm.PrintCompleted -= OnPrintCompleted;
            _subscribedVm = null;
        }
        base.OnClosed(e);
    }

    private void OnPrintCompleted(object? sender, EventArgs e)
    {
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
