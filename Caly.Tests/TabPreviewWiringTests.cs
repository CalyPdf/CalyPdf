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
/// The hover preview reaches the tab through a Setter whose value is a template, so every
/// DragTabItem gets its own instance and inherits the document as its DataContext. A single
/// shared instance would show one document's preview on every tab.
/// </summary>
public class TabPreviewWiringTests
{
    [AvaloniaFact]
    public async Task EachTab_GetsItsOwnTabPreviewControl()
    {
        var mainViewModel = new MainViewModel();
        mainViewModel.Dispose();

        var firstService = new RenderingPdfDocumentService();
        await using var firstPageService = new PdfPageService(firstService);
        var first = DocumentTestHarness.NewLoadedDocument(firstService, firstPageService);

        var secondService = new RenderingPdfDocumentService();
        await using var secondPageService = new PdfPageService(secondService);
        var second = DocumentTestHarness.NewLoadedDocument(secondService, secondPageService);

        // The tab header, and the selected tab's plain tip, both come from this.
        first.FileName = "first.pdf";
        second.FileName = "second.pdf";

        mainViewModel.PdfDocuments.Add(first);
        mainViewModel.PdfDocuments.Add(second);

        var control = new DocumentsTabsControl { DataContext = mainViewModel };
        var window = new Window { Width = 600, Height = 400, Content = control };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var firstTab = Assert.IsType<DragTabItem>(control.TabsControl.ContainerFromIndex(0));
            var secondTab = Assert.IsType<DragTabItem>(control.TabsControl.ContainerFromIndex(1));

            // Index 0 is selected, so it keeps the plain file-name tip.
            Assert.Equal("first.pdf", ToolTip.GetTip(firstTab));

            // The unselected one carries the preview, and it is its own instance.
            var preview = Assert.IsType<TabPreviewControl>(ToolTip.GetTip(secondTab));
            Assert.NotSame(ToolTip.GetTip(firstTab), preview);

            // The preview inherits this when the popup parents it; nothing else supplies it.
            Assert.Same(second, secondTab.DataContext);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// The control only enters the visual tree when the tooltip popup opens, so its own load is
    /// the signal that the user is looking at the tab.
    /// </summary>
    [AvaloniaFact]
    public async Task ShowingThePreviewControl_AsksTheDocumentForAPreview()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = DocumentTestHarness.NewLoadedDocument(pdfService, pageService);

        document.SetInactive();
        Assert.True(await DocumentTestHarness.WaitUntil(() => !document.IsActive),
            "the document to settle as inactive");
        Assert.Null(document.TabPreview);

        var preview = new TabPreviewControl { DataContext = document };
        var window = new Window { Width = 400, Height = 400, Content = preview };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            Assert.True(await DocumentTestHarness.WaitUntil(() => document.TabPreview is not null),
                "showing the preview control to request a preview");
        }
        finally
        {
            window.Close();
        }
    }
}
