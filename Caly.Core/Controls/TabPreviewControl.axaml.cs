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

using Avalonia.Controls;
using Avalonia.Interactivity;
using Caly.Core.ViewModels;

namespace Caly.Core.Controls;

/// <summary>
/// The contents of a document tab's hover tooltip: a preview of the page the user was last
/// on, the file name, and the page position.
/// <para>
/// One instance per tab, materialised by the <c>ToolTip.Tip</c> setter in
/// <c>DocumentsTabsControl.axaml</c>, which inherits the tab's <see cref="DocumentViewModel"/>
/// as its data context.
/// </para>
/// </summary>
public partial class TabPreviewControl : UserControl
{
    public TabPreviewControl()
    {
        InitializeComponent();
    }

    /// <summary>
    /// This control only enters the visual tree when the tab's tooltip popup opens, so its own
    /// load is the tooltip opening.
    /// <para>
    /// <c>Loaded</c> rather than <c>AttachedToVisualTree</c>, because the data context is
    /// inherited through the popup's parent and has reliably propagated by this point.
    /// </para>
    /// </summary>
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (DataContext is DocumentViewModel document)
        {
            // No-ops unless this document is inactive and has no preview captured.
            document.EnsureTabPreviewCommand.Execute(null);
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        if (DataContext is DocumentViewModel document)
        {
            document.CancelTabPreview();
        }
    }
}
