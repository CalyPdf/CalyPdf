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

using Avalonia.Headless.XUnit;
using Caly.Core.Services;
using Caly.Core.ViewModels;
using Caly.Tests.Fakes;

namespace Caly.Tests;

/// <summary>
/// A real production log once caught <c>System.InvalidOperationException: "Document has not
/// been loaded yet."</c> from <see cref="DocumentViewModel.LoadPages"/>, raised via
/// <c>MainViewModel</c>'s reactive subscription evaluating <see cref="DocumentViewModel.LoadPagesTask"/>
/// before <see cref="DocumentViewModel.LoadDocument(Avalonia.Platform.Storage.IStorageFile?, string?, System.Threading.CancellationToken)"/>
/// had run for that document. Back when <c>LoadPagesTask</c> pulled its work from a
/// <see cref="Lazy{T}"/>, that first (premature) evaluation permanently cached the faulted task
/// it produced - <c>Pages</c> would then never populate for that document again, even once
/// loading did eventually succeed.
/// <para>
/// <see cref="DocumentViewModel.LoadDocumentCore"/> now pushes <see cref="DocumentViewModel.LoadPages"/>
/// itself, once loading is known to have succeeded, instead of leaving it to be pulled lazily by
/// whoever first reads <c>LoadPagesTask</c>. Reading that property early can no longer trigger -
/// or corrupt - the work at all; it can only wait.
/// </para>
/// </summary>
public class DocumentLoadRaceTests
{
    [AvaloniaFact]
    public async Task LoadPagesTask_EvaluatedBeforeLoadDocument_WaitsInsteadOfFailing()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = new DocumentViewModel(pdfService, pageService, new NoopTextSearchService());

        // Evaluate LoadPagesTask - the only thing MainViewModel.cs ever does with it - before
        // LoadDocumentCore has decided anything. This is the exact race the production log
        // caught; it must merely wait, never fault.
        Task loadPagesTask = document.LoadPagesTask;

        await Task.Delay(150);
        Assert.False(loadPagesTask.IsCompleted,
            "LoadPagesTask to still be waiting for LoadDocumentCore rather than already faulted");
    }
}
