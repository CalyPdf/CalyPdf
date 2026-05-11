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
using Caly.UiTests.Support;

namespace Caly.UiTests.Flows;

public class ScrollFlowTests : HeadlessTestBase
{
    [AvaloniaFact]
    public async Task SelectPage_UpdatesPageIndex()
    {
        var mainVm = GetMainViewModel();

        Assert.Empty(mainVm.PdfDocuments);

        await Task.Run(() => TestApp.OpenDocTest(Fixtures.Path("MOZILLA-11518-0.pdf"), CancellationToken.None));
        await Pump.UntilAsync(() => mainVm.PdfDocuments.Count == 1, TimeSpan.FromSeconds(10));

        var doc = mainVm.PdfDocuments[0];
        await Pump.UntilAsync(() => doc.PageCount > 1, TimeSpan.FromSeconds(10));

        Assert.Equal(1, doc.SelectedPageNumber);
        Assert.Equal(0, doc.SelectedPageIndex);

        doc.SelectedPageNumber = 2;
        await Pump.UntilAsync(() => doc.SelectedPageIndex == 1);
        Assert.Equal(2, doc.SelectedPageNumber);
        Assert.Equal(1, doc.SelectedPageIndex);

        doc.SelectedPageIndex = doc.PageCount - 1;
        await Pump.UntilAsync(() => doc.SelectedPageNumber == doc.PageCount);
        Assert.Equal(doc.PageCount, doc.SelectedPageNumber);

        Dialogs.AcceptExceptions();
    }
}
