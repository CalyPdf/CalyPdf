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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Caly.Avalonia.Pdf.Document.Internal;

internal static class InputExtensions
{
    public static bool IsMobilePlatform()
    {
        return OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();
    }

    /// <summary>
    /// Get the Viewport Rect to check if elements are visible or not.
    /// </summary>
    public static Rect GetViewportRect(this ScrollViewer sv)
    {
        return new Rect(sv.Offset.X, sv.Offset.Y, sv.Viewport.Width, sv.Viewport.Height);
    }

    public static T FindFromNameScope<T>(this INameScope e, string name) where T : Control
    {
        var element = e.Find<T>(name);
        return element ?? throw new NullReferenceException($"Could not find {name}.");
    }

    public static bool IsEmpty(this Rect rect)
    {
        return rect.Size.IsEmpty();
    }

    public static bool IsEmpty(this Size size)
    {
        return size.Height <= float.Epsilon || size.Width <= float.Epsilon;
    }

    public static bool IsPanning(this PointerEventArgs e)
    {
        if (!e.Properties.IsLeftButtonPressed)
        {
            return false;
        }

        var hotkeys = Application.Current!.PlatformSettings?.HotkeyConfiguration;
        return hotkeys is not null && e.KeyModifiers.HasFlag(hotkeys.CommandModifiers);
    }

    public static bool IsPanningOrZooming(this PointerEventArgs e)
    {
        var hotkeys = Application.Current!.PlatformSettings?.HotkeyConfiguration;
        return hotkeys is not null && e.KeyModifiers.HasFlag(hotkeys.CommandModifiers);
    }

    public static bool IsPanningOrZooming(this KeyEventArgs e)
    {
        var hotkeys = Application.Current!.PlatformSettings?.HotkeyConfiguration;
        return hotkeys is not null && e.KeyModifiers.HasFlag(hotkeys.CommandModifiers);
    }

    public static double Euclidean(this Point point1, Point point2)
    {
        double dx = point1.X - point2.X;
        double dy = point1.Y - point2.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
