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
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Caly.Tests;

/// <summary>
/// A tab strip on the left (the navigation and settings panes) marks the selected tab with a vertical
/// pipe at its left edge, as Fluent did, that grows like Fluent 2's pill (CalyStyles.axaml). Fluent 2
/// 12.2.0 draws a horizontal pill under the header instead, because its placement styles miss its own
/// Rectangle pipe.
/// </summary>
public class LeftTabStripPipeTests
{
    [AvaloniaFact]
    public void SelectedTab_HasAVerticalPipeBesideItsHeader_ThatGrowsWhenSelected()
    {
        // Headers the size of the panes' 17 px icons.
        var a = new TabItem { Header = new Border { Width = 17, Height = 17 }, MinHeight = 40 };
        var b = new TabItem { Header = new Border { Width = 17, Height = 17 }, MinHeight = 40 };
        var tabs = new TabControl { TabStripPlacement = Dock.Left, Items = { a, b }, SelectedIndex = 0 };
        var window = new Window { Width = 400, Height = 300, Content = tabs };
        window.Styles.Add((Styles)AvaloniaXamlLoader.Load(new Uri("avares://Caly.Core/CalyStyles.axaml")));
        window.Show();
        Settle();

        tabs.SelectedIndex = 1;
        Settle();

        Rectangle pipeA = Pipe(a), pipeB = Pipe(b);
        var header = b.GetTemplateDescendants().OfType<ContentPresenter>().Single(p => p.Name == "PART_ContentPresenter");
        Point pipeTop = pipeB.TranslatePoint(default, b)!.Value;
        Point headerTop = header.TranslatePoint(default, b)!.Value;

        Assert.Contains(pipeB.Transitions!, t => t is DoubleTransition { Property: var p } && p == Layoutable.HeightProperty);
        Assert.Equal(0, pipeA.Opacity);
        Assert.Equal(4, pipeA.Bounds.Height);
        Assert.Equal(1, pipeB.Opacity);
        Assert.Equal(2, pipeB.Bounds.Width);
        Assert.Equal(24, pipeB.Bounds.Height);
        Assert.Equal(0, pipeB.Bounds.X);
        Assert.True(pipeTop.X + pipeB.Bounds.Width <= headerTop.X, $"pipe ends at {pipeTop.X + pipeB.Bounds.Width}, header starts at {headerTop.X}");
        Assert.Equal(headerTop.Y + header.Bounds.Height / 2, pipeTop.Y + pipeB.Bounds.Height / 2, 0.5);

        window.Close();
    }

    private static Rectangle Pipe(TabItem item)
        => item.GetTemplateDescendants().OfType<Rectangle>().Single(r => r.Name == "PART_SelectedPipe");

    // The headless render timer runs on the wall clock: wait out the 167 ms transition, then tick once.
    private static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(250);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
