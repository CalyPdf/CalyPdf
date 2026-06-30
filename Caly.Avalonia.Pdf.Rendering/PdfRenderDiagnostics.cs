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

namespace Caly.Avalonia.Pdf.Rendering;

/// <summary>
/// Global, optional sink for exceptions caught on background/render threads inside the
/// rendering controls. Host applications may set <see cref="ExceptionLogger"/> to record them.
/// </summary>
public static class PdfRenderDiagnostics
{
    /// <summary>Optional handler invoked for render/background-thread exceptions. May run off the UI thread.</summary>
    public static Action<Exception>? ExceptionLogger { get; set; }

    /// <summary>Reports an exception to <see cref="ExceptionLogger"/>; never throws.</summary>
    public static void Report(Exception e)
    {
        try
        {
            ExceptionLogger?.Invoke(e);
        }
        catch
        {
            // A faulty logger must never destabilise the render pipeline.
        }
    }
}
