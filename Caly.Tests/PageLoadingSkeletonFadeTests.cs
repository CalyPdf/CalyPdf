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

using System;
using Caly.Core.Controls;

namespace Caly.Tests;

/// <summary>
/// The skeleton fades in rather than appearing at once, so a page that renders quickly never shows
/// a fully drawn skeleton for a fraction of a second.
/// </summary>
public class PageLoadingSkeletonFadeTests
{
    [Fact]
    public void StartsInvisible()
    {
        Assert.Equal(0.0, PageLoadingSkeleton.FadeInOpacity(TimeSpan.Zero));
    }

    [Fact]
    public void IsFullyVisibleAfterFiveHundredMilliseconds()
    {
        Assert.Equal(1.0, PageLoadingSkeleton.FadeInOpacity(TimeSpan.FromMilliseconds(500)));
    }

    [Fact]
    public void StaysFullyVisibleAfterwards()
    {
        Assert.Equal(1.0, PageLoadingSkeleton.FadeInOpacity(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void IsStillFaintWhenAPageRendersQuickly()
    {
        // The case this exists for: a page that renders in 100ms should barely have shown anything.
        double atHundredMs = PageLoadingSkeleton.FadeInOpacity(TimeSpan.FromMilliseconds(100));

        Assert.InRange(atHundredMs, 0.0, 0.25);
    }

    [Fact]
    public void RampsUpMonotonically()
    {
        double previous = -1;
        for (int ms = 0; ms <= 600; ms += 25)
        {
            double current = PageLoadingSkeleton.FadeInOpacity(TimeSpan.FromMilliseconds(ms));

            Assert.InRange(current, 0.0, 1.0);
            Assert.True(current >= previous, $"fade went backwards at {ms}ms");
            previous = current;
        }
    }

    [Fact]
    public void NegativeElapsedIsTreatedAsNotStarted()
    {
        Assert.Equal(0.0, PageLoadingSkeleton.FadeInOpacity(TimeSpan.FromMilliseconds(-50)));
    }
}
