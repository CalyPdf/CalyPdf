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
using System.Diagnostics;
using System.IO;
using Avalonia;
using Caly.Core.Services;

namespace Caly.Core;

public static class Debug
{
    /// <summary>
    /// Whether <see cref="Avalonia.Threading.Dispatcher.UIThread"/> identifies a real, dedicated
    /// UI thread. It does not hold under the headless unit-test host.
    /// </summary>
    private static bool HasDedicatedUiThread => Application.Current?.ApplicationLifetime is not null;

    [Conditional("DEBUG")]
    public static void ThrowOnUiThread()
    {
        if (HasDedicatedUiThread && Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            throw new InvalidOperationException("Call from UI thread");
        }
    }

    [Conditional("DEBUG")]
    public static void ThrowNotOnUiThread()
    {
        if (HasDedicatedUiThread && !Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            throw new InvalidOperationException("Call from non-UI thread");
        }
    }

    /// <summary>
    /// Where exception and render-timing logs are written. Defaults to the user's Caly logs
    /// folder; the test assemblies point it at a temp folder so that exceptions the tests
    /// raise on purpose do not end up in the real logs.
    /// </summary>
    internal static string LogDirectory { get; set; } = JsonSettingsService.LogFilePath;

    //[Conditional("DEBUG")]
    public static void WriteExceptionToFile(Exception? exception)
    {
        string logDirectory = LogDirectory;
        Directory.CreateDirectory(logDirectory);

        string logFile = Path.Combine(logDirectory, $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid()}.txt");
        if (exception is null)
        {
            File.WriteAllText(logFile, "Received null exception");
            return;
        }

        File.WriteAllText(logFile, exception.ToString());
    }

    /// <summary>
    /// Assert if the matrix is only a scale matrix (or null) and scale X equals scale Y.
    /// </summary>
    [Conditional("DEBUG")]
    public static void AssertIsNullOrScale(Matrix? matrix)
    {
        if (!matrix.HasValue) return;
        System.Diagnostics.Debug.Assert(!matrix.Value.ContainsPerspective());
        System.Diagnostics.Debug.Assert(matrix.Value.M12.Equals(0));
        System.Diagnostics.Debug.Assert(matrix.Value.M21.Equals(0));
        System.Diagnostics.Debug.Assert(matrix.Value.M21.Equals(0));
        System.Diagnostics.Debug.Assert(matrix.Value.M32.Equals(0));

        System.Diagnostics.Debug.Assert(matrix.Value.M11.Equals(matrix.Value.M22));
    }
}
