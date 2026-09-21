using Avalonia.Headless.XUnit;
using Caly.Core.Services;
using Caly.Core.ViewModels;
using Caly.Tests.Fakes;

namespace Caly.Tests;

/// <summary>
/// <see cref="DocumentViewModel.SelectedPageNumber"/> used to default to <c>1</c> via a field
/// initialiser, which bypassed its own setter's <c>value &gt; PageCount</c> validation -
/// <see cref="DocumentViewModel.PageCount"/> itself defaults to <c>0</c> and only becomes
/// accurate once <see cref="DocumentViewModel.LoadDocument"/> has finished successfully. A tab
/// exists (and is hoverable) as soon as its <see cref="DocumentViewModel"/> is constructed and
/// added to the strip - well before that. A live production log caught
/// <c>System.ArgumentException: "Page number should exist in the document."</c> thrown from
/// <see cref="DocumentViewModel.GetPage"/>, called via <see cref="DocumentViewModel.EnsureTabPreview"/>
/// hovering a tab in exactly this window - the command's task faulted silently (before
/// <c>IsTabPreviewLoading</c> was ever set, and before the try/catch that logs every other
/// failure in that method), so no spinner or image ever showed and nothing but the fault itself
/// was logged. Fixed by leaving <see cref="DocumentViewModel.SelectedPageNumber"/> <c>null</c>
/// until <see cref="DocumentViewModel.PageCount"/> is actually known.
/// </summary>
public class TabPreviewBeforeLoadTests
{
    [AvaloniaFact]
    public async Task EnsureTabPreview_HoveredBeforeTheDocumentFinishesLoading_DoesNothingGracefully()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);

        // A freshly constructed, not-yet-loaded document: PageCount is still 0 (its default),
        // SelectedPageNumber is still null (nothing valid to select yet), and IsActive is false
        // (RenderingPdfDocumentService.IsActive defaults to false) - the exact state of a tab
        // that exists but has not opened yet.
        var document = new DocumentViewModel(pdfService, pageService, new NoopTextSearchService());

        Assert.False(document.IsActive);
        Assert.Null(document.SelectedPageNumber);
        Assert.Equal(0, document.PageCount);
        Assert.False(document.IsTabPreviewLoading);

        document.EnsureTabPreviewCommand.Execute(null);

        Assert.True(await DocumentTestHarness.WaitUntil(() =>
            document.EnsureTabPreviewCommand.ExecutionTask is { IsCompleted: true }),
            "the hover command to finish (successfully or not) rather than hang");

        Assert.False(document.EnsureTabPreviewCommand.ExecutionTask!.IsFaulted,
            "hovering a tab before its document finishes loading must not fault the command");
        Assert.False(document.IsTabPreviewLoading);
        Assert.Null(document.TabPreview);
    }
}
