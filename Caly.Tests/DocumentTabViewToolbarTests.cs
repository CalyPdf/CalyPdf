using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Caly.Core.Controls;
using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Tests.Fakes;

namespace Caly.Tests;

/// <summary>
/// What a screen reader gets from the document toolbar. Its buttons show icons: a button without
/// an automation name falls back to its content's ToString(), so each was announced as
/// "Avalonia.Controls.PathIcon". The help text already comes from the icons' tooltips.
/// </summary>
public class DocumentTabViewToolbarTests : IDisposable
{
    private static readonly string[] ExpectedNames =
    [
        "Toggle Sidebar Menu",
        "Previous Page",
        "Next Page",
        "Single Page View",
        "Rotate Pages Counterclockwise",
        "Rotate Pages Clockwise",
        "Zoom Out",
        "Zoom In",
        "Fit",
        "Print"
    ];

    private readonly Styles _icons;
    private Window? _window;

    public DocumentTabViewToolbarTests()
    {
        // DocumentTabView references icon geometries via StaticResource, which the test app does
        // not load by default.
        _icons = (Styles)AvaloniaXamlLoader.Load(new Uri("avares://Caly.Core/Assets/Icons.axaml"));
        Application.Current!.Styles.Add(_icons);
    }

    public void Dispose()
    {
        _window?.Close();
        Application.Current!.Styles.Remove(_icons);
    }

    /// <summary>
    /// The toolbar buttons of a shown DocumentTabView (shown so its styles apply), in toolbar order.
    /// The tab view has no other buttons outside control templates.
    /// </summary>
    private Button[] ShowToolbarButtons(object? dataContext = null)
    {
        var docTabView = new DocumentTabView { DataContext = dataContext };
        _window = new Window { Width = 1000, Height = 600, Content = docTabView };
        _window.Show();
        Dispatcher.UIThread.RunJobs();

        return docTabView.GetLogicalDescendants().OfType<Button>().ToArray();
    }

    [AvaloniaFact]
    public void ToolbarButtons_HaveAccessibleNames()
    {
        var names = ShowToolbarButtons()
            .Select(button => ControlAutomationPeer.CreatePeerForElement(button).GetName())
            .ToArray();

        Assert.Equal(ExpectedNames, names);
    }

    /// <summary>
    /// The fit menu marks the document's zoom mode as its checked radio item, and nothing in
    /// <see cref="ZoomMode.Manual"/>. Its items only get the document as DataContext once the
    /// menu is open.
    /// </summary>
    [AvaloniaFact]
    public async Task FitMenu_ChecksTheDocumentsZoomMode()
    {
        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = DocumentTestHarness.NewLoadedDocument(pdfService, pageService);

        var dropDown = ShowToolbarButtons(document).OfType<DropDownButton>().Single();
        var fitMenu = Assert.IsType<MenuFlyout>(dropDown.Flyout);
        fitMenu.ShowAt(dropDown);
        Dispatcher.UIThread.RunJobs();

        var radioItems = fitMenu.Items.OfType<MenuItem>()
            .Where(item => item.ToggleType == MenuItemToggleType.Radio)
            .ToArray();
        Assert.Equal(["Fit page", "Fit width", "Fit content", "Fit content width"],
            radioItems.Select(item => item.Header as string));

        bool[] Checks() => radioItems.Select(item => item.IsChecked).ToArray();

        Assert.Equal([false, false, false, false], Checks());

        document.ZoomMode = ZoomMode.FitWidth;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([false, true, false, false], Checks());

        document.ZoomMode = ZoomMode.FitContentWidth;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([false, false, false, true], Checks());

        document.ZoomMode = ZoomMode.Manual;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([false, false, false, false], Checks());

        fitMenu.Hide();
    }

    /// <summary>
    /// The toggle's name stays the same in both modes, as a toggle's name should (its on/off state
    /// tells the mode); the help text, from the icon's tooltip, describes the current mode.
    /// </summary>
    [AvaloniaFact]
    public void SinglePageToggle_HelpTextDescribesTheCurrentMode()
    {
        var toggle = ShowToolbarButtons().OfType<ToggleButton>()
            .Single(button => AutomationProperties.GetName(button) == "Single Page View");
        var peer = ControlAutomationPeer.CreatePeerForElement(toggle);

        Assert.Equal("Continuous Scrolling View", peer.GetHelpText());

        toggle.IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Single Page View", peer.GetHelpText());
    }
}
