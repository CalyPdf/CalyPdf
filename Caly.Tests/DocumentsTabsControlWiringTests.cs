using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Caly.Core.Controls;
using Caly.Core.ViewModels;

namespace Caly.Tests;

/// <summary>
/// A torn-off tab moves into a window whose tab strip is built from
/// <c>DocumentsTabsControl.axaml</c>, so that window behaves exactly like the one it came from.
/// <para>
/// This pins the invariant that makes that possible: the declared strip really does carry its
/// commands and items binding. Before the <c>DetachedHostFactory</c> refactor, Tabalonia built a
/// bare strip and Caly re-applied all of this in code — duplicating what the XAML already
/// declares, and doing it with reflection bindings that Native AOT trimming strips.
/// </para>
/// </summary>
public class DocumentsTabsControlWiringTests
{
    [AvaloniaFact]
    public void DeclaredStrip_CarriesItsCommandsAndItemsFromXaml()
    {
        var viewModel = new MainViewModel();
        viewModel.Dispose();

        var control = new DocumentsTabsControl { DataContext = viewModel };
        var window = new Window { Width = 400, Height = 300, Content = control };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var tabs = control.TabsControl;

            // The strip handed to Tabalonia for a detached window is this one.
            Assert.NotNull(tabs);
            Assert.Same(viewModel.PdfDocuments, tabs.ItemsSource);
            Assert.Same(viewModel.OpenFileCommand, tabs.AddItemCommand);
            Assert.Same(viewModel.CloseTabCommand, tabs.CloseItemCommand);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Detaching is only wired up on desktop lifetimes; the headless test app has none, so the
    /// factory must stay null rather than trying to build a window that cannot exist.
    /// </summary>
    [AvaloniaFact]
    public void DeclaredStrip_LeavesDetachingOffWhenThereIsNoDesktopLifetime()
    {
        var viewModel = new MainViewModel();
        viewModel.Dispose();

        var control = new DocumentsTabsControl { DataContext = viewModel };
        var window = new Window { Width = 400, Height = 300, Content = control };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            Assert.False(control.TabsControl.EnableTabDetaching);
            Assert.False(control.TabsControl.EnableTabAttaching);
            Assert.Null(control.TabsControl.DetachedHostFactory);
        }
        finally
        {
            window.Close();
        }
    }
}
