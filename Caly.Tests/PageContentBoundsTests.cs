using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Caly.Core.Controls;
using Caly.Core.Models;
using Caly.Core.Services.Rendering;
using Caly.Core.Utilities;
using Caly.Core.ViewModels;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;

namespace Caly.Tests;

/// <summary>
/// A page's content box is its picture's cull rect: pages are recorded with an R-tree, so Skia trims
/// the cull rect to what was drawn. It feeds the Fit content and Fit content width zoom modes.
/// </summary>
public class PageContentBoundsTests
{
    /// <summary>
    /// A 100×200 page with one rect drawn at (10, 20)-(40, 60), recorded like the renderer records
    /// pages (with an R-tree).
    /// </summary>
    private static SKPicture RecordPage(bool drawSomething = true)
    {
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(SKRect.Create(100, 200), true);
        if (drawSomething)
        {
            using var paint = new SKPaint { Color = SKColors.Black };
            canvas.DrawRect(new SKRect(10, 20, 40, 60), paint);
        }

        return recorder.EndRecording();
    }

    [Fact]
    public void ComputeContentBounds_ScalesTheCullRect()
    {
        using var picture = RecordPage();

        Assert.Equal(new Rect(15, 30, 45, 60), PageViewModel.ComputeContentBounds(picture.CullRect, 1.5));
    }

    [Fact]
    public void ComputeContentBounds_NothingDrawn_IsNull()
    {
        using var picture = RecordPage(drawSomething: false);

        Assert.Null(PageViewModel.ComputeContentBounds(picture.CullRect, 1.5));
    }

    [Fact]
    public void PictureArrival_SetsContentBounds_AndEvictionKeepsThem()
    {
        var page = new PageViewModel(
            pageNumber: 1,
            textSelection: new TextSelection(1),
            tileRenderService: new TileRenderService(),
            ppiScale: 1.5,
            copyTextCommand: new RelayCommand(() => { }));
        using var picture = RefCountable.Create(RecordPage());

        page.PdfPicture = picture;
        Assert.Equal(new Rect(15, 30, 45, 60), page.ContentBounds);

        // The picture is evicted when the page leaves the screen; its content box stays known.
        page.PdfPicture = null;
        Assert.Equal(new Rect(15, 30, 45, 60), page.ContentBounds);
    }

    /// <summary>
    /// The rect (10, 20, 30, 40) of an unrotated 100×200 page, after the page is turned clockwise.
    /// </summary>
    [Theory]
    [InlineData(0, 100, 200, 10, 20, 30, 40)]
    [InlineData(90, 200, 100, 140, 10, 40, 30)]
    [InlineData(180, 100, 200, 60, 140, 30, 40)]
    [InlineData(270, 200, 100, 20, 60, 40, 30)]
    public void ToDisplayRect_FollowsThePageRotation(int rotation, double displayWidth, double displayHeight,
        double x, double y, double width, double height)
    {
        var rect = PageItemsControl.ToDisplayRect(rotation, new Size(displayWidth, displayHeight), new Rect(10, 20, 30, 40));

        Assert.Equal(new Rect(x, y, width, height), rect);
    }

    [AvaloniaFact]
    public void ContentBoundsChange_BubblesContentBoundsChanged()
    {
        var page = new PageItem();
        var panel = new StackPanel { Children = { page } };
        int raised = 0;
        panel.AddHandler(PageItem.ContentBoundsChangedEvent, (_, _) => raised++);

        page.ContentBounds = new Rect(1, 2, 3, 4);

        Assert.Equal(1, raised);
    }
}
