using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Core.ViewModels;
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
    public async Task Toggle_KeepsTheLayerCaches_DeactivationReleasesThem()
    {
        // The layered pages and raw text layers hold every layer's content, so a toggle reuses
        // them: only deactivation may release them.
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = NewLoadedDocument(pdfService, pageService);
        var node = OneLayer(pdfService);
        await document.LayersSource;

        document.SetActive();
        Assert.True(await WaitForPicture(document));

        await document.SetLayerVisibilityAsync(node, true);
        await document.SetLayerVisibilityAsync(node, false);
        Assert.True(await WaitForPicture(document));
        await Settle();
        Assert.Equal(0, pdfService.ClearLayerCachesCount);

        document.SetInactive();
        Assert.True(await WaitUntil(() => pdfService.ClearLayerCachesCount == 1),
            "the layer caches to be released on deactivation");
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

    private static async Task<bool> CompletesWithin(Task task, int milliseconds = 4000)
        => await Task.WhenAny(task, Task.Delay(milliseconds)) == task;

    [AvaloniaFact]
    public async Task SecondToggle_DoesNotWaitForTheFirstTogglesIndexRebuild()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var search = new GatedTextSearchService();
        var document = NewLoadedDocument(pdfService, pageService, textSearchService: search);
        var node = OneLayer(pdfService);
        await document.LayersSource;

        document.TextSearch = "layer";
        Assert.True(await WaitUntil(() => search.Builds == 1), "the index to be built for the search");

        Assert.True(await CompletesWithin(document.SetLayerVisibilityAsync(node, true)),
            "the first toggle to complete while its index rebuild is still running");
        Assert.True(await WaitUntil(() => search.Builds == 2), "the index to be rebuilt after the first toggle");

        Assert.True(await CompletesWithin(document.SetLayerVisibilityAsync(node, false)),
            "the second toggle to complete without the first toggle's rebuild finishing");
        Assert.True(await WaitUntil(() => search.CancelledBuilds == 2),
            "the first toggle's rebuild to be cancelled (as was the original build)");
        Assert.True(await WaitUntil(() => search.Builds == 3), "the index to be rebuilt after the second toggle");
        Assert.False(node.IsOn);

        search.Gate.SetResult();
    }

    private static bool SearchEnded(DocumentViewModel document)
        => document.Exception is not null || document.SearchStatus == "No Result Found";

    [AvaloniaFact]
    public async Task SetInactive_WhileTheIndexIsBuilding_DoesNotBreakTheSearch()
    {
        // Deactivation does not stop the index build, so text layers it requested are still in
        // flight across the deactivation's release of the page content.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pdfService = new RenderingPdfDocumentService { TextLayerGate = gate };
        await using var pageService = new PdfPageService(pdfService);
        using var search = new RecordingTextSearchService(new SearchValuesTextSearchService(pageService));
        var document = NewLoadedDocument(pdfService, pageService, textSearchService: search);

        document.TextSearch = "layer";
        Assert.True(await WaitUntil(() => pdfService.TextLayerRequests == 2),
            "both pages' text layers to be requested for the index");

        document.SetInactive();
        await Settle();

        gate.SetResult();
        Assert.True(await WaitUntil(() => SearchEnded(document)), "the search to end");
        Assert.Null(document.Exception);

        // The next search runs over a complete index: the first build's, or a new one if that one
        // did not complete - never a build that stopped part-way.
        document.TextSearch = "other";
        Assert.True(await WaitUntil(() => search.SuccessfulBuilds == 1), "an index build to complete");
        Assert.True(await WaitUntil(() => SearchEnded(document)), "the next search to end");
        Assert.Null(document.Exception);
    }

    [AvaloniaFact]
    public async Task Search_AfterAFailedIndexBuild_StartsANewBuild()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var search = new FailingOnceTextSearchService();
        var document = NewLoadedDocument(pdfService, pageService, textSearchService: search);

        document.TextSearch = "layer";
        Assert.True(await WaitUntil(() => document.Exception is not null), "the failed build to be reported");
        document.Exception = null;

        document.TextSearch = "other";
        Assert.True(await WaitUntil(() => search.Builds == 2), "a new build, not the failed one reused");
        Assert.True(await WaitUntil(() => SearchEnded(document)), "the search to end");
        Assert.Null(document.Exception);
    }

    [AvaloniaFact]
    public async Task Toggle_CancellingAnIndexBuild_LeavesNoStaleProgress()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var search = new GatedTextSearchService();
        var document = NewLoadedDocument(pdfService, pageService, textSearchService: search);
        var node = OneLayer(pdfService);
        await document.LayersSource;

        Task toggle = document.SetLayerVisibilityAsync(node, true);

        // A build started after the click, before the reload: the reload cancels it, and it reports
        // progress after that. An empty query still starts the build, and nothing re-runs it.
        document.SearchTextCommand.Execute(null);
        Assert.True(search.Reported.Task.Wait(4000), "the build to start");

        await toggle;
        Assert.True(await WaitUntil(() => search.CancelledBuilds == 1), "the build to be cancelled");
        await Settle();

        Assert.Equal(0, document.BuildIndexProgress);
        Assert.False(document.BuildingIndex);

        search.Gate.SetResult();
    }

    [AvaloniaFact]
    public async Task Toggle_ClearsTheSearchResultsImmediately()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = NewLoadedDocument(pdfService, pageService);
        var node = OneLayer(pdfService);
        await document.LayersSource;

        var result = new TextSearchResult { ItemType = SearchResultItemType.Word, PageNumber = 1 };
        document.SearchResults.AddSorted(result);
        document.SelectedTextSearchResult = result;

        Task toggle = document.SetLayerVisibilityAsync(node, true);

        Assert.Empty(document.SearchResults);
        Assert.Null(document.SelectedTextSearchResult);
        await toggle;
    }
}
