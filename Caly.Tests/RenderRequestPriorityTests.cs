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

namespace Caly.Tests;

/// <summary>
/// What the reader is looking at must outrank background work, whatever the page numbers are.
/// Sorting by page number first meant thumbnails for earlier pages jumped ahead of the picture for
/// the page on screen - measured at over four seconds of blank page on a large document.
/// </summary>
public class RenderRequestPriorityTests
{
    private static int Compare(RenderRequestTypes xType, int xPage, RenderRequestTypes yType, int yPage)
        => RenderRequestPriority.Compare(xType, xPage, yType, yPage);

    [Fact]
    public void PictureForALaterPageBeatsThumbnailsForEarlierOnes()
    {
        Assert.True(Compare(RenderRequestTypes.Picture, 31, RenderRequestTypes.Thumbnail, 21) < 0);
        Assert.True(Compare(RenderRequestTypes.Thumbnail, 21, RenderRequestTypes.Picture, 31) > 0);
    }

    [Fact]
    public void TextLayerBeatsThumbnails()
    {
        Assert.True(Compare(RenderRequestTypes.TextLayer, 500, RenderRequestTypes.Thumbnail, 1) < 0);
    }

    [Fact]
    public void PageSizeBeatsEverything()
    {
        Assert.True(Compare(RenderRequestTypes.PageSize, 900, RenderRequestTypes.Picture, 1) < 0);
        Assert.True(Compare(RenderRequestTypes.PageSize, 900, RenderRequestTypes.TextLayer, 1) < 0);
        Assert.True(Compare(RenderRequestTypes.PageSize, 900, RenderRequestTypes.Thumbnail, 1) < 0);
    }

    [Fact]
    public void PictureBeatsTextLayerForTheSamePage()
    {
        Assert.True(Compare(RenderRequestTypes.Picture, 10, RenderRequestTypes.TextLayer, 10) < 0);
    }

    [Fact]
    public void WithinOneTypeTheEarlierPageGoesFirst()
    {
        Assert.True(Compare(RenderRequestTypes.Picture, 3, RenderRequestTypes.Picture, 9) < 0);
        Assert.True(Compare(RenderRequestTypes.Thumbnail, 9, RenderRequestTypes.Thumbnail, 3) > 0);
        Assert.Equal(0, Compare(RenderRequestTypes.Picture, 5, RenderRequestTypes.Picture, 5));
    }

    [Fact]
    public void QueueDrainsTheVisiblePageBeforeSidebarWork()
    {
        // The measured scenario: reading page 31 with thumbnails queued for 21-30.
        var queued = new List<(RenderRequestTypes Type, int Page)>();
        for (int p = 21; p <= 30; p++)
        {
            queued.Add((RenderRequestTypes.Thumbnail, p));
        }

        queued.Add((RenderRequestTypes.Picture, 31));
        queued.Add((RenderRequestTypes.TextLayer, 31));

        var order = queued
            .OrderBy(r => r, Comparer<(RenderRequestTypes Type, int Page)>.Create(
                (a, b) => Compare(a.Type, a.Page, b.Type, b.Page)))
            .ToList();

        Assert.Equal((RenderRequestTypes.Picture, 31), order[0]);
        Assert.Equal((RenderRequestTypes.TextLayer, 31), order[1]);
        Assert.All(order.Skip(2), r => Assert.Equal(RenderRequestTypes.Thumbnail, r.Type));
    }
}
