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

using Avalonia.Platform.Storage;
using Moq;

namespace Caly.UiTests.TestDoubles;

/// <summary>
/// Static factory for a Moq-based <see cref="IStorageProvider"/> wired so that
/// <see cref="IStorageProvider.TryGetFileFromPathAsync"/> returns a <see cref="NullStorageFile"/>
/// when the given uri points to an existing file on disk.
/// </summary>
internal static class NullStorageProvider
{
    public static IStorageProvider Create()
    {
        var mock = new Mock<IStorageProvider>();
        mock.Setup(p => p.TryGetFileFromPathAsync(It.IsAny<Uri>()))
            .Returns<Uri>(uri =>
            {
                var local = uri.IsAbsoluteUri ? uri.LocalPath : uri.OriginalString;
                if (!File.Exists(local))
                {
                    return Task.FromResult<IStorageFile?>(null);
                }

                return Task.FromResult<IStorageFile?>(NullStorageFile.Create(local));
            });
        return mock.Object;
    }
}
