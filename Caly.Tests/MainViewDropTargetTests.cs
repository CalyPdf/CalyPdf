using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Caly.Core.ViewModels;
using Caly.Core.Views;

namespace Caly.Tests;

/// <summary>
/// A file dropped on a window must open in <b>that</b> window.
/// <para>
/// Dropping does not activate the window under the pointer, so the drop target cannot be
/// resolved from the window registry's active window the way keyboard- and menu-driven opens
/// are: with two windows open, a PDF dropped on the unfocused one opened as a tab in the other.
/// <see cref="MainView.DropTarget"/> pins where the target comes from instead.
/// </para>
/// <para>
/// This covers the routing decision, not the <c>DragDrop</c> event plumbing that delivers the
/// files - that stays covered by the manual cases in <c>docs/tab-detach-reattach-tests.md</c>.
/// </para>
/// </summary>
public class MainViewDropTargetTests
{
    private static MainViewModel NewMainViewModel()
    {
        var vm = new MainViewModel();
        vm.Dispose();
        return vm;
    }

    [AvaloniaFact]
    public void DropTarget_IsTheViewsOwnWindowNotWhicheverIsFocused()
    {
        MainViewModel first = NewMainViewModel();
        MainViewModel second = NewMainViewModel();

        var firstWindow = new Window { Width = 400, Height = 300, DataContext = first, Content = new MainView() };
        var secondWindow = new Window { Width = 400, Height = 300, DataContext = second, Content = new MainView() };

        firstWindow.Show();
        secondWindow.Show();

        // The second window has focus; a drop on the first must still go to the first.
        secondWindow.Activate();
        Dispatcher.UIThread.RunJobs();

        try
        {
            Assert.Same(first, ((MainView)firstWindow.Content!).DropTarget);
            Assert.Same(second, ((MainView)secondWindow.Content!).DropTarget);
        }
        finally
        {
            secondWindow.Close();
            firstWindow.Close();
        }
    }

    /// <summary>
    /// With no view model to read - the design-time surface - the drop falls back to the active
    /// window inside the manager, which is what this path did before it carried a target.
    /// </summary>
    [AvaloniaFact]
    public void DropTarget_IsNullWhenTheViewHasNoViewModel()
    {
        var view = new MainView();
        var window = new Window { Width = 400, Height = 300, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            Assert.Null(view.DropTarget);
        }
        finally
        {
            window.Close();
        }
    }
}
