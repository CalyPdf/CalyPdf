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
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace Caly.Core.ViewModels;

public partial class DocumentViewModel
{
    private readonly Lazy<Task<DocumentPropertiesViewModel?>> _propertiesTask;
    public Task<DocumentPropertiesViewModel?> Properties => _propertiesTask.Value;

    private readonly Lazy<Task<IReadOnlyList<PdfEmbeddedFileViewModel>>> _embeddedFilesTask;
    public Task<IReadOnlyList<PdfEmbeddedFileViewModel>> EmbeddedFiles => _embeddedFilesTask.Value;

    private async Task<DocumentPropertiesViewModel?> GetProperties()
    {
        try
        {
            _mainToken.ThrowIfCancellationRequested();
            return await Task.Run(() => _pdfService.GetDocumentPropertiesAsync(_mainToken), _mainToken);
        }
        catch (OperationCanceledException)
        { /* No op */ }

        return null;
    }

    private async Task<IReadOnlyList<PdfEmbeddedFileViewModel>> GetEmbeddedFiles()
    {
        try
        {
            _mainToken.ThrowIfCancellationRequested();
            var items = await Task.Run(() => _pdfService.GetEmbeddedFilesAsync(_mainToken), _mainToken);
            if (items is not null && items.Count > 0)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (IsPortfolio)
                    {
                        // Select 'Embedded Files' tab
                        SelectedTabIndex = 4;
                    }
                });
                return items;
            }
        }
        catch (OperationCanceledException)
        { /* No op */ }

        return [];
    }
}
