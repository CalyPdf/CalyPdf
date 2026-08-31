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
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Caly.Core.Services.Interfaces;

public interface IFilesService
{
    /// <summary>
    /// Shows the open-PDF picker over <paramref name="owner"/>, or over the window the user is
    /// working in when the caller has no particular window in mind.
    /// </summary>
    Task<IStorageFile?> OpenPdfFileAsync(Window? owner = null);

    Task<IStorageFile?> SaveFileAsync(ReadOnlyMemory<byte> data, string? fileName = null);

    Task<IStorageFile?> SaveTempFileAsync(ReadOnlyMemory<byte> data, string? fileName = null);

    Task<IStorageFile?> TryGetFileFromPathAsync(string path);
}
