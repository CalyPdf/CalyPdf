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

using Caly.Core.Services.Interfaces;
using Caly.Core.ViewModels;
using Caly.Tests.Integration.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Caly.Tests.Integration;

public class DocumentViewModelTests : IClassFixture<AvaloniaDispatcherFixture>
{
    [Fact]
    public async Task Construct_FromContainer_Resolves()
    {
        await using var provider = TestServiceProvider.Build();
        await using var scope = provider.CreateAsyncScope();

        var vm = scope.ServiceProvider.GetRequiredService<DocumentViewModel>();
        Assert.NotNull(vm);
    }

    [Fact]
    public async Task Close_DisposesUnderlyingService()
    {
        await using var provider = TestServiceProvider.Build();
        var scope = provider.CreateAsyncScope();

        var pdf = scope.ServiceProvider.GetRequiredService<IPdfDocumentService>();
        var vm = scope.ServiceProvider.GetRequiredService<DocumentViewModel>();
        Assert.False(pdf.IsActive); // sanity: not opened yet

        await scope.DisposeAsync();
        Assert.False(pdf.IsActive);
    }
}
