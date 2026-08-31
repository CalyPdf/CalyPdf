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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Caly.Core.ViewModels;

namespace Caly.Core.Services.Interfaces;

public interface IPdfDocumentsManagerService
{
    /// <summary>
    /// Open and load pdf document through popup window. The picker is shown over
    /// <paramref name="target"/>, and the document opens there; when it is <c>null</c> the
    /// active window is used for both.
    /// </summary>
    Task OpenLoadDocument(MainViewModel? target, CancellationToken cancellationToken);

    /// <summary>
    /// Open and load the pdf document in <paramref name="target"/>, or in the active window
    /// when the caller has no particular window in mind.
    /// </summary>
    Task OpenLoadDocument(IStorageFile? storageFile, MainViewModel? target, CancellationToken cancellationToken);

    /// <summary>
    /// Open and load the pdf documents in <paramref name="target"/>, or in the active window
    /// when the caller has no particular window in mind.
    /// </summary>
    Task<int> OpenLoadDocuments(IEnumerable<IStorageItem?> storageFiles, MainViewModel? target, CancellationToken cancellationToken);

    /// <summary>
    /// Open and load the pdf document.
    /// </summary>
    Task OpenLoadDocument(string? path, CancellationToken cancellationToken);

    Task CloseUnloadDocument(DocumentViewModel? document);
}
