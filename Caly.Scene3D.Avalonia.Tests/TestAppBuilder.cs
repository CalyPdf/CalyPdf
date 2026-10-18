using Avalonia;
using Avalonia.Headless;
using Caly.Scene3D.Avalonia.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Caly.Scene3D.Avalonia.Tests;

/// <summary>
/// The headless Avalonia application the control tests run in. Headless has no GPU: OpenGL never
/// initialises, which is exactly the situation of the software renderer.
/// </summary>
public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
