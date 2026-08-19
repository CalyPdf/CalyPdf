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
using Caly.Core.Controls;

namespace Caly.Tests;

public class PageLoadingSkeletonLayoutTests
{
    private static readonly Size A4 = new(595, 842);

    [Fact]
    public void BlocksStayInsideThePage()
    {
        var blocks = PageLoadingSkeletonLayout.Build(A4);

        Assert.NotEmpty(blocks);
        Assert.All(blocks, b =>
        {
            Assert.True(b.X >= 0 && b.Y >= 0, $"{b} starts outside the page");
            Assert.True(b.Right <= A4.Width, $"{b} overflows the page width");
            Assert.True(b.Bottom <= A4.Height, $"{b} overflows the page height");
            Assert.True(b.Width > 0 && b.Height > 0, $"{b} is degenerate");
        });
    }

    [Fact]
    public void BlocksRespectAPageMargin()
    {
        var blocks = PageLoadingSkeletonLayout.Build(A4);

        double left = blocks.Min(b => b.X);
        double right = blocks.Max(b => b.Right);
        double top = blocks.Min(b => b.Y);

        Assert.InRange(left / A4.Width, 0.04, 0.15);
        Assert.InRange(1.0 - (right / A4.Width), 0.04, 0.15);
        Assert.InRange(top / A4.Height, 0.03, 0.12);
    }

    [Fact]
    public void BodyTextIsLaidOutInTwoNonOverlappingColumns()
    {
        var blocks = PageLoadingSkeletonLayout.Build(A4);

        // The heading spans the left column and beyond, so exclude the topmost block.
        double headingBottom = blocks.Min(b => b.Bottom);
        var body = blocks.Where(b => b.Y > headingBottom).ToList();
        Assert.NotEmpty(body);

        double mid = A4.Width / 2.0;
        var leftColumn = body.Where(b => b.X < mid).ToList();
        var rightColumn = body.Where(b => b.X >= mid).ToList();

        Assert.NotEmpty(leftColumn);
        Assert.NotEmpty(rightColumn);

        // A gutter separates them: nothing from the left column reaches the right one.
        Assert.True(leftColumn.Max(b => b.Right) < rightColumn.Min(b => b.X),
            "left and right columns overlap - there is no gutter");
    }

    [Fact]
    public void LayoutScalesWithThePage()
    {
        var single = PageLoadingSkeletonLayout.Build(A4);
        var doubled = PageLoadingSkeletonLayout.Build(new Size(A4.Width * 2, A4.Height * 2));

        Assert.Equal(single.Count, doubled.Count);
        for (int i = 0; i < single.Count; i++)
        {
            Assert.Equal(single[i].X * 2, doubled[i].X, 6);
            Assert.Equal(single[i].Y * 2, doubled[i].Y, 6);
            Assert.Equal(single[i].Width * 2, doubled[i].Width, 6);
            Assert.Equal(single[i].Height * 2, doubled[i].Height, 6);
        }
    }

    [Fact]
    public void ParagraphsEndWithAShortLine()
    {
        var blocks = PageLoadingSkeletonLayout.Build(A4);

        // Several lines are noticeably shorter than the full column width - the paragraph ends.
        double widest = blocks.Max(b => b.Width);
        int shortLines = blocks.Count(b => b.Width < widest * 0.8);

        Assert.True(shortLines >= 4, $"expected several short trailing lines, found {shortLines}");
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-10, 100)]
    [InlineData(100, 0)]
    public void DegeneratePageSizesProduceNothing(double width, double height)
    {
        Assert.Empty(PageLoadingSkeletonLayout.Build(new Size(width, height)));
    }
}
