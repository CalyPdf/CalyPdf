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

using Caly.Core.Services;
using Caly.Core.Services.Interfaces;
using Caly.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Caly.Tests.Integration.Support;

/// <summary>
/// Minimal DI container for service+VM integration tests.
/// Does not register Visual/IStorageProvider/IClipboard — anything that
/// touches Avalonia UI plumbing belongs in Caly.UiTests, not here.
/// </summary>
internal static class TestServiceProvider
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISettingsService, TestSettingsService>();
        services.AddScoped<IPdfDocumentService, PdfPigDocumentService>();
        services.AddScoped<PdfPageService>();
        services.AddScoped<ITextSearchService, SearchValuesTextSearchService>();
        services.AddScoped<DocumentViewModel>();
        return services.BuildServiceProvider();
    }
}
