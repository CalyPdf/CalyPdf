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
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Caly.Tests;

/// <summary>
/// Pages set UseLayoutRounding=False, and popups opened on a page inherit it. CalyStyles turns it
/// back on for flyout and tooltip content, or Windows truncates the popup: the menu shows scroll arrows
/// and a tooltip loses its last word.
/// </summary>
public class FlyoutLayoutRoundingTests
{
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void PopupsOpenedOnAnUnroundedControlAreRounded(bool withCalyStyles)
    {
        var target = new Border { Width = 200, Height = 300, UseLayoutRounding = false };
        var window = new Window { Content = target };
        if (withCalyStyles)
        {
            window.Styles.Add(new StyleInclude(new Uri("avares://Caly.Tests"))
            {
                Source = new Uri("avares://Caly.Core/CalyStyles.axaml")
            });
        }

        window.Show();

        var menuItem = new MenuItem { Header = "Copy" };
        var menu = new MenuFlyout { Items = { menuItem } };
        menu.ShowAt(target);
        Dispatcher.UIThread.RunJobs();
        var menuPresenter = menuItem.FindAncestorOfType<MenuFlyoutPresenter>();
        Assert.NotNull(menuPresenter);
        Assert.Equal(withCalyStyles, menuPresenter.UseLayoutRounding);
        menu.Hide();

        var content = new TextBlock { Text = "Annotation" };
        var flyout = new Flyout { Content = content };
        flyout.ShowAt(target);
        Dispatcher.UIThread.RunJobs();
        var flyoutPresenter = content.FindAncestorOfType<FlyoutPresenter>();
        Assert.NotNull(flyoutPresenter);
        Assert.Equal(withCalyStyles, flyoutPresenter.UseLayoutRounding);
        flyout.Hide();

        var tip = new TextBlock { Text = "Reset view" };
        ToolTip.SetTip(target, tip);
        ToolTip.SetIsOpen(target, true);
        Dispatcher.UIThread.RunJobs();
        var toolTip = tip.FindAncestorOfType<ToolTip>();
        Assert.NotNull(toolTip);
        Assert.Equal(withCalyStyles, toolTip.UseLayoutRounding);
        ToolTip.SetIsOpen(target, false);
    }
}
