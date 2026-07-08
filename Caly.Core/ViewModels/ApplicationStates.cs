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

using Caly.Core.Models;
using Caly.Core.Services.Interfaces;
using Caly.Core.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace Caly.Core.ViewModels;

/// <summary>
/// App-level states, shared across all documents. A single instance is
/// created at startup, handed to <see cref="MainViewModel"/> and registered in DI so
/// each <see cref="DocumentViewModel"/> receives the same one; views bind to it
/// through their own DataContext instead of casting an ancestor's.
/// </summary>
public sealed partial class ApplicationStates : ObservableObject
{
    /// <summary>
    /// Whether the document side pane is open.
    /// </summary>
    [ObservableProperty]
    public partial bool IsDocumentPaneOpen { get; set; } = !CalyExtensions.IsMobilePlatform();

    /// <summary>
    /// Width of the document side pane, persisted to settings.
    /// </summary>
    [ObservableProperty]
    public partial double PaneSize { get; set; }

    partial void OnPaneSizeChanged(double oldValue, double newValue)
    {
        App.Current?.Services?.GetService<ISettingsService>()?
            .SetProperty(CalySettings.CalySettingsProperty.PaneSize, newValue);
    }
}
