using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Caly.Core.Controls;
using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Core.Utilities;
using Caly.Core.ViewModels;
using Caly.Tests.Fakes;

namespace Caly.Tests;

/// <summary>
/// The zoom shortcuts are Alt+0 to Alt+4 (Cmd on macOS): fit page, actual size, fit width, fit
/// content, fit content width. Ctrl+1 to Ctrl+7 are global hotkeys of PowerToys ZoomIt, so with
/// it running they never reached Caly; Alt+digits are not taken.
/// </summary>
public class DocumentControlZoomShortcutTests : IAsyncDisposable
{
    private readonly Styles _icons;
    private readonly ResourceDictionary _documentControlTheme;
    private readonly RenderingPdfDocumentService _pdfService = new();
    private readonly PdfPageService _pageService;
    private Window? _window;

    public DocumentControlZoomShortcutTests()
    {
        // The key bindings live in DocumentControl's theme, which references icon geometries.
        _icons = (Styles)AvaloniaXamlLoader.Load(new Uri("avares://Caly.Core/Assets/Icons.axaml"));
        _documentControlTheme = (ResourceDictionary)AvaloniaXamlLoader.Load(
            new Uri("avares://Caly.Core/Controls/DocumentControl.axaml"));
        Application.Current!.Styles.Add(_icons);
        Application.Current.Resources.MergedDictionaries.Add(_documentControlTheme);

        _pageService = new PdfPageService(_pdfService);
    }

    public async ValueTask DisposeAsync()
    {
        _window?.Close();
        Application.Current!.Resources.MergedDictionaries.Remove(_documentControlTheme);
        Application.Current.Styles.Remove(_icons);
        await _pageService.DisposeAsync();
    }

    /// <summary>
    /// Shows a document and puts the keyboard focus in its pages, where the reader's key
    /// presses land.
    /// </summary>
    private (Window Window, DocumentViewModel Document) ShowDocument(Action<DocumentViewModel>? setUp = null)
    {
        var document = DocumentTestHarness.NewLoadedDocument(_pdfService, _pageService);
        setUp?.Invoke(document);

        var control = new DocumentControl { DataContext = document };
        _window = new Window { Width = 800, Height = 600, Content = control };
        _window.Show();
        Dispatcher.UIThread.RunJobs();

        // PageItemsControl's own theme (not loaded here) is what makes it focusable.
        var pages = control.GetVisualDescendants().OfType<PageItemsControl>().Single();
        pages.Focusable = true;
        pages.Focus();
        Dispatcher.UIThread.RunJobs();

        return (_window, document);
    }

    [AvaloniaTheory]
    [InlineData(PhysicalKey.Digit0, ZoomMode.FitPage)]
    [InlineData(PhysicalKey.Digit2, ZoomMode.FitWidth)]
    [InlineData(PhysicalKey.Digit3, ZoomMode.FitContent)]
    [InlineData(PhysicalKey.Digit4, ZoomMode.FitContentWidth)]
    public void AltDigit_PicksTheFitMode(PhysicalKey key, ZoomMode expected)
    {
        var (window, document) = ShowDocument();

        window.KeyPressQwerty(key, RawInputModifiers.Alt);

        Assert.Equal(expected, document.ZoomMode);
    }

    [AvaloniaFact]
    public void Alt1_ZoomsToActualSize()
    {
        var (window, document) = ShowDocument(d =>
        {
            d.ZoomLevel = 3;
            d.ZoomMode = ZoomMode.FitWidth;
        });

        window.KeyPressQwerty(PhysicalKey.Digit1, RawInputModifiers.Alt);

        Assert.Equal(1, document.ZoomLevel);
        Assert.Equal(ZoomMode.Manual, document.ZoomMode);
    }

    /// <summary>
    /// Where the command key is Cmd (macOS) the zoom shortcuts keep it, since Option+digits type
    /// characters there. Elsewhere they use Alt. An Avalonia test: the hotkey class reads the
    /// platform settings when first used.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(KeyModifiers.Control, KeyModifiers.Alt)]
    [InlineData(KeyModifiers.Meta, KeyModifiers.Meta)]
    public void ZoomModifiers_AreAltUnlessTheCommandKeyIsCmd(KeyModifiers commandModifiers, KeyModifiers expected)
    {
        Assert.Equal(expected, CalyHotkeyConfiguration.GetZoomModifiers(commandModifiers));
    }
}
