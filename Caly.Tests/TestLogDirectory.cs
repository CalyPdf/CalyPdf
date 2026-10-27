using System.Runtime.CompilerServices;

namespace Caly.Tests;

internal static class TestLogDirectory
{
    /// <summary>
    /// Several tests make the code under test throw on purpose, and Caly logs every caught
    /// exception to a file. Send those files to a temp folder instead of the user's real
    /// <c>%LOCALAPPDATA%\Caly\logs</c>.
    /// </summary>
    [ModuleInitializer]
    internal static void Initialize()
    {
        Core.Debug.LogDirectory = Path.Combine(Path.GetTempPath(), "Caly.Tests", "logs");
    }
}
