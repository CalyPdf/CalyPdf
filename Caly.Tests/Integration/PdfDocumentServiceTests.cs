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
using Caly.Core.Services;
using Caly.Core.Services.Interfaces;
using Caly.Tests.Integration.Support;

namespace Caly.Tests.Integration;

public class PdfDocumentServiceTests : IClassFixture<AvaloniaDispatcherFixture>
{
    [Fact]
    public async Task OpenDocument_Algo_ReturnsExpectedPageCount()
    {
        await using IPdfDocumentService svc = new PdfPigDocumentService();
        var file = CreateLocalStorageFile(Fixtures.Path("algo.pdf"));

        var pageCount = await svc.OpenDocument(file, password: null, CancellationToken.None);

        Assert.True(pageCount > 0, "Expected at least one page");
        Assert.Equal(pageCount, svc.NumberOfPages);
        Assert.Equal("algo", svc.FileName);
        Assert.False(svc.IsPasswordProtected);
    }

    [Fact]
    public async Task OpenDocument_PublisherError_StillOpens()
    {
        await using IPdfDocumentService svc = new PdfPigDocumentService();
        var file = CreateLocalStorageFile(Fixtures.Path("Document-PublisherError-1.pdf"));

        var pageCount = await svc.OpenDocument(file, password: null, CancellationToken.None);

        Assert.True(pageCount > 0);
    }

    [Fact]
    public async Task OpenDocument_Properties_RoundTrip()
    {
        await using IPdfDocumentService svc = new PdfPigDocumentService();
        var file = CreateLocalStorageFile(Fixtures.Path("algo.pdf"));
        await svc.OpenDocument(file, password: null, CancellationToken.None);

        var properties = await svc.GetDocumentPropertiesAsync(CancellationToken.None);

        Assert.NotNull(properties);
        Assert.False(string.IsNullOrEmpty(properties!.PdfVersion));
    }

    [Fact]
    public async Task Dispose_ReleasesFileHandle()
    {
        var path = Fixtures.Path("algo.pdf");
        IPdfDocumentService first = new PdfPigDocumentService();
        await first.OpenDocument(CreateLocalStorageFile(path), null, CancellationToken.None);
        await first.DisposeAsync();

        await using IPdfDocumentService second = new PdfPigDocumentService();
        var pageCount = await second.OpenDocument(CreateLocalStorageFile(path), null, CancellationToken.None);

        Assert.True(pageCount > 0);
    }

    [Fact]
    public async Task OpenDocument_NullStorageFile_ReturnsZero()
    {
        await using IPdfDocumentService svc = new PdfPigDocumentService();

        var pageCount = await svc.OpenDocument(storageFile: null, password: null, CancellationToken.None);

        Assert.Equal(0, pageCount);
    }

    /// <summary>
    /// Creates an <see cref="IStorageFile"/> for a local path by instantiating the internal
    /// <c>BclStorageFile</c> from Avalonia via reflection. This is necessary because
    /// <see cref="IStorageFile"/> is decorated with <c>[NotClientImplementable]</c> in the
    /// Avalonia ref assembly, which prevents direct user-code implementations at compile time.
    /// </summary>
    private static IStorageFile CreateLocalStorageFile(string path)
    {
        var fileInfo = new FileInfo(path);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("Test fixture not found.", path);
        }

        // Avalonia.Platform.Storage.FileIO.BclStorageFile(FileInfo) is internal but fully
        // implements IStorageFile at runtime. We bypass the compile-time restriction via reflection.
        var avaloniaBase = typeof(IStorageFile).Assembly;
        var bclType = avaloniaBase.GetType(
            "Avalonia.Platform.Storage.FileIO.BclStorageFile",
            throwOnError: true)!;

        var ctor = bclType.GetConstructor(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
            binder: null,
            types: [typeof(FileInfo)],
            modifiers: null)
            ?? throw new InvalidOperationException("BclStorageFile(FileInfo) constructor not found.");

        return (IStorageFile)ctor.Invoke([fileInfo]);
    }
}
