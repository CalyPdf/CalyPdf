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
/// Static factory for a Moq-based <see cref="IStorageFile"/> backed by a real file on disk.
/// <see cref="IStorageFile"/> is <c>[NotClientImplementable]</c>, so we use Moq (Castle.Core
/// proxies) to bypass the C# rule while still giving the production code a real stream to
/// read from.
/// </summary>
internal static class NullStorageFile
{
    public static IStorageFile Create(string path)
    {
        var fileInfo = new FileInfo(path);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("Test fixture not found.", path);
        }

        var mock = new Mock<IStorageFile>();
        mock.SetupGet(f => f.Name).Returns(fileInfo.Name);
        mock.SetupGet(f => f.Path).Returns(new Uri(fileInfo.FullName));
        mock.Setup(f => f.OpenReadAsync()).ReturnsAsync(() => File.OpenRead(fileInfo.FullName));
        return mock.Object;
    }
}
