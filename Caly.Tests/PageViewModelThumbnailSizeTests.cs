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

using Avalonia;
using Caly.Core.Models;
using Caly.Core.Services.Rendering;
using Caly.Core.ViewModels;
using CommunityToolkit.Mvvm.Input;

namespace Caly.Tests;

public class PageViewModelThumbnailSizeTests
{
    private const int Height = 135;
    private const int MaxWidth = 135;

    private static PageViewModel PageOfSize(double width, double height)
    {
        var page = new PageViewModel(
            pageNumber: 1,
            textSelection: new TextSelection(1),
            tileRenderService: new TileRenderService(),
            ppiScale: 1.0,
            copyTextCommand: new RelayCommand(() => { }));

        page.Size = new Size(width, height);
        return page;
    }

    [Theory]
    [InlineData(595, 842)]   // A4 portrait
    [InlineData(612, 792)]   // US Letter portrait
    [InlineData(500, 500)]   // square
    [InlineData(792, 612)]   // US Letter landscape
    [InlineData(842, 595)]   // A4 landscape
    [InlineData(2000, 500)]  // panoramic spread
    [InlineData(10000, 100)] // extreme
    public void ThumbnailIsNeverWiderThanTheMaximum(double width, double height)
    {
        Assert.True(PageOfSize(width, height).ThumbnailSize.Width <= MaxWidth);
    }

    [Fact]
    public void PortraitPageKeepsTheFullThumbnailHeight()
    {
        var size = PageOfSize(595, 842).ThumbnailSize;

        Assert.Equal(new PixelSize(95, Height), size);
    }

    [Fact]
    public void SquarePageFillsTheThumbnailBox()
    {
        // The cap equals the height, so a square page is the widest one that is not clamped.
        var size = PageOfSize(500, 500).ThumbnailSize;

        Assert.Equal(new PixelSize(MaxWidth, Height), size);
    }

    [Fact]
    public void LandscapePageIsClampedAndLosesHeightToKeepItsAspectRatio()
    {
        // A4 landscape: 191px wide at full height, so it clamps to 135 x 95.
        var size = PageOfSize(842, 595).ThumbnailSize;

        Assert.Equal(new PixelSize(MaxWidth, 95), size);
    }

    [Fact]
    public void PanoramicPageIsClampedToASliverOfTheFullHeight()
    {
        // 4:1 page: 540px wide at full height, so it clamps to 135 x 33.
        var size = PageOfSize(2000, 500).ThumbnailSize;

        Assert.Equal(new PixelSize(MaxWidth, 33), size);
    }

    [Fact]
    public void ClampedThumbnailKeepsThePageAspectRatio()
    {
        var page = PageOfSize(842, 595); // A4 landscape, clamped
        var size = page.ThumbnailSize;

        Assert.Equal(page.Size.AspectRatio, size.Width / (double)size.Height, 1);
    }

    [Fact]
    public void ThumbnailSizeStaysPositiveForADegeneratePage()
    {
        // An unset or zero page size gives a NaN / infinite aspect ratio - the
        // thumbnail still has to be a size Avalonia can lay out.
        foreach (var page in new[] { PageOfSize(0, 0), PageOfSize(100, 0), PageOfSize(0, 100) })
        {
            var size = page.ThumbnailSize;

            Assert.True(size.Width >= 1, $"Width was {size.Width}");
            Assert.True(size.Height >= 1, $"Height was {size.Height}");
            Assert.True(size.Width <= MaxWidth, $"Width was {size.Width}");
        }
    }

    [Fact]
    public void ThumbnailSizeIsRaisedWhenThePageSizeIsSet()
    {
        var page = PageOfSize(0, 0);
        bool raised = false;
        page.PropertyChanged += (_, e) => raised |= e.PropertyName == nameof(PageViewModel.ThumbnailSize);

        page.Size = new Size(2000, 500);

        Assert.True(raised);
    }
}
