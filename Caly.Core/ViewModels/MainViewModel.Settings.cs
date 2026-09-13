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

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Core.Services.Interfaces;
using Caly.Core.Utilities;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace Caly.Core.ViewModels;

public sealed partial class MainViewModel
{
    public bool ShowPdfLogs
    {
        get => GetSetting(static s => s.ShowPdfLogs);
        set => SetSetting(static (s, v) => s.ShowPdfLogs = v, value);
    }

    public bool DebugRenderTimeGraph
    {
        get => GetSetting(static s => s.Debug?.Render ?? false);
        set => SetSetting(static (s, v) => (s.Debug ??= new CalySettings.CalySettingsDebug()).Render = v, value);
    }

    public bool DebugLayoutTimeGraph
    {
        get => GetSetting(static s => s.Debug?.Layout ?? false);
        set => SetSetting(static (s, v) => (s.Debug ??= new CalySettings.CalySettingsDebug()).Layout = v, value);
    }

    public bool DebugFps
    {
        get => GetSetting(static s => s.Debug?.Fps ?? false);
        set => SetSetting(static (s, v) => (s.Debug ??= new CalySettings.CalySettingsDebug()).Fps = v, value);
    }

    public bool DebugDirtyRects
    {
        get => GetSetting(static s => s.Debug?.DirtyRects ?? false);
        set => SetSetting(static (s, v) => (s.Debug ??= new CalySettings.CalySettingsDebug()).DirtyRects = v, value);
    }

    [RelayCommand]
    private static Task OpenLogsFolder()
    {
        Directory.CreateDirectory(JsonSettingsService.LogFilePath);
        return CalyExtensions.OpenDirectory(JsonSettingsService.LogFilePath);
    }

    private static bool GetSetting(Func<CalySettings, bool> read)
    {
        var settings = App.Current?.Services?.GetService<ISettingsService>()?.GetSettings();
        return settings is not null && read(settings);
    }

    private void SetSetting(Action<CalySettings, bool> write, bool value, [CallerMemberName] string? propertyName = null)
    {
        var service = App.Current?.Services?.GetService<ISettingsService>();
        var settings = service?.GetSettings();
        if (service is null || settings is null)
        {
            return;
        }

        write(settings, value);
        OnPropertyChanged(propertyName);
        service.Save();
    }
}
