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

using System.Collections.Generic;
using System.Windows.Input;
using Avalonia;
using Caly.Core.Models;
using Caly.Core.Services.Rendering;
using Caly.Core.ViewModels;
using CommunityToolkit.Mvvm.Input;

namespace Caly.Tests;

/// <summary>
/// A visible page with nothing to draw yet is "loading", whether or not a worker has picked its
/// render up. Render requests can sit queued for seconds behind thumbnail work, and the page is
/// just as blank during that wait - the loading skeleton has to cover it.
/// </summary>
public class PageViewModelLoadingTests
{
    private static PageViewModel CreatePage()
    {
        return new PageViewModel(
            pageNumber: 1,
            textSelection: new TextSelection(1),
            tileRenderService: new TileRenderService(),
            ppiScale: 1.0,
            copyTextCommand: new RelayCommand(() => { }));
    }

    private static PageViewModel VisiblePage()
    {
        var page = CreatePage();
        page.VisibleArea = new Rect(0, 0, 100, 100);
        return page;
    }

    [Fact]
    public void VisiblePageWithoutAPictureIsLoading()
    {
        Assert.True(VisiblePage().IsPageLoading);
    }

    [Fact]
    public void PageStopsLoadingOnceItHasAPicture()
    {
        var page = VisiblePage();

        page.PdfPicture = FakePicture();

        Assert.False(page.IsPageLoading);
    }

    [Fact]
    public void OffScreenPageIsNotLoading()
    {
        var page = CreatePage();

        Assert.False(page.IsPageLoading);
    }

    [Fact]
    public void LoadingIsRaisedWhenThePictureArrives()
    {
        var page = VisiblePage();
        var raised = new List<string?>();
        page.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        page.PdfPicture = FakePicture();

        Assert.Contains(nameof(PageViewModel.IsPageLoading), raised);
    }

    [Fact]
    public void LoadingIsRaisedWhenThePageBecomesVisible()
    {
        var page = CreatePage();
        var raised = new List<string?>();
        page.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        page.VisibleArea = new Rect(0, 0, 100, 100);

        Assert.Contains(nameof(PageViewModel.IsPageLoading), raised);
    }

    private static Caly.Core.Utilities.IRef<SkiaSharp.SKPicture> FakePicture()
    {
        using var recorder = new SkiaSharp.SKPictureRecorder();
        recorder.BeginRecording(SkiaSharp.SKRect.Create(10, 10));
        return Caly.Core.Utilities.RefCountable.Create(recorder.EndRecording());
    }
}
