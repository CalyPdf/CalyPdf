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
using Avalonia.Headless;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Caly.Core.Services.Interfaces;
using Caly.Core.ViewModels;
using Caly.Core.Views;
using Caly.UiTests;
using Caly.UiTests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

[assembly: AvaloniaTestApplication(typeof(CalyTestApp))]

namespace Caly.UiTests;

public sealed class CalyTestApp : Core.App
{
    public CalyTestApp()
        : base(false)
    { }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<CalyTestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false
            });

    internal Task OpenDocTest(string? path, CancellationToken token)
    {
        return OpenDoc(path, token);
    }

    protected override void OverrideRegisteredServices(IServiceCollection services)
    {
        var mainView = new MainView { DataContext = new MainViewModel() };
        services.AddSingleton<Visual>(_ => mainView);
        services.AddSingleton<IStorageProvider>(_ => NullStorageProvider.Create());
        services.AddSingleton<IClipboard>(_ => NullClipboard.Create());

        services.Replace(ServiceDescriptor.Singleton<IDialogService, NullDialogService>());
        services.Replace(ServiceDescriptor.Singleton<ISettingsService, TestSettingsService>());
    }
}
