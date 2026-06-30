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
using SkiaSharp;

namespace Caly.Avalonia.Pdf.Rendering;

/// <summary>
/// Public factory for creating ref-counted wrappers around disposable Skia objects,
/// principally <see cref="SKPicture"/> instances handed to the render controls.
/// </summary>
public static class PdfRef
{
    /// <summary>Wraps an <see cref="SKPicture"/> in a new ref-counted reference (refcount = 1).</summary>
    public static IRef<SKPicture> Create(SKPicture picture)
    {
        ArgumentNullException.ThrowIfNull(picture);
        return RefCountable.Create(picture);
    }

    /// <summary>Wraps any disposable object in a new ref-counted reference (refcount = 1).</summary>
    public static IRef<T> Create<T>(T item) where T : class, IDisposable
    {
        ArgumentNullException.ThrowIfNull(item);
        return RefCountable.Create(item);
    }
}
