using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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
    private static async Task<(Window Window, TabItem Tab)> LayersTab(RenderingPdfDocumentService pdfService,
        PdfPageService pageService)
    {
        var document = NewLoadedDocument(pdfService, pageService);
        var view = new DocumentTabView { DataContext = document };
        var window = new Window { Content = view };
        window.Show();

        var tabControl = view.FindControl<TabControl>("PART_TabControlNavigation")!;
        var tab = (TabItem)tabControl.Items[(int)LeftNavBarTabIndex.Layers]!;

        await document.LayersSource;
        Dispatcher.UIThread.RunJobs();
        return (window, tab);
    }

    [AvaloniaFact]
    public async Task LayersTab_IsHiddenWithoutLayersAndShownWithThem()
    {
        var icons = (Styles)AvaloniaXamlLoader.Load(new Uri("avares://Caly.Core/Assets/Icons.axaml"));
        Application.Current!.Styles.Add(icons);
        try
        {
            var noLayers = new RenderingPdfDocumentService();
            var pageService = new PdfPageService(noLayers);
            Window? window = null;
            try
            {
                (window, var tab) = await LayersTab(noLayers, pageService);
                Assert.False(tab.IsVisible);
            }
            finally
            {
                window?.Close();
                await pageService.DisposeAsync();
            }

            var withLayers = new RenderingPdfDocumentService
            {
                Layers = [new PdfLayerNode("Layer", 0, null)],
                LayerStates = [true]
            };
            pageService = new PdfPageService(withLayers);
            window = null;
            try
            {
                (window, var tab) = await LayersTab(withLayers, pageService);
                Assert.True(tab.IsVisible);
            }
            finally
            {
                window?.Close();
                await pageService.DisposeAsync();
            }
        }
        finally
        {
            Application.Current.Styles.Remove(icons);
        }
    }

    /// <summary>
    /// Shows a document with one layer (on) with its Layers tab selected, runs <paramref name="test"/>
    /// on the realized layer check box, then closes the window before disposing the page service.
    /// </summary>
    private static async Task WithRealizedLayerCheckBox(
        Func<Window, CheckBox, RenderingPdfDocumentService, Task> test)
    {
        var icons = (Styles)AvaloniaXamlLoader.Load(new Uri("avares://Caly.Core/Assets/Icons.axaml"));
        Application.Current!.Styles.Add(icons);
        var treeTheme = new StyleInclude(new Uri("avares://Caly.Tests"))
        {
            Source = new Uri("avares://Avalonia.Controls.TreeDataGrid/Themes/Fluent.axaml")
        };
        Application.Current.Styles.Add(treeTheme);

        var node = new PdfLayerNode("Layer", 0, null);
        node.ApplyState(true, true);
        var pdfService = new RenderingPdfDocumentService
        {
            Layers = [node],
            LayerStates = [true]
        };
        var pageService = new PdfPageService(pdfService);
        Window? window = null;
        try
        {
            var document = NewLoadedDocument(pdfService, pageService);
            var view = new DocumentTabView { DataContext = document };
            window = new Window { Content = view };
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

            var shown = window;
            var found = await WaitUntil(() =>
            {
                Dispatcher.UIThread.RunJobs();
                shown.UpdateLayout();
                return Find() is not null;
            });
            Assert.True(found);

            await test(window, Find()!, pdfService);
        }
        finally
        {
            window?.Close();
            await pageService.DisposeAsync();
            Application.Current.Styles.Remove(treeTheme);
            Application.Current.Styles.Remove(icons);
        }
    }

    [AvaloniaFact]
    public Task LayersTree_RealizesACheckBoxThatTogglesTheLayer() =>
        WithRealizedLayerCheckBox(async (_, checkBox, pdfService) =>
        {
            Assert.True(checkBox.IsChecked);

            checkBox.IsChecked = false;
            Assert.True(await WaitUntil(() =>
            {
                Dispatcher.UIThread.RunJobs();
                return pdfService.SetLayerVisibilityCount == 1;
            }));
        });

    [AvaloniaFact]
    public Task LayersTree_SpaceTogglesTheFocusedLayer() =>
        WithRealizedLayerCheckBox(async (window, checkBox, pdfService) =>
        {
            Assert.True(checkBox.Focus(NavigationMethod.Tab));
            Dispatcher.UIThread.RunJobs();

            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);

            Assert.True(await WaitUntil(() =>
            {
                Dispatcher.UIThread.RunJobs();
                return pdfService.SetLayerVisibilityCount == 1;
            }), "Space to toggle the focused layer");
            Assert.False(checkBox.IsChecked);
        });

    [AvaloniaFact]
    public Task LayersTree_SpaceTogglesTheLayerOfTheFocusedRow() =>
        WithRealizedLayerCheckBox(async (window, checkBox, pdfService) =>
        {
            // Keyboard navigation in a TreeDataGrid moves the focus between cells, not onto the check box.
            var cell = checkBox.GetVisualAncestors().OfType<Avalonia.Controls.Primitives.TreeDataGridExpanderCell>()
                .First();
            Assert.True(cell.Focus(NavigationMethod.Directional));
            Dispatcher.UIThread.RunJobs();

            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);

            Assert.True(await WaitUntil(() =>
            {
                Dispatcher.UIThread.RunJobs();
                return pdfService.SetLayerVisibilityCount == 1;
            }), "Space to toggle the layer of the focused row");
            Assert.False(checkBox.IsChecked);
        });
}
