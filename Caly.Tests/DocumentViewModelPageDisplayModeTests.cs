using System.ComponentModel;
using Avalonia.Headless.XUnit;
using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Tests.Fakes;
using static Caly.Tests.Fakes.DocumentTestHarness;

namespace Caly.Tests;

public class DocumentViewModelPageDisplayModeTests
{
    [AvaloniaFact]
    public async Task IsSinglePageMode_TogglesPageDisplayMode()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = NewLoadedDocument(pdfService, pageService);
        var changed = new List<string?>();
        ((INotifyPropertyChanged)document).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.Equal(PageDisplayMode.Continuous, document.PageDisplayMode);

        document.IsSinglePageMode = true;

        Assert.Equal(PageDisplayMode.SinglePage, document.PageDisplayMode);
        Assert.Contains(nameof(document.IsSinglePageMode), changed);

        document.IsSinglePageMode = false;
        Assert.Equal(PageDisplayMode.Continuous, document.PageDisplayMode);
    }

    [AvaloniaFact]
    public async Task SinglePageDocument_Refresh_PrefetchesTheNextPage()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = NewLoadedDocument(pdfService, pageService, pageCount: 2);
        document.IsSinglePageMode = true;
        document.VisiblePages = new Range(1, 2);
        document.RealisedPages = new Range(1, 2);

        await document.RefreshPagesCommand.ExecuteAsync(null);

        Assert.True(await WaitUntil(() => pdfService.RenderCountFor(2) == 1));
        Assert.Null(document.Pages[1].PdfPicture);
    }

    [AvaloniaFact]
    public async Task ThumbnailRefresh_InSinglePage_KeepsTheWiderCacheWindow()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = NewLoadedDocument(pdfService, pageService, pageCount: 8);
        document.IsSinglePageMode = true;

        document.VisiblePages = new Range(1, 2);
        document.RealisedPages = new Range(1, 2);
        await document.RefreshPagesCommand.ExecuteAsync(null);
        Assert.True(await WaitUntil(() => document.Pages[0].PdfPicture is not null));

        document.VisiblePages = new Range(4, 5);
        document.RealisedPages = new Range(4, 5);
        await document.RefreshPagesCommand.ExecuteAsync(null);
        Assert.True(await WaitUntil(() => document.Pages[3].PdfPicture is not null));

        // Only page 8's thumbnail: it renders page 8, never page 1.
        document.VisibleThumbnails = new Range(8, 9);
        document.RealisedThumbnails = new Range(8, 9);
        await document.RefreshThumbnailsCommand.ExecuteAsync(null);
        Assert.True(await WaitUntil(() => pdfService.RenderCountFor(8) == 1));

        document.VisiblePages = new Range(1, 2);
        document.RealisedPages = new Range(1, 2);
        await document.RefreshPagesCommand.ExecuteAsync(null);
        Assert.True(await WaitUntil(() => document.Pages[0].PdfPicture is not null));

        Assert.Equal(1, pdfService.RenderCountFor(1));
    }
}
