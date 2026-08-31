using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Caly.Core.Controls;
using Caly.Core.Services;
using Caly.Core.ViewModels;
using Tabalonia.Controls;

namespace Caly.Tests;

/// <summary>
/// The side pane is window state, so <c>DocumentTabView</c> reaches it by walking up to its
/// host <see cref="TabsControl"/>, whose DataContext is that window's
/// <see cref="MainViewModel"/> — not through its document, which changes window whenever its
/// tab is dragged.
/// <para>
/// These tests pin the mechanism that binding relies on: content built by
/// <c>ContentTemplate</c> is hosted in <c>PART_SelectedContentHost</c> inside the strip's
/// template, and must be able to find the strip as an ancestor. If that ever stops holding,
/// the sidebar silently stops responding — a runtime binding failure, not a build error.
/// </para>
/// </summary>
public class DocumentTabViewPaneBindingTests
{
    private sealed class TabModel
    {
        public string Header { get; init; } = "tab";
    }

    /// <summary>
    /// Builds a real Tabalonia strip whose ContentTemplate resolves the ancestor strip's
    /// DataContext, exactly as DocumentTabView.axaml's IsPaneOpen binding does.
    /// </summary>
    private static (Window window, ContentControl probe) BuildStrip(MainViewModel viewModel)
    {
        ContentControl? probe = null;

        var tabs = new TabsControl
        {
            DataContext = viewModel,
            ItemsSource = new[] { new TabModel() },
            ItemTemplate = new FuncDataTemplate<TabModel>((m, _) => new TextBlock { Text = m?.Header }),
            ContentTemplate = new FuncDataTemplate<TabModel>((_, _) =>
            {
                probe = new ContentControl();
                probe.Bind(ContentControl.ContentProperty, new Binding("DataContext")
                {
                    RelativeSource = new RelativeSource
                    {
                        Mode = RelativeSourceMode.FindAncestor,
                        AncestorType = typeof(TabsControl)
                    }
                });
                return probe;
            })
        };

        var window = new Window { Width = 400, Height = 300, Content = tabs };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(probe);
        return (window, probe!);
    }

