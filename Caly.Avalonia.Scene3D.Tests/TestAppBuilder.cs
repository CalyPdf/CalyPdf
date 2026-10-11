using Avalonia;
using Avalonia.Headless;
using Caly.Avalonia.Scene3D.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Caly.Avalonia.Scene3D.Tests;

/// <summary>
/// The headless Avalonia application the control tests run in. Headless has no GPU: OpenGL never
/// initialises, which is exactly the situation of the software renderer.
/// </summary>
public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
