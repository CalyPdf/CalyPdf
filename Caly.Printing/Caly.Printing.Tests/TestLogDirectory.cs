using System.Runtime.CompilerServices;

namespace Caly.Printing.Tests;

internal static class TestLogDirectory
{
    /// <summary>
    /// Caly logs every caught exception to a file. Send those files to a temp folder instead
    /// of the user's real <c>%LOCALAPPDATA%\Caly\logs</c>.
    /// </summary>
    [ModuleInitializer]
    internal static void Initialize()
    {
        Caly.Core.Debug.LogDirectory = Path.Combine(Path.GetTempPath(), "Caly.Printing.Tests", "logs");
    }
}
