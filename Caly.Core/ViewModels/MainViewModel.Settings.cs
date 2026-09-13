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

/// <summary>
/// The settings pane's bindings: one property per user-visible setting, plus the commands the pane
/// offers. Split out of <c>MainViewModel.cs</c> the same way <c>DocumentViewModel</c> is divided by
/// concern, so adding a settings category does not grow the main file.
/// </summary>
/// <remarks>
/// These are deliberately plain CLR properties rather than <c>[ObservableProperty]</c> fields: the
/// backing store is the shared <see cref="CalySettings"/> instance owned by
/// <see cref="ISettingsService"/>, not a field on this view model.
/// </remarks>
public sealed partial class MainViewModel
{
    /// <summary>
    /// Passes a logger to PdfPig while parsing.
    /// <para>
    /// Read by <c>PdfPigDocumentService</c> each time a document is opened, so a change applies to
    /// the next document rather than the next launch.
    /// </para>
    /// </summary>
    public bool ShowPdfLogs
    {
        get => GetSetting(static s => s.ShowPdfLogs);
        set => SetSetting(static (s, v) => s.ShowPdfLogs = v, value);
    }

    /*
     * Avalonia renderer overlays. Each maps to a RendererDebugOverlays flag that JsonSettingsService
     * applies in the window's Opened handler, so they land at the next launch.
     *
     * CalySettings.Debug is null by default, hence the null-safe read and the ??= on write. Once
     * created it stays non-null even if every overlay is turned back off - the empty object is
     * harmless, and collapsing it back to null is not worth the extra branch.
     */

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

    /// <summary>
    /// Opens the folder holding the crash logs.
    /// </summary>
    /// <remarks>
    /// Creates the folder first: nothing writes there until something is actually logged, so on a
    /// clean install it does not exist yet and <c>OpenDirectory</c> would silently do nothing.
    /// </remarks>
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

    /// <summary>
    /// Writes a flag straight onto the settings instance and persists it.
    /// </summary>
    /// <remarks>
    /// Deliberately not routed through <see cref="ISettingsService.SetProperty"/>: that switch only
    /// handles <c>PaneSize</c> and silently ignores anything else, so a toggle would appear to work
    /// and change nothing. Saved on the spot because <c>Save</c> is otherwise only called from the
    /// last window's closing handler, which a force-kill never reaches.
    /// </remarks>
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