    [AvaloniaFact]
    public void TabContent_CanResolveItsHostStripsViewModel()
    {
        var viewModel = new MainViewModel();
        viewModel.Dispose();

        var (window, probe) = BuildStrip(viewModel);

        try
        {
            Assert.Same(viewModel, probe.Content);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Two strips in two windows resolve their own view model, which is what makes the pane
    /// setting per-window rather than shared.
    /// </summary>
    [AvaloniaFact]
    public void TabContentInTwoWindows_ResolvesEachWindowsOwnViewModel()
    {
        var first = new MainViewModel();
        var second = new MainViewModel();
        first.Dispose();
        second.Dispose();

        var (firstWindow, firstProbe) = BuildStrip(first);
        var (secondWindow, secondProbe) = BuildStrip(second);

        try
        {
            Assert.Same(first, firstProbe.Content);
            Assert.Same(second, secondProbe.Content);
            Assert.NotSame(firstProbe.Content, secondProbe.Content);
        }
        finally
        {
            firstWindow.Close();
            secondWindow.Close();
        }
    }

    /// <summary>
    /// Builds a strip whose tab content two-way binds a bool straight to the host window's
    /// <see cref="MainViewModel.IsDocumentPaneOpen"/> — the shape DocumentTabView's IsPaneOpen
    /// binding uses.
    /// </summary>
    private static (Window window, ToggleButton toggle) BuildBoundStrip(MainViewModel viewModel)
    {
        ToggleButton? toggle = null;

        var tabs = new TabsControl
        {
            DataContext = viewModel,
            ItemsSource = new[] { new TabModel() },
            ItemTemplate = new FuncDataTemplate<TabModel>((m, _) => new TextBlock { Text = m?.Header }),
            ContentTemplate = new FuncDataTemplate<TabModel>((_, _) =>
            {
                toggle = new ToggleButton();
                toggle.Bind(ToggleButton.IsCheckedProperty,
                    new Binding($"DataContext.{nameof(MainViewModel.IsDocumentPaneOpen)}")
                    {
                        Mode = BindingMode.TwoWay,
                        RelativeSource = new RelativeSource
                        {
                            Mode = RelativeSourceMode.FindAncestor,
                            AncestorType = typeof(TabsControl)
                        }
                    });
                return toggle;
            })
        };

        var window = new Window { Width = 400, Height = 300, Content = tabs };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(toggle);
        return (window, toggle!);
    }

    /// <summary>
    /// Regression for M27: closing the sidebar then tearing the tab off gave a new window with
    /// the sidebar open. Detach copies the flag off the source window's view model, so the
    /// toggle must actually reach the view model — not just change the view.
    /// </summary>
    [AvaloniaFact]
    public void TogglingTheTabContent_WritesBackToItsWindowsViewModel()
    {
        var viewModel = new MainViewModel();
        viewModel.Dispose();
        Assert.True(viewModel.IsDocumentPaneOpen);

        var (window, toggle) = BuildBoundStrip(viewModel);

        try
        {
            Assert.True(toggle.IsChecked);

            // The user closes the sidebar.
            toggle.IsChecked = false;
            Dispatcher.UIThread.RunJobs();

            Assert.False(viewModel.IsDocumentPaneOpen);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Stand-in with the same shape as <c>DocumentTabView</c>: a styled bool property that the
    /// XAML binds to the host window's view model, with an inner control bound to that property.
    /// </summary>
    private sealed class PaneHost : UserControl
    {
        public static readonly StyledProperty<bool> IsPaneOpenProperty =
            AvaloniaProperty.Register<PaneHost, bool>(nameof(IsPaneOpen), defaultBindingMode: BindingMode.TwoWay);

        public bool IsPaneOpen
        {
            get => GetValue(IsPaneOpenProperty);
            set => SetValue(IsPaneOpenProperty, value);
        }

        public ToggleButton Toggle { get; }

        public PaneHost()
        {
            Toggle = new ToggleButton();

            // Hop 1: the inner control tracks this control's property.
            Toggle.Bind(ToggleButton.IsCheckedProperty,
                new Binding(nameof(IsPaneOpen)) { Mode = BindingMode.TwoWay, Source = this });

            Content = Toggle;
        }
    }

    /// <summary>
    /// Regression for M27. The real chain is two hops — ToggleButton.IsChecked ↔
    /// DocumentTabView.IsPaneOpen ↔ MainViewModel.IsDocumentPaneOpen — and only the second hop
    /// leaves the view. If it fails, the sidebar still toggles (so M26 passes) while the view
    /// model keeps its default, and a torn-off window inherits the wrong state.
    /// </summary>
    [AvaloniaFact]
    public void TogglingThroughTheStyledProperty_ReachesTheWindowsViewModel()
    {
        var viewModel = new MainViewModel();
        viewModel.Dispose();

        PaneHost? host = null;
        var tabs = new TabsControl
        {
            DataContext = viewModel,
            ItemsSource = new[] { new TabModel() },
            ItemTemplate = new FuncDataTemplate<TabModel>((m, _) => new TextBlock { Text = m?.Header }),
            ContentTemplate = new FuncDataTemplate<TabModel>((_, _) =>
            {
                host = new PaneHost();

                // Hop 2: this control tracks the host window's view model.
                host.Bind(PaneHost.IsPaneOpenProperty,
                    new Binding($"DataContext.{nameof(MainViewModel.IsDocumentPaneOpen)}")
                    {
                        Mode = BindingMode.TwoWay,
                        RelativeSource = new RelativeSource
                        {
                            Mode = RelativeSourceMode.FindAncestor,
                            AncestorType = typeof(TabsControl)
                        }
                    });

                return host;
            })
        };

        var window = new Window { Width = 400, Height = 300, Content = tabs };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            Assert.NotNull(host);
            Assert.True(viewModel.IsDocumentPaneOpen);
            Assert.True(host!.IsPaneOpen);

            // The user closes the sidebar via the toggle.
            host.Toggle.IsChecked = false;
            Dispatcher.UIThread.RunJobs();

            Assert.False(host.IsPaneOpen);
            Assert.False(viewModel.IsDocumentPaneOpen);
        }
        finally
        {
            window.Close();
        }
    }



}
