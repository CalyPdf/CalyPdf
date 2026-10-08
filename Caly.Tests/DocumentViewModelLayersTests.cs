using Avalonia.Headless.XUnit;
using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Tests.Fakes;
using static Caly.Tests.Fakes.DocumentTestHarness;

namespace Caly.Tests;

public class DocumentViewModelLayersTests
{
    private static PdfLayerNode OneLayer(RenderingPdfDocumentService pdfService, bool isOn = false)
    {
        var node = new PdfLayerNode("Layer", 0, null);
        pdfService.Layers = [node];
        pdfService.LayerStates = [isOn];
        return node;
    }

    private static Task<bool> WaitForPicture(Caly.Core.ViewModels.DocumentViewModel document)
        => WaitUntil(() => document.Pages[0].PdfPicture is not null);

    [AvaloniaFact]
    public async Task Toggle_ReRendersTheVisiblePageAndUpdatesTheNode()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = NewLoadedDocument(pdfService, pageService);
        var node = OneLayer(pdfService);
        await document.LayersSource;

        document.SetActive();
        Assert.True(await WaitForPicture(document));
        int rendersBefore = pdfService.RenderCountFor(1);

        await document.SetLayerVisibilityAsync(node, true);

        Assert.True(node.IsOn);
        Assert.True(pdfService.LayerStates[0]);
        Assert.True(await WaitUntil(() => pdfService.RenderCountFor(1) > rendersBefore && document.Pages[0].PdfPicture is not null),
            "the visible page to render again with the new layer state");
    }

    [AvaloniaFact]
    public async Task Toggle_WhileARenderIsInFlight_EndsWithANewRender()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = NewLoadedDocument(pdfService, pageService);
        var node = OneLayer(pdfService);
        await document.LayersSource;

        var gate = new TaskCompletionSource();
        pdfService.RenderGate = gate;
        document.SetActive();
        Assert.True(await WaitUntil(() => pdfService.RenderCountFor(1) >= 1), "a render to start");

        Task toggle = document.SetLayerVisibilityAsync(node, true);
        gate.SetResult();
        await toggle;

        Assert.True(await WaitUntil(() => pdfService.RenderCountFor(1) >= 2 && document.Pages[0].PdfPicture is not null),
            "a render started after the toggle");
    }

    [AvaloniaFact]
    public async Task RapidToggles_EndOnTheLastRequestedState()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = NewLoadedDocument(pdfService, pageService);
        var node = OneLayer(pdfService);
        await document.LayersSource;

        document.SetActive();
        Task first = document.SetLayerVisibilityAsync(node, true);
        Task second = document.SetLayerVisibilityAsync(node, false);
        await Task.WhenAll(first, second);

        Assert.Equal(2, pdfService.SetLayerVisibilityCount);
        Assert.False(pdfService.LayerStates[0]);
        Assert.False(node.IsOn);
        Assert.True(await WaitForPicture(document));
    }

    [AvaloniaFact]
    public async Task Toggle_WhileInactive_RendersOnlyOnceActive()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = NewLoadedDocument(pdfService, pageService);
        var node = OneLayer(pdfService);
        await document.LayersSource;

        await document.SetLayerVisibilityAsync(node, true);
        Assert.False(await WaitUntil(() => pdfService.RenderCount > 0), "no render for an inactive document");

        document.SetActive();
        Assert.True(await WaitForPicture(document));
    }

    [AvaloniaFact]
    public async Task Toggle_WithAnActiveSearch_RebuildsTheIndex()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var search = new CountingTextSearchService();
        var document = NewLoadedDocument(pdfService, pageService, textSearchService: search);
        var node = OneLayer(pdfService);
        await document.LayersSource;

        document.TextSearch = "layer";
        Assert.True(await WaitUntil(() => search.Builds == 1), "the index to be built for the search");

        await document.SetLayerVisibilityAsync(node, true);

        Assert.True(await WaitUntil(() => search.Builds == 2), "the index to be rebuilt after the toggle");
    }
}
