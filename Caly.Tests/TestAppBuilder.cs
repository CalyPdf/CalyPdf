using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent2;
using Caly.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Caly.Tests;

/// <summary>
/// Headless Avalonia application for control-lifecycle tests. Uses a bare
/// <see cref="Application"/> with the Fluent 2 theme (for built-in control templates
/// such as ScrollViewer); Caly controls under test supply inline templates.
/// </summary>
public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<TestApp>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public class TestApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new Fluent2Theme());

        // Tabalonia's theme supplies the TabsControl template, including the
        // PART_SelectedContentHost that tab content is hosted in.
        Styles.Add(new Tabalonia.Themes.Fluent.FluentTheme());
    }
}
