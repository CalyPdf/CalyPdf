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
using Caly.Core.Services.Interfaces;
using Caly.Core.ViewModels;
using Caly.UiTests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;

namespace Caly.UiTests.Support;

public abstract class HeadlessTestBase : IAsyncLifetime
{
    protected static CalyTestApp TestApp =>
        Application.Current as CalyTestApp ?? throw new InvalidOperationException("CalyTestApp not initialised.");

    protected IServiceProvider Services =>
        TestApp.Services ?? throw new InvalidOperationException("CalyTestApp.Services not initialised.");

    protected NullDialogService Dialogs =>
        (NullDialogService)Services.GetRequiredService<IDialogService>();

    protected TestSettingsService Settings =>
        (TestSettingsService)Services.GetRequiredService<ISettingsService>();

    public MainViewModel GetMainViewModel()
    {
        var visual = Services.GetRequiredService<Visual>();
        return (MainViewModel)((Control)visual).DataContext!;
    }

    public ValueTask InitializeAsync()
    {
        Dialogs.Reset();
        Settings.Reset();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Dialogs.Verify();
        return ValueTask.CompletedTask;
    }
}
