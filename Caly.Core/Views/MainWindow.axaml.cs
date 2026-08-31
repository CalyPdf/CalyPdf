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

using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;

namespace Caly.Core.Views;

public partial class MainWindow : Window
{
    public WindowNotificationManager? NotificationManager { get; set; }

    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// This window's tab strip, declared in XAML with its templates and bindings already set
    /// up. Handed to Tabalonia when a tab is torn off into this window.
    /// </summary>
    internal Tabalonia.Controls.TabsControl TabsControl => PART_MainView.TabsControl;

    protected override void OnLoaded(RoutedEventArgs e)
    {
        NotificationManager = new WindowNotificationManager(this)
        {
            Position = NotificationPosition.BottomRight,
#if DEBUG
            MaxItems = 50
#else
            MaxItems = 5
#endif
        };

        base.OnLoaded(e); // We need to OnLoad() after the NotificationManager is created
    }
}