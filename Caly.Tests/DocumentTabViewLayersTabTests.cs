using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
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

    [AvaloniaFact]
    public async Task LayersTree_RealizesACheckBoxThatTogglesTheLayer()
    {
        var icons = (Styles)AvaloniaXamlLoader.Load(new Uri("avares://Caly.Core/Assets/Icons.axaml"));
        Application.Current!.Styles.Add(icons);
        var treeTheme = new StyleInclude(new Uri("avares://Caly.Tests"))
        {
            Source = new Uri("avares://Avalonia.Controls.TreeDataGrid/Themes/Fluent.axaml")
        };
        Application.Current.Styles.Add(treeTheme);
        try
        {
            var node = new PdfLayerNode("Layer", 0, null);
            node.ApplyState(true, true);
            var pdfService = new RenderingPdfDocumentService
            {
                Layers = [node],
                LayerStates = [true]
            };
            await using var pageService = new PdfPageService(pdfService);
            var document = NewLoadedDocument(pdfService, pageService);
            var view = new DocumentTabView { DataContext = document };
            var window = new Window { Content = view };
            window.Show();

            // The pane state and size come from the host tab strip, which this view does not have here.
            view.PaneSize = 300;
            view.IsPaneOpen = true;
            await document.LayersSource;
            Dispatcher.UIThread.RunJobs();

            var tabControl = view.FindControl<TabControl>("PART_TabControlNavigation")!;
            tabControl.SelectedIndex = (int)LeftNavBarTabIndex.Layers;
            Dispatcher.UIThread.RunJobs();

            // A TabItem's content is hosted by the TabControl's presenter, not inside the TabItem.
            CheckBox? Find() => tabControl.GetVisualDescendants().OfType<CheckBox>()
                .FirstOrDefault(c => AutomationProperties.GetName(c) == "Layer");

            var found = await WaitUntil(() =>
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                return Find() is not null;
            });
            Assert.True(found);

            var checkBox = Find()!;
            Assert.True(checkBox.IsChecked);

            checkBox.IsChecked = false;
            Assert.True(await WaitUntil(() =>
            {
                Dispatcher.UIThread.RunJobs();
                return pdfService.SetLayerVisibilityCount == 1;
            }));
        }
        finally
        {
            Application.Current.Styles.Remove(treeTheme);
            Application.Current.Styles.Remove(icons);
        }
    }
}
