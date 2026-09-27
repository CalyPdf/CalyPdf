using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Caly.Core.Controls;
using static Caly.Tests.Fakes.PageItemsControlTestHost;

namespace Caly.Tests;

/// <summary>
/// Go-to offsets on rotated pages. A point on the unrotated page must land where the page's
/// clockwise <c>RotateTransform</c> draws it.
/// </summary>
public class PageItemsControlRotationOffsetTests
{
    // Unrotated page: 200 wide x 400 tall. Point of interest: 30 from the left, 50 from the top
    // (PDF y = 400 - 50 = 350).
    private const double UnrotatedWidth = 200;
    private const double UnrotatedHeight = 400;
    private const double U = 30;
    private const double V = 50;
    private const double PdfY = UnrotatedHeight - V;

    public static TheoryData<int, double, double> ExpectedDisplayPoints => new()
    {
        // Rotation, display X, display Y
        { 0, U, V },
        // 90 clockwise: the unrotated top edge becomes the right edge, the left edge becomes the top.
        { 90, UnrotatedHeight - V, U },
        { 180, UnrotatedWidth - U, UnrotatedHeight - V },
        // 270 clockwise: the unrotated top edge becomes the left edge, the left edge becomes the bottom.
        { 270, V, UnrotatedWidth - U }
    };

    private static Size DisplaySize(int rotation) => rotation is 0 or 180
        ? new Size(UnrotatedWidth, UnrotatedHeight)
        : new Size(UnrotatedHeight, UnrotatedWidth);

    [Theory]
    [MemberData(nameof(ExpectedDisplayPoints))]
    public void ToDisplayOffsets_UnrotatedPage(int rotation, double expectedX, double expectedY)
    {
        var (x, y) = PageItemsControl.ToDisplayOffsets(rotation, DisplaySize(rotation), U, V, isPdf: false);

        Assert.Equal(expectedX, x);
        Assert.Equal(expectedY, y);
    }

    [Theory]
    [MemberData(nameof(ExpectedDisplayPoints))]
    public void ToDisplayOffsets_Pdf(int rotation, double expectedX, double expectedY)
    {
        var (x, y) = PageItemsControl.ToDisplayOffsets(rotation, DisplaySize(rotation), U, PdfY, isPdf: true);

        Assert.Equal(expectedX, x);
        Assert.Equal(expectedY, y);
    }

