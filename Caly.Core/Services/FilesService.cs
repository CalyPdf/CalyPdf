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
using System.IO;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Caly.Core.Services.Interfaces;
using Caly.Core.Utilities;

namespace Caly.Core.Services;
// https://github.com/AvaloniaUI/AvaloniaUI.QuickGuides/blob/main/IoCFileOps/Services/FilesService.cs

internal sealed class FilesService : IFilesService
{
    private readonly IStorageProvider _storageProvider;
    private readonly IReadOnlyList<FilePickerFileType> _pdfFileFilter = [FilePickerFileTypes.Pdf];

    public FilesService(IStorageProvider? storageProvider)
    {
#if DEBUG
        if (global::Avalonia.Controls.Design.IsDesignMode)
        {
            _storageProvider = storageProvider!;
            return;
        }
#endif
        _storageProvider = storageProvider ?? throw new ArgumentNullException($"Could not find {typeof(IStorageProvider)}.");
        // TODO - Validate CanOpen, CanSave, CanPickFolder
    }

    public async Task<IStorageFile?> OpenPdfFileAsync()
    {
        Debug.ThrowNotOnUiThread();

        try
        {
            IReadOnlyList<IStorageFile> files = await _storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
            {
                Title = "Open",
                AllowMultiple = false,
                FileTypeFilter = _pdfFileFilter
            });

            return files.Count >= 1 ? files[0] : null;
        }
        catch (Exception e)
        {
            Debug.WriteExceptionToFile(e);
            return null;
        }
    }

    public async Task<IStorageFile?> SaveFileAsync(ReadOnlyMemory<byte> data, string? fileName = null)
    {
        try
        {
            fileName = Helpers.SanitiseFileName(fileName);
            
            var file = await _storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions()
            {
                Title = "Save File",
                SuggestedFileName = fileName,
                DefaultExtension = Path.GetExtension(fileName)
            }).ConfigureAwait(false);

            if (file is null)
            {
                // TODO - logs
                return null;
            }
            
            await using (var ms = await file.OpenWriteAsync().ConfigureAwait(false))
            {
                await ms.WriteAsync(data).ConfigureAwait(false);
                await ms.FlushAsync().ConfigureAwait(false);
            }

            return file;
        }
        catch (Exception e)
        {
            Debug.WriteExceptionToFile(e);
            return null;
        }
    }

    public async Task<IStorageFile?> SaveTempFileAsync(ReadOnlyMemory<byte> data, string? fileName = null)
    {
        try
        {
            string tempFilePath;
            string tempDirectory;

            if (string.IsNullOrEmpty(fileName))
            {
                string tempFile = Path.GetTempFileName();
                tempDirectory = Path.GetDirectoryName(tempFile) ?? string.Empty;
                if (string.IsNullOrEmpty(tempDirectory))
                {
                    tempDirectory = Path.GetTempPath();
                }

                tempFilePath = Path.GetFileName(tempFile);
            }
            else
            {
                tempDirectory = Path.GetTempPath();

                fileName = Helpers.SanitiseFileName(fileName);
                
                string extension = Path.GetExtension(fileName)!;
                string rootFileName = Path.GetFileNameWithoutExtension(fileName)!;
                tempFilePath = $"{rootFileName}{extension}";

                int i = 0;
                while (File.Exists(Path.Combine(tempDirectory, tempFilePath)))
                {
                    tempFilePath = $"{rootFileName}.{++i}{extension}";
                }
            }

            using var tempFolder = await _storageProvider.TryGetFolderFromPathAsync(tempDirectory);
            if (tempFolder is null)
            {
                return null;
            }

            var file = await tempFolder.CreateFileAsync(tempFilePath)
                .ConfigureAwait(false);
            
            if (file is null)
            {
                return null;
            }

            await using (var ms = await file.OpenWriteAsync().ConfigureAwait(false))
            {
                await ms.WriteAsync(data).ConfigureAwait(false);
                await ms.FlushAsync().ConfigureAwait(false);
            }

            return file;
        }
        catch (Exception e)
        {
            Debug.WriteExceptionToFile(e);
            return null;
        }
    }

    public async Task<IStorageFile?> TryGetFileFromPathAsync(string path)
    {
        try
        {
            // UIThread needed for Avalonia.FreeDesktop.DBusSystemDialog
            return await Dispatcher.UIThread.InvokeAsync(() => _storageProvider.TryGetFileFromPathAsync(path))
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine($"Could not get TopLevel in FilesService.TryGetFileFromPathAsync (path: '{path}').");
            Debug.WriteExceptionToFile(e);
            return null;
        }
    }
}
