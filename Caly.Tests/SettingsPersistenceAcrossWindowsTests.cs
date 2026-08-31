using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Caly.Core.Services;
using Caly.Core.Services.Interfaces;
using Caly.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Caly.Tests;

/// <summary>
/// Settings are written by whichever window closes last, not by the one created at startup.
/// <para>
/// <c>Save</c> is only ever called from the closing handler, and that handler used to be hooked
/// to the startup window alone. That was airtight under <c>ShutdownMode.OnMainWindowClose</c>,
/// where closing that window meant exiting. Under <c>OnLastWindowClose</c> it closes as soon as
/// its last tab is dragged elsewhere (M6/M7), after which nothing was ever saved again - the
/// surviving window's geometry and every later <c>PaneSize</c> were discarded at exit.
/// </para>
/// <para>
/// These tests never call <c>Load</c> or <c>GetSettings</c>, so <c>_current</c> stays null and
/// <c>Save</c> short-circuits. Nothing here touches the real settings file.
/// </para>
/// </summary>
public class SettingsPersistenceAcrossWindowsTests
{
    private static CalyWindowContext NewContext(Window? window, bool isPrimary)
    {
        var viewModel = new MainViewModel();
        viewModel.Dispose();
        return new CalyWindowContext { ViewModel = viewModel, Window = window, IsPrimary = isPrimary };
    }

    /// <summary>
    /// The registry has to announce new windows, or a service resolved at startup can never
    /// learn about a window torn off later.
    /// </summary>
    [AvaloniaFact]
    public void Register_AnnouncesTheWindow()
    {
        var registry = new CalyWindowRegistry();
        var seen = new List<CalyWindowContext>();
        registry.WindowRegistered += (_, context) => seen.Add(context);

        var first = NewContext(null, isPrimary: true);
        var second = NewContext(null, isPrimary: false);
        registry.Register(first);
        registry.Register(second);

        Assert.Equal([first, second], seen);
    }

    /// <summary>
    /// Registering the same context twice is already a no-op; it must not announce twice
    /// either, or the settings service would double-hook the window.
    /// </summary>
    [AvaloniaFact]
    public void Register_AnnouncesEachWindowOnlyOnce()
    {
        var registry = new CalyWindowRegistry();
        int announced = 0;
        registry.WindowRegistered += (_, _) => announced++;

        var context = NewContext(null, isPrimary: true);
        registry.Register(context);
        registry.Register(context);

        Assert.Equal(1, announced);
    }

    /// <summary>
    /// The regression: the startup window closing while another remains must not be treated as
    /// the end of the session.
    /// </summary>
    [AvaloniaFact]
    public void ThePrimaryWindowClosingWhileAnotherRemains_IsNotTheLastWindow()
    {
        var registry = new CalyWindowRegistry();
        var primaryWindow = new Window();
        var detachedWindow = new Window();
        registry.Register(NewContext(primaryWindow, isPrimary: true));
        registry.Register(NewContext(detachedWindow, isPrimary: false));

        var settings = new JsonSettingsService(primaryWindow, registry);

        Assert.False(settings.IsLastWindow(primaryWindow));
    }

    /// <summary>
    /// ...and the detached window that outlives it is, so it is the one that writes.
    /// </summary>
    [AvaloniaFact]
    public void TheLastWindowLeft_IsTheOneThatWrites()
    {
        var registry = new CalyWindowRegistry();
        var primaryWindow = new Window();
        var detachedWindow = new Window();
        var primaryContext = NewContext(primaryWindow, isPrimary: true);
        registry.Register(primaryContext);
        registry.Register(NewContext(detachedWindow, isPrimary: false));

        var settings = new JsonSettingsService(primaryWindow, registry);

        // The startup window has gone; only the detached one is left.
        registry.Unregister(primaryContext);

        Assert.True(settings.IsLastWindow(detachedWindow));
    }

    /// <summary>
    /// The ordinary single-window session still writes on the way out.
    /// </summary>
    [AvaloniaFact]
    public void TheOnlyWindow_IsTheLastWindow()
    {
        var registry = new CalyWindowRegistry();
        var window = new Window();
        registry.Register(NewContext(window, isPrimary: true));

        var settings = new JsonSettingsService(window, registry);

        Assert.True(settings.IsLastWindow(window));
    }

    /// <summary>
    /// Without a registry - the design surface, and any test that does not supply one - the
    /// service keeps its old behaviour rather than silently never saving.
    /// </summary>
    [AvaloniaFact]
    public void WithoutARegistry_TheWindowStillWrites()
    {
        var window = new Window();
        var settings = new JsonSettingsService(window);

        Assert.True(settings.IsLastWindow(window));
    }

    /// <summary>
    /// The fix relies on the container actually handing the registry to the optional
    /// constructor parameter; if it fell back to the one-argument path the service would go on
    /// tracking only the startup window.
    /// </summary>
    [AvaloniaFact]
    public void TheContainerInjectsTheRegistry()
    {
        var registry = new CalyWindowRegistry();
        var primaryWindow = new Window();
        var detachedWindow = new Window();
        registry.Register(NewContext(primaryWindow, isPrimary: true));
        registry.Register(NewContext(detachedWindow, isPrimary: false));

        // Mirrors App.axaml.cs: the registry is a pre-built singleton, the settings service is
        // resolved from the container.
        var services = new ServiceCollection();
        services.AddSingleton<ICalyWindowRegistry>(registry);
        services.AddSingleton<Avalonia.Visual>(_ => primaryWindow);
        services.AddSingleton<ISettingsService, JsonSettingsService>();

        var settings = (JsonSettingsService)services.BuildServiceProvider()
            .GetRequiredService<ISettingsService>();

        // Registry-aware: two windows are open, so this one is not the last.
        Assert.False(settings.IsLastWindow(primaryWindow));
    }
}