    /// <summary>
    /// Grounds <see cref="ExpectedDisplayPoints"/> in Avalonia's layout: PageItem.axaml rotates the
    /// unrotated page with a <see cref="LayoutTransformControl"/> holding a <see cref="RotateTransform"/>.
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(ExpectedDisplayPoints))]
    public void ExpectedDisplayPoints_MatchAvaloniaRotateTransform(int rotation, double expectedX, double expectedY)
    {
        var page = new Canvas { Width = UnrotatedWidth, Height = UnrotatedHeight, UseLayoutRounding = false };
        var rotated = new LayoutTransformControl
        {
            UseLayoutRounding = false, // As PageItem.axaml
            LayoutTransform = new RotateTransform(rotation),
            Child = page
        };
        var window = new Window { Width = 1000, Height = 1000, Content = new Canvas { Children = { rotated } } };
        window.Show();
        RunLayout();

        Point? display = page.TranslatePoint(new Point(U, V), rotated);

        Assert.NotNull(display);
        Assert.Equal(expectedX, display.Value.X, 3); // The transform matrix is single-precision
        Assert.Equal(expectedY, display.Value.Y, 3);
    }

    [Theory]
    [MemberData(nameof(ExpectedDisplayPoints))]
    public void ToUnrotatedPagePoint_IsTheInverseOfToDisplayOffsets(int rotation, double displayX, double displayY)
    {
        // The page is placed away from the document origin: the mapping is relative to its bounds.
        var bounds = new Rect(new Point(1000, 2000), DisplaySize(rotation));

        Point unrotated = PageItemsControl.ToUnrotatedPagePoint(rotation, bounds,
            new Point(bounds.Left + displayX, bounds.Top + displayY));

        Assert.Equal(U, unrotated.X, 6);
        Assert.Equal(V, unrotated.Y, 6);
    }

    [Fact]
    public void ToUnrotatedPagePoint_ClampsToThePage()
    {
        var bounds = new Rect(0, 1000, UnrotatedWidth, UnrotatedHeight);

        Point unrotated = PageItemsControl.ToUnrotatedPagePoint(0, bounds, new Point(-50, 5000));

        Assert.Equal(new Point(0, UnrotatedHeight), unrotated);
    }

    // Same stand-in as below: 100 x 1000 pages, which are 1000 x 100 landscape pages at 90 and 270.
    // GoToPage(3, 300) puts the viewport (400 high) over 2300..2700: its centre is 500 into page 3.
    // Horizontally, the page is centred in the wider viewport: the centre is 50 from its left edge.
    [AvaloniaTheory]
    [InlineData(0, 50, 500)]
    [InlineData(90, 500, 100 - 50)]
    [InlineData(180, 100 - 50, 1000 - 500)]
    [InlineData(270, 1000 - 500, 50)]
    public void ReadingPoint_IsTheViewportCentreOnTheUnrotatedPage(int rotation, double expectedX, double expectedY)
    {
        const double pageHeight = 1000;
        var control = Create(10, pageHeight);
        control.Styles.Add(new Style(x => x.OfType<PageItem>())
        {
            Setters = { new Setter(PageItem.RotationProperty, rotation) }
        });
        Show(control);

        control.GoToPage(3, 300);
        RunLayout();

        Assert.Equal(2 * pageHeight + 300, control.Scroll!.Offset.Y, 1);
        Assert.NotNull(control.ReadingPoint);
        Assert.Equal(3, control.ReadingPoint.Value.PageNumber);
        Assert.Equal(expectedX, control.ReadingPoint.Value.Position.X, 1);
        Assert.Equal(expectedY, control.ReadingPoint.Value.Position.Y, 1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void ToDisplayOffsets_MissingSourceAxis_IsMissingOnTheAxisItLandsOn(int rotation)
    {
        var (xOnly, yFromX) = PageItemsControl.ToDisplayOffsets(rotation, DisplaySize(rotation), U, null, isPdf: true);
        var (xFromY, yOnly) = PageItemsControl.ToDisplayOffsets(rotation, DisplaySize(rotation), null, PdfY, isPdf: true);

        bool swapsAxes = rotation is 90 or 270;
        Assert.Equal(swapsAxes, xOnly is null);
        Assert.Equal(!swapsAxes, yFromX is null);
        Assert.Equal(!swapsAxes, xFromY is null);
        Assert.Equal(swapsAxes, yOnly is null);
    }

    // The headless host lays out 100 x 1000 pages, which stand for the display size here: at 90
    // and 270 they are 1000 x 100 landscape pages turned upright. The pages are narrower than the
    // viewport, so only the vertical offset can be checked.
    [AvaloniaTheory]
    [InlineData(0, 1000 - 300)]   // PDF y = 300 from the bottom
    [InlineData(180, 300)]        // PDF bottom is at the display top
    [InlineData(90, 30)]          // PDF x = 30 from the unrotated left, which is the display top
    [InlineData(270, 1000 - 30)]  // the unrotated left edge is at the display bottom
    public void GoToPage_PdfCoordinates_FollowThePageRotation(int rotation, double expectedYInPage)
    {
        const double pageHeight = 1000;
        var control = Create(10, pageHeight);
        control.Styles.Add(new Style(x => x.OfType<PageItem>())
        {
            Setters = { new Setter(PageItem.RotationProperty, rotation) }
        });
        Show(control);

        control.GoToPage(3, yOffset: 300, xOffset: 30, offsetPdfCoord: true);
        RunLayout();

        // The target is centred in the viewport.
        Assert.Equal(2 * pageHeight + expectedYInPage - control.Scroll!.Viewport.Height / 2, control.Scroll.Offset.Y, 1);
    }

    [AvaloniaTheory]
    [InlineData(90)]
    [InlineData(270)]
    public void GoToPage_PdfYOnly_OnSidewaysPage_LandsAtThePageTop(int rotation)
    {
        // A bookmark only has a PDF y, which a 90 / 270 rotation lays along the horizontal axis.
        // With no vertical target, the page top is aligned with the viewport top, not centred.
        const double pageHeight = 1000;
        var control = Create(10, pageHeight);
        control.Styles.Add(new Style(x => x.OfType<PageItem>())
        {
            Setters = { new Setter(PageItem.RotationProperty, rotation) }
        });
        Show(control);

        control.GoToPage(3, yOffset: 50, offsetPdfCoord: true);
        RunLayout();

        Assert.Equal(2 * pageHeight, control.Scroll!.Offset.Y, 1);
    }
}
