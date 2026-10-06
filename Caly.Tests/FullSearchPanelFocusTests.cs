using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Caly.Core.Controls;
using Caly.Core.Services;
using Caly.Core.Utilities;
using Caly.Tests.Fakes;
using CommunityToolkit.Mvvm.Input;
using static Caly.Tests.Fakes.DocumentTestHarness;

namespace Caly.Tests;

/// <summary>
/// The search gesture (Ctrl+F) must put the caret in the search box every time, not only the
/// first time the search tab is shown: once the tab is already showing, nothing reloads.
/// </summary>
public class FullSearchPanelFocusTests
{
    [AvaloniaFact]
    public async Task SearchGesture_RefocusesSearchBox_WhenPanelAlreadyShown()
    {
        // FullSearchPanelControl's ControlTheme is not loaded by the test app.
        var theme = (ResourceDictionary)AvaloniaXamlLoader.Load(
            new Uri("avares://Caly.Core/Controls/FullSearchPanelControl.axaml"));
        Application.Current!.Resources.MergedDictionaries.Add(theme);

        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = NewLoadedDocument(pdfService, pageService);

        var gesture = CalyHotkeyConfiguration.DocumentSearchGesture;
        var panel = new FullSearchPanelControl { DataContext = document };
        var elsewhere = new Button();

        // Stands in for MainView, whose KeyBinding runs the search command and marks the
        // key event handled before it reaches the window.
        var host = new StackPanel { Children = { panel, elsewhere } };
        host.KeyBindings.Add(new KeyBinding { Gesture = gesture, Command = new RelayCommand(() => { }) });

        var window = new Window { Width = 400, Height = 300, Content = host };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var textBox = panel.FindDescendantOfType<TextBox>(false, t => t.Name == "PART_TextBoxSearch");
            Assert.NotNull(textBox);

            // First show: focused on load.
            Assert.True(textBox.IsFocused);

            // The user searched, then clicked into the document.
            document.TextSearch = "foo";
            elsewhere.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.False(textBox.IsFocused);

            // The search gesture again, with the panel still showing.
            window.KeyPressQwerty(PhysicalKey.F, (RawInputModifiers)gesture.KeyModifiers);
            Dispatcher.UIThread.RunJobs();

            Assert.True(textBox.IsFocused);
            Assert.Equal("foo", textBox.SelectedText);
        }
        finally
        {
            window.Close();
            Application.Current.Resources.MergedDictionaries.Remove(theme);
        }
    }

    [AvaloniaFact]
    public async Task ReshownPanel_IsFocused_AndSearchBoxKeysStillWork()
    {
        var theme = (ResourceDictionary)AvaloniaXamlLoader.Load(
            new Uri("avares://Caly.Core/Controls/FullSearchPanelControl.axaml"));
        Application.Current!.Resources.MergedDictionaries.Add(theme);

        var pdfService = new RenderingPdfDocumentService();
        await using var pageService = new PdfPageService(pdfService);
        var document = NewLoadedDocument(pdfService, pageService);

        var panel = new FullSearchPanelControl { DataContext = document };
        var elsewhere = new Button();
        var host = new StackPanel { Children = { panel, elsewhere } };
        var window = new Window { Width = 400, Height = 300, Content = host };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var textBox = panel.FindDescendantOfType<TextBox>(false, t => t.Name == "PART_TextBoxSearch");
            Assert.NotNull(textBox);

            // Hidden (e.g. another side tab selected), then shown again.
            elsewhere.Focus();
            host.Children.Remove(panel);
            Dispatcher.UIThread.RunJobs();
            host.Children.Insert(0, panel);
            Dispatcher.UIThread.RunJobs();

            Assert.True(textBox.IsFocused);

            // Escape clears the query.
            document.TextSearch = "foo";
            Dispatcher.UIThread.RunJobs();
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.True(string.IsNullOrEmpty(textBox.Text));
        }
        finally
        {
            window.Close();
            Application.Current.Resources.MergedDictionaries.Remove(theme);
        }
    }
}
