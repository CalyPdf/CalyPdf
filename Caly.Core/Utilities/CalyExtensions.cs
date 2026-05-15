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
using System.Collections;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Caly.Core.Services;
using CommunityToolkit.Mvvm.Messaging;
using UglyToad.PdfPig.Core;

namespace Caly.Core.Utilities;

internal static class CalyExtensions
{
    public static readonly string CalyVersion;

    private static ReadOnlySpan<char> PdfExtension => ['.', 'p', 'd', 'f'];

    static CalyExtensions()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

        string? version = assembly.GetName().Version?.ToString().Trim();
        CalyVersion = !string.IsNullOrEmpty(version) ? version : @"n/a";
    }

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

    public static PdfRectangle ToPdfRectangle(this Rect rect)
    {
        return new PdfRectangle(rect.Left, rect.Bottom, rect.Right, rect.Top);
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

    internal static Task OpenFile(string? path)
    {
        return Task.Run(async () =>
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            if (!IsValidFilePath(path))
            {
                App.Messenger.Send(new ShowNotificationMessage(NotificationType.Warning,
                    "Invalid Path",
                    $"'{path}' is not valid."));
                return;
            }

            if (!File.Exists(path))
            {
                App.Messenger.Send(new ShowNotificationMessage(NotificationType.Warning,
                    "File Not Found",
                    $"'{path}' does not exist."));
                return;
            }

            if (path.IsPdf())
            {
                await OpenPdfDocument(path);
                return;
            }

            // We don't want to directly open any other file extension
            // as this could be harmful. We just open the directory.
            var directory = Path.GetDirectoryName(path);
            await OpenDirectory(directory);
        });
    }

    private static bool IsValidFilePath(ReadOnlySpan<char> path)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) != -1)
        {
            return false;
        }

        var dir = Path.GetDirectoryName(path);
        if (!IsValidPath(dir))
        {
            return false;
        }

        return true;
    }

    private static bool IsValidPath(ReadOnlySpan<char> path)
    {
        if (path.IndexOfAny(Path.GetInvalidPathChars()) != -1)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Open a pdf document with Caly.
    /// </summary>
    private static Task OpenPdfDocument(string? path)
    {
        return Task.Run(() =>
        {
            try
            {
                if (string.IsNullOrEmpty(path))
                {
                    return;
                }

                if (!File.Exists(path))
                {
                    App.Messenger.Send(new ShowNotificationMessage(NotificationType.Warning,
                        "File Not Found",
                        $"'{path}' does not exist."));
                    return;
                }

                if (!path.IsPdf())
                {
                    App.Messenger.Send(new ShowNotificationMessage(NotificationType.Warning,
                        "File is not a Pdf",
                        $"'{path}' is not a Pdf document."));
                    return;
                }

                if (FilePipeStream.SendPath(path))
                {
                    FilePipeStream.SendBringToFront();
                }
            }
            catch (Exception e)
            {
                Debug.WriteExceptionToFile(e);
            }
        });
    }

    internal static Task OpenDirectory(string? path)
    {
        return Task.Run(() =>
        {
            if (string.IsNullOrEmpty(path) || !IsValidPath(path) || !Directory.Exists(path))
            {
                return;
            }

            if (!IsValidPath(path))
            {
                App.Messenger.Send(new ShowNotificationMessage(NotificationType.Warning,
                    "Invalid Path",
                    $"'{path}' is not valid."));
                return;
            }

            if (!Directory.Exists(path))
            {
                App.Messenger.Send(new ShowNotificationMessage(NotificationType.Warning,
                    "Directory Not Found",
                    $"'{path}' does not exist."));
                return;
            }

            ProcessStart(path);
        });
    }

    /// <summary>
    /// Open a uri.
    /// <para>Warning: This is an <c>async void</c> method.</para>
    /// </summary>
    internal static async void OpenUriAsync(string? uri)
    {
        try
        {
            // https://en.wikipedia.org/wiki/Uniform_Resource_Identifier
            if (string.IsNullOrEmpty(uri))
            {
                return;
            }

            var index = uri.IndexOf(':');
            if (index == -1)
            {
                if (!uri.StartsWith("http"))
                {
                    // We only want to open http / https uris in this method.
                    // We force 'http'.
                    uri = $"http://{uri}";
                    index = 4;
                }
            }

            var scheme = uri.AsSpan(0, index);
            switch (scheme)
            {
                case "ftp":
                case "http":
                case "https":
                case "mailto":
                case "tel":
                case "imap":
                {
                    var uriObj = new Uri(uri);
                    await Task.Run(() => ProcessStart(uriObj.AbsoluteUri));
                }
                    break;

                case "file":
                {
                    var uriObj = new Uri(uri);
                    if (!string.IsNullOrWhiteSpace(uriObj.LocalPath))
                    {
                        await OpenFile(uriObj.LocalPath);
                    }
                    else
                    {
                        // Ignore non-local path
                    }
                }
                    break;
            }
        }
        catch (Exception e)
        {
            Debug.WriteExceptionToFile(e);
        }
    }

    /// <summary>
    /// Open a link (local path, url, etc.).
    /// <para>Warning - sanitise the input as this will execute anything passed.</para>
    /// </summary>
    private static void ProcessStart(string url)
    {
        // https://brockallen.com/2016/09/24/process-start-for-urls-on-net-core/

        // See https://docs.avaloniaui.net/docs/concepts/services/launcher to use avalonia to do so

        Debug.ThrowOnUiThread();

        try
        {
            Process.Start(url);
        }
        catch (Exception ex)
        {
            ProcessStartFallback(url);
        }
    }
    
    /// <summary>
    /// Warning - sanitise the input as this will execute anything passed.
    /// </summary>
    private static void ProcessStartFallback(string url)
    {
        try
        {
            // hack because of this: https://github.com/dotnet/corefx/issues/10361
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                url = url.Replace("&", "^&");
                Process.Start(new ProcessStartInfo("cmd", $"/c start \"\" \"{url}\"") { CreateNoWindow = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", url);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteExceptionToFile(ex);
            App.Messenger.Send(new ShowNotificationMessage(NotificationType.Error,
                $"Failed to open '{url}'.",
                ex.Message));
        }
    }

    internal static bool IsPdf(this ReadOnlySpan<char> path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Length == 4)
        {
            return extension.Equals(PdfExtension, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    /// <summary>
    /// The Euclidean distance is the "ordinary" straight-line distance between two points.
    /// </summary>
    /// <param name="point1">The first point.</param>
    /// <param name="point2">The second point.</param>
    public static double Euclidean(this Point point1, Point point2)
    {
        double dx = point1.X - point2.X;
        double dy = point1.Y - point2.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
