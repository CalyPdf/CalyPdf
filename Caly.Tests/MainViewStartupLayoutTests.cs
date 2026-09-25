using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Caly.Core.Views;
using Caly.Core.ViewModels;
using System.Reflection;

namespace Caly.Tests;

/// <summary>
/// Regression tests for https://github.com/CalyPdf/CalyPdf/issues/321: Caly crashed on every
/// startup, before the main window appeared, on some display scalings.
/// <para>
/// <c>MainWindow</c> is shown at the 250 px width declared in its XAML; the saved size is only
/// applied once it has opened. At that width the tab strip's parts are measured down to fill it
/// exactly, then layout rounding at scalings like 110% arranges the strip a fraction of a pixel
/// narrower than it was measured. Tabalonia's <c>TopPanel</c> gave the tabs what was left, a
/// negative width, and <c>Arrange</c> threw "Invalid Arrange rectangle".
/// </para>
/// </summary>
public class MainViewStartupLayoutTests
{
    /// <summary>
    /// The headless platform always reports a render scaling of 1. Overrides it on a window that
    /// has not been shown yet, the way a real platform reports a monitor's DPI.
    /// </summary>
    private static void SetRenderScaling(Window window, double scaling)
    {
        var impl = window.PlatformImpl!;
        var backingField = impl.GetType().GetField("<RenderScaling>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(backingField); // Headless implementation changed: update this helper.
        backingField.SetValue(impl, scaling);

        // The window caches its scaling, and picks up a change through this callback.
        var scalingChanged = (Action<double>?)impl.GetType().GetProperty("ScalingChanged")!.GetValue(impl);
        Assert.NotNull(scalingChanged);
        scalingChanged(scaling);

        Assert.Equal(scaling, window.RenderScaling);
    }

    [AvaloniaTheory]
    [InlineData(1.00)]
    [InlineData(1.10)]
    [InlineData(2.20)]
    [InlineData(2.70)]
    [InlineData(1.25)]
    [InlineData(1.50)]
    [InlineData(1.75)]
    [InlineData(2.00)]
    [InlineData(2.25)]
    [InlineData(2.50)]
    [InlineData(3.00)]
    public void MainView_AtTheInitialWindowWidth_ShowsWithoutThrowing(double scaling)
    {
        var viewModel = new MainViewModel();
        viewModel.Dispose();

        // Mirrors MainWindow.axaml: its initial and minimum width, and the extended client area.
        var window = new Window
        {
            Width = 250,
            MinWidth = 250,
            Height = 150,
            ExtendClientAreaToDecorationsHint = true,
            Content = new Border { Child = new MainView { DataContext = viewModel } }
        };
        SetRenderScaling(window, scaling);

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            window.Close();
        }
    }
}
