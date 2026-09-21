using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Caly.Core.Controls;
using Caly.Core.Services;
using Caly.Core.ViewModels;
using Caly.Tests.Fakes;
using Tabalonia.Controls;

namespace Caly.Tests;

/// <summary>
/// A live crash caught <c>System.InvalidOperationException: "...already has a visual parent
/// ContentPresenter (Host = ToolTip) while trying to add it as a child of ContentPresenter
/// (Host = ToolTip)."</c> from <c>TabPreviewControl</c>, while rapidly hovering across tabs.
/// <para>
/// The base <c>tabalonia|DragTabItem</c> style's <c>ToolTip.Tip</c> Setter used to be a
/// <c>&lt;Template&gt;</c> value, which Avalonia's styling engine materialises once and caches
/// for the tab's lifetime - so the SAME <c>TabPreviewControl</c> (and the <c>ToolTip</c> wrapper
/// caching it) was reused across every hover of that tab. A fast pointer sweep re-hovering a
/// tab within <c>ToolTip.BetweenShowDelay</c> closes and immediately reopens that same cached
/// instance; a control that has only just begun detaching from its closing popup can still be
/// attached when that reopen tries to reparent it.
/// </para>
/// <para>
/// Not reproducible headlessly: the crash depends on real Win32 popup window creation/teardown
/// timing that Avalonia's headless backend does not have (it processes popup show/hide
/// synchronously). <see cref="RapidlyReopeningTheSameTabsTooltip_DoesNotThrow"/> exercises the
/// suspected reentrancy directly and passes both before and after the fix - it is defensive
/// coverage, not proof. <see cref="ReopeningTheSameTabsTooltip_NeverReusesTheSamePreviewControl"/>
/// is the actual regression test: it pins the fix's real mechanism (a fresh, never-parented
/// control every open, via <c>DocumentsTabsControl</c>'s <c>ToolTip.ToolTipOpeningEvent</c>
/// handler), which fails without it regardless of timing.
/// </para>
/// </summary>
public class TabPreviewToolTipReentrancyTests
{
    private static async Task<(DocumentsTabsControl control, Window window, DragTabItem tab, PdfPageService pageService, PdfPageService otherPageService)> BuildTwoTabStrip()
    {
        var mainViewModel = new MainViewModel();
        mainViewModel.Dispose();

        var pdfService = new RenderingPdfDocumentService();
        var pageService = new PdfPageService(pdfService);
        var document = DocumentTestHarness.NewLoadedDocument(pdfService, pageService);
        document.FileName = "first.pdf";

        // A second tab so this one is not :selected - the :selected style overrides ToolTip.Tip
        // to a plain string, and only an inactive tab's real TabPreviewControl is exercised.
        var otherService = new RenderingPdfDocumentService();
        var otherPageService = new PdfPageService(otherService);
        var other = DocumentTestHarness.NewLoadedDocument(otherService, otherPageService);
        other.FileName = "second.pdf";

        mainViewModel.PdfDocuments.Add(other);
        mainViewModel.PdfDocuments.Add(document);
        mainViewModel.SelectedDocumentIndex = 0; // "other" selected, "document" inactive

        var control = new DocumentsTabsControl { DataContext = mainViewModel };
        var window = new Window { Width = 600, Height = 400, Content = control };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var tab = Assert.IsType<DragTabItem>(control.TabsControl.ContainerFromIndex(1));
        Assert.Same(document, tab.DataContext);

        await Task.CompletedTask;
        return (control, window, tab, pageService, otherPageService);
    }

    [AvaloniaFact]
    public async Task RapidlyReopeningTheSameTabsTooltip_DoesNotThrow()
    {
        var (_, window, tab, pageService, otherPageService) = await BuildTwoTabStrip();

        try
        {
            // Fast hover sweep: open, close, and reopen the same tab's tooltip without pumping
            // the dispatcher in between - the exact reentrancy a rapid pointer sweep across tabs
            // produces (ToolTipService's BetweenShowDelay lets a just-closed tooltip reopen
            // immediately, synchronously, rather than waiting out the full ShowDelay again).
            for (int i = 0; i < 5; i++)
            {
                ToolTip.SetIsOpen(tab, true);
                ToolTip.SetIsOpen(tab, false);
            }

            ToolTip.SetIsOpen(tab, true);
            Dispatcher.UIThread.RunJobs();

            Assert.True(ToolTip.GetIsOpen(tab));
        }
        finally
        {
            window.Close();
            await pageService.DisposeAsync();
            await otherPageService.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task ReopeningTheSameTabsTooltip_NeverReusesTheSamePreviewControl()
    {
        var (_, window, tab, pageService, otherPageService) = await BuildTwoTabStrip();

        try
        {
            ToolTip.SetIsOpen(tab, true);
            var first = Assert.IsType<TabPreviewControl>(ToolTip.GetTip(tab));

            ToolTip.SetIsOpen(tab, false);
            ToolTip.SetIsOpen(tab, true);
            var second = Assert.IsType<TabPreviewControl>(ToolTip.GetTip(tab));

            // The actual fix: a control that was never reused can never collide with itself on
            // reattach, regardless of exactly when Avalonia finishes detaching the previous one.
            Assert.NotSame(first, second);
        }
        finally
        {
            window.Close();
            await pageService.DisposeAsync();
            await otherPageService.DisposeAsync();
        }
    }
}
