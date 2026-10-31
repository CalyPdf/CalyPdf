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
using System.Linq;
using Avalonia;
using Avalonia.Input;

namespace Caly.Core.Utilities;

internal static class CalyHotkeyConfiguration
{
    private static readonly KeyModifiers CommandModifiers;

    /// <summary>
    /// The modifier of the zoom shortcuts (the fit modes and actual size). See <see cref="GetZoomModifiers"/>.
    /// </summary>
    private static readonly KeyModifiers ZoomModifiers;

    static CalyHotkeyConfiguration()
    {
        if (Application.Current?.PlatformSettings is null)
        {
            throw new NullReferenceException("PlatformSettings is null.");
        }

        CommandModifiers = Application.Current.PlatformSettings.HotkeyConfiguration.CommandModifiers;
        ZoomModifiers = GetZoomModifiers(CommandModifiers);
    }

    /// <summary>
    /// The modifier of the zoom shortcuts on a platform whose command modifier is
    /// <paramref name="commandModifiers"/>: Alt, or Cmd where the command key is Cmd (macOS).
    /// </summary>
    internal static KeyModifiers GetZoomModifiers(KeyModifiers commandModifiers) =>
        commandModifiers == KeyModifiers.Meta ? KeyModifiers.Meta : KeyModifiers.Alt;

    /// <summary>
    /// Gets a platform-specific <see cref="KeyGesture"/> for the 'Copy' action.
    /// </summary>
    public static KeyGesture? CopyGesture => Application.Current?.PlatformSettings?.HotkeyConfiguration.Copy.FirstOrDefault();

    /// <summary>
    /// Gets a platform-specific <see cref="KeyGesture"/> for the 'Open File' action.
    /// </summary>
    public static KeyGesture OpenFileGesture => new KeyGesture(Key.O, CommandModifiers);

    /// <summary>
    /// Gets a platform-specific <see cref="KeyGesture"/> for the 'Print Document' action.
    /// </summary>
    public static KeyGesture DocumentPrintGesture => new KeyGesture(Key.P, CommandModifiers);

    /// <summary>
    /// Gets a platform-specific <see cref="KeyGesture"/> for the 'Go to Specific Page' action.
    /// </summary>
    public static KeyGesture DocumentGoToGesture => new KeyGesture(Key.G, CommandModifiers);

    /// <summary>
    /// Gets a platform-specific <see cref="KeyGesture"/> for the 'Zoom In' action.
    /// </summary>
    public static KeyGesture DocumentZoomInGesture => new KeyGesture(Key.OemPlus, CommandModifiers);

    /// <summary>
    /// Gets a platform-specific <see cref="KeyGesture"/> for the 'Zoom Out' action.
    /// </summary>
    public static KeyGesture DocumentZoomOutGesture => new KeyGesture(Key.OemMinus, CommandModifiers);

    /// <summary>
    /// Gets a platform-specific <see cref="KeyGesture"/> for the 'Fit Page' zoom mode.
    /// </summary>
    public static KeyGesture DocumentFitPageGesture => new KeyGesture(Key.D0, ZoomModifiers);

    /// <summary>
    /// Gets a platform-specific <see cref="KeyGesture"/> for the 'Actual Size' (100%) action.
    /// </summary>
    public static KeyGesture DocumentActualSizeGesture => new KeyGesture(Key.D1, ZoomModifiers);

    /// <summary>
    /// Gets a platform-specific <see cref="KeyGesture"/> for the 'Fit Width' zoom mode.
    /// </summary>
    public static KeyGesture DocumentFitWidthGesture => new KeyGesture(Key.D2, ZoomModifiers);

    /// <summary>
    /// Gets a platform-specific <see cref="KeyGesture"/> for the 'Fit Content' zoom mode.
    /// </summary>
    public static KeyGesture DocumentFitContentGesture => new KeyGesture(Key.D3, ZoomModifiers);

    /// <summary>
    /// Gets a platform-specific <see cref="KeyGesture"/> for the 'Fit Content Width' zoom mode.
    /// </summary>
    public static KeyGesture DocumentFitContentWidthGesture => new KeyGesture(Key.D4, ZoomModifiers);

    /// <summary>
    /// Gets a platform-specific <see cref="KeyGesture"/> for the 'Search in Document' action.
    /// </summary>
    public static KeyGesture DocumentSearchGesture => new KeyGesture(Key.F, CommandModifiers);

    /// <summary>
    /// Gets a platform-specific <see cref="KeyGesture"/> for toggling the settings pane.
    /// </summary>
    public static KeyGesture SettingsGesture => new KeyGesture(Key.OemComma, CommandModifiers);

    /// <summary>
    /// Gets a platform-specific <see cref="KeyGesture"/> for the 'Close Document' action.
    /// </summary>
    public static KeyGesture DocumentCloseGesture => new KeyGesture(Key.F4, CommandModifiers);

    /// <summary>
    /// Gets a platform-specific <see cref="KeyGesture"/> for the 'Go To Next Page' action.
    /// </summary>
    public static KeyGesture DocumentNextGesture => new KeyGesture(Key.PageDown, CommandModifiers);

    /// <summary>
    /// Gets a platform-specific <see cref="KeyGesture"/> for the 'Go To Previous Page' action.
    /// </summary>
    public static KeyGesture DocumentPreviousGesture => new KeyGesture(Key.PageUp, CommandModifiers);
}
