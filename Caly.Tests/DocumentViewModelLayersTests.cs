using Avalonia.Headless.XUnit;
using Avalonia.Threading;
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

    /// <summary>
    /// Whether page 1 shows a picture rendered under exactly <paramref name="states"/>.
    /// </summary>
    private static bool ShowsPictureRenderedUnder(RenderingPdfDocumentService pdfService,
        Caly.Core.ViewModels.DocumentViewModel document, params bool[] states)
        => pdfService.RenderedUnder(document.Pages[0].PdfPicture) is { } renderedUnder
           && renderedUnder.SequenceEqual(states);

    /// <summary>
    /// Pumps the dispatcher for a while, so that a late (stale) result has the chance to land.
    /// </summary>
    private static async Task Settle()
    {
        for (int i = 0; i < 30; ++i)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Dispatcher.UIThread.RunJobs();
    }

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

        // The in-flight render (old state) must not end up on the page, now or later.
        Assert.True(await WaitUntil(() => ShowsPictureRenderedUnder(pdfService, document, true)),
            "the page to show a picture rendered under the new layer state");
        await Settle();
        Assert.True(ShowsPictureRenderedUnder(pdfService, document, true));
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

        // Not a picture of the intermediate (on) state.
        Assert.True(await WaitUntil(() => ShowsPictureRenderedUnder(pdfService, document, false)),
            "the page to show a picture rendered under the final layer state");
        await Settle();
        Assert.True(ShowsPictureRenderedUnder(pdfService, document, false));
    }

    [Fact]
    public async Task InvalidateContent_TheCachedPictureIsNeverServedAgain()
    {
        // A request dequeued between InvalidateContent and CancelAndClear must not get the
        // picture cached under the old state: it would assign it and nothing would ask again.
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        pdfService.Publish(1);
        pageService.Initialise();
        OneLayer(pdfService);

        using (var before = await pageService.GetPicture(1, CancellationToken.None))
        {
            Assert.NotNull(before);
        }

        lock (pdfService.LayerStates)
        {
            pdfService.LayerStates[0] = true;
        }

        pageService.InvalidateContent();

        using var after = await pageService.GetPicture(1, CancellationToken.None);
        Assert.Equal(2, pdfService.RenderCountFor(1));
        Assert.Equal([true], pdfService.RenderedUnder(after));
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
