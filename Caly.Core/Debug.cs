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
using System.Diagnostics;
using System.IO;
using Avalonia;
using Caly.Core.Services;

namespace Caly.Core;

public static class Debug
{
    [Conditional("DEBUG")]
    public static void ThrowOnUiThread()
    {
        if (global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            throw new InvalidOperationException("Call from UI thread");
        }
    }

    [Conditional("DEBUG")]
    public static void ThrowNotOnUiThread()
    {
        if (!global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            throw new InvalidOperationException("Call from non-UI thread");
        }
    }

    //[Conditional("DEBUG")]
    public static void WriteExceptionToFile(Exception? exception)
    {
        Directory.CreateDirectory(JsonSettingsService.LogFilePath);

        string logFile = Path.Combine(JsonSettingsService.LogFilePath, $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid()}.txt");
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
