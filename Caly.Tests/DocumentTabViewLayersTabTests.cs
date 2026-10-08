using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Caly.Core.Controls;
using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Core.Utilities;
using Caly.Tests.Fakes;
using static Caly.Tests.Fakes.DocumentTestHarness;

namespace Caly.Tests;

public class DocumentTabViewLayersTabTests
{
    private static async Task<TabItem> LayersTab(RenderingPdfDocumentService pdfService, PdfPageService pageService)
    {
        var document = NewLoadedDocument(pdfService, pageService);
        var view = new DocumentTabView { DataContext = document };
        var window = new Window { Content = view };
        window.Show();

        var tabControl = view.FindControl<TabControl>("PART_TabControlNavigation")!;
        var tab = (TabItem)tabControl.Items[(int)LeftNavBarTabIndex.Layers]!;

        await document.LayersSource;
        Dispatcher.UIThread.RunJobs();
        return tab;
    }

    [AvaloniaFact]
    public async Task LayersTab_IsHiddenWithoutLayersAndShownWithThem()
    {
        var icons = (Styles)AvaloniaXamlLoader.Load(new Uri("avares://Caly.Core/Assets/Icons.axaml"));
        Application.Current!.Styles.Add(icons);
        try
        {
            var noLayers = new RenderingPdfDocumentService();
            await using (var pageService = new PdfPageService(noLayers))
            {
                Assert.False((await LayersTab(noLayers, pageService)).IsVisible);
            }

            var withLayers = new RenderingPdfDocumentService
            {
                Layers = [new PdfLayerNode("Layer", 0, null)],
                LayerStates = [true]
            };
            await using (var pageService = new PdfPageService(withLayers))
            {
                Assert.True((await LayersTab(withLayers, pageService)).IsVisible);
            }
        }
        finally
        {
            Application.Current.Styles.Remove(icons);
        }
    }
}
