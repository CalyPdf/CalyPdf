using Avalonia;
using Caly.Core.Controls;
using Caly.Core.Models;

namespace Caly.Tests;

/// <summary>
/// <see cref="ZoomFit"/> turns a fit mode into a zoom. The expected values are worked out by hand
/// from a 400×400 viewer with 16 px scroll bars that take room from the viewport (as in
/// PageItemsControl.axaml), the 10 px margin each fit keeps on either side of what it fits, and
/// the 1 px of slack each fit leaves so that layout rounding never tips the content over the viewport.
/// </summary>
public class ZoomFitTests
{
    private static readonly ZoomFitInput Continuous = new(
        Mode: ZoomMode.FitWidth,
        Viewport: new Size(400, 400),
        ScrollBarThickness: 16,
        Page: new Size(100, 100),
        Content: null,
        DocumentHeight: 2000,
        GapCount: 20,
        Gap: 0,
        MinZoom: 0.08,
        MaxZoom: 64);

    /// <summary>
    /// One 100×200 page, as in single-page view.
    /// </summary>
    private static readonly ZoomFitInput SinglePortraitPage = Continuous with
    {
        Page = new Size(100, 200),
        DocumentHeight = 200,
        GapCount = 1
    };

    private static double Fit(in ZoomFitInput input)
    {
        Assert.True(ZoomFit.TryCompute(input, out double zoom));
        return zoom;
    }

    [Fact]
    public void FitWidth_ContinuousDocument_ReservesTheVerticalBar()
    {
        // The pages overflow vertically, so the vertical bar takes 16 px: (400 - 16 - 2 × 10 - 1) / 100.
        Assert.Equal(3.63, Fit(Continuous), 6);
    }

    /// <summary>
    /// Fit width keeps the margin fit content width keeps: on a page whose content box is the
    /// whole page, the two agree.
    /// </summary>
    [Fact]
    public void FitWidth_KeepsTheContentWidthMargin()
    {
        var page = Continuous with { Content = new Rect(0, 0, 100, 100) };

        Assert.Equal(Fit(page with { Mode = ZoomMode.FitContentWidth }), Fit(page with { Mode = ZoomMode.FitWidth }), 6);
    }

    /// <summary>
    /// Fit page never zooms past fit width: when the page's width is what limits it, it keeps the
    /// same side margins.
    /// </summary>
    [Fact]
    public void FitPage_WidthLimited_IsFitWidth()
    {
        var landscape = SinglePortraitPage with { Page = new Size(200, 100), DocumentHeight = 100 };

        // (400 - 2 × 10 - 1) / 200: the page is 189.5 px tall, so no bar shows.
        Assert.Equal(1.895, Fit(landscape with { Mode = ZoomMode.FitWidth }), 6);
        Assert.Equal(1.895, Fit(landscape with { Mode = ZoomMode.FitPage }), 6);
    }

    [Fact]
    public void FitPage_PortraitPages_FitTheHeight()
    {
        var input = Continuous with { Mode = ZoomMode.FitPage, Page = new Size(100, 200), DocumentHeight = 4000 };

        // Height-limited: (400 - 1) / 200.
        Assert.Equal(1.995, Fit(input), 6);
    }

    [Fact]
    public void FitPage_SinglePage_KeepsThePageGap()
    {
        var input = SinglePortraitPage with { Mode = ZoomMode.FitPage, Gap = 5 };

        // (400 - 1 - 5) / 200: the page and the gap below it fit, so no bar shows.
        Assert.Equal(1.97, Fit(input), 6);
    }

    [Fact]
    public void FitWidth_BorderlineSinglePage_FallsBackToBothBars()
    {
        // At 3.79 the page is 409 px tall and needs the vertical bar; at 3.63 (with the bar's room
        // taken) it is 392 px tall and does not. No scroll bar state is consistent, so the fit keeps
        // the bars' room reserved rather than flip-flopping between the two.
        var input = Continuous with { Page = new Size(100, 108), DocumentHeight = 108, GapCount = 1 };

        Assert.Equal(3.63, Fit(input), 6);
    }

    [Fact]
    public void FitWidth_OverlaidScrollBars_UseTheWholeWidth()
    {
        var input = Continuous with { ScrollBarThickness = 0 };

        // (400 - 2 × 10 - 1) / 100
        Assert.Equal(3.79, Fit(input), 6);
    }

    [Fact]
    public void FitContent_FitsTheWholeBoxWithAMargin()
    {
        var input = SinglePortraitPage with { Mode = ZoomMode.FitContent, Content = new Rect(25, 50, 50, 100) };

        // Height-limited: (400 - 1 - 2 * 10) / 100. The page (379 px wide) still fits beside the bar.
        Assert.Equal(3.79, Fit(input), 6);
    }

    [Fact]
    public void FitContentWidth_FitsTheBoxWidth()
    {
        var input = SinglePortraitPage with { Mode = ZoomMode.FitContentWidth, Content = new Rect(25, 50, 50, 100) };

        // The page overflows both ways, so both bars show: (400 - 16 - 1 - 2 * 10) / 50.
        Assert.Equal(7.26, Fit(input), 6);
    }

    [Fact]
    public void ContentZoom_IsCappedAt800Percent()
    {
        var input = SinglePortraitPage with { Mode = ZoomMode.FitContent, Content = new Rect(50, 100, 1, 1) };

        Assert.Equal(8, Fit(input), 6);
    }

    [Fact]
    public void ContentCap_YieldsToAHigherPageZoom()
    {
        var input = Continuous with
        {
            Mode = ZoomMode.FitContent,
            Page = new Size(10, 10),
            Content = new Rect(4, 4, 1, 1),
            DocumentHeight = 10,
            GapCount = 1
        };

        // Fit page alone is already (400 - 2 × 10 - 1) / 10 = 37.9, above the 800% cap: content
        // never zooms out from what fitting the whole page gives.
        Assert.Equal(37.9, Fit(input), 6);
    }

    [Fact]
    public void FitContent_WithoutAContentBox_IsFitPage()
    {
        var input = SinglePortraitPage with { Mode = ZoomMode.FitContent, Content = null, Gap = 5 };

        Assert.Equal(1.97, Fit(input), 6);
    }

    [Fact]
    public void FitPage_TinyPage_IsClampedToMaxZoom()
    {
        var input = Continuous with { Mode = ZoomMode.FitPage, Page = new Size(1, 1), DocumentHeight = 1, GapCount = 1 };

        Assert.Equal(64, Fit(input), 6);
    }

    [Theory]
    [InlineData("empty viewport")]
    [InlineData("empty page")]
    [InlineData("manual")]
    public void TryCompute_NothingToFit_ReturnsFalse(string nothingToFit)
    {
        var input = nothingToFit switch
        {
            "empty viewport" => Continuous with { Viewport = default },
            "empty page" => Continuous with { Page = new Size(0, 100) },
            _ => Continuous with { Mode = ZoomMode.Manual }
        };

        Assert.False(ZoomFit.TryCompute(input, out _));
    }

    /// <summary>
    /// A zoom range no zoom fits in is a broken configuration: no fit, rather than an exception
    /// from a posted layout job or a zoom outside the range.
    /// </summary>
    [Theory]
    [InlineData(2, 1)]
    [InlineData(double.NaN, 64)]
    [InlineData(0.08, double.NaN)]
    public void TryCompute_InvalidZoomRange_ReturnsFalse(double minZoom, double maxZoom)
    {
        var input = Continuous with { MinZoom = minZoom, MaxZoom = maxZoom };

        Assert.False(ZoomFit.TryCompute(input, out _));
    }
}
