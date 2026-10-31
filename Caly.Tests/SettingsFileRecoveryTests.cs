using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Caly.Core.Services;
using System;
using System.IO;

namespace Caly.Tests;

/// <summary>
/// The settings file is hand-edited (CLAUDE.md documents <c>UseSoftwareRendering</c>), so an
/// unreadable file must be kept for repair, and a save must never leave a truncated file.
/// Each test works on its own temp file, never the real one.
/// </summary>
public class SettingsFileRecoveryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"caly_settings_test_{Guid.NewGuid():N}");
    private readonly string _file;

    public SettingsFileRecoveryTests()
    {
        Directory.CreateDirectory(_dir);
        _file = Path.Combine(_dir, "caly_settings");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [AvaloniaFact]
    public void TypoInFile_IsKeptAsBad_AndDefaultsAreUsed()
    {
        const string withTrailingComma = """{ "UseSoftwareRendering": true, }""";
        File.WriteAllText(_file, withTrailingComma);

        var settings = new JsonSettingsService(new Border(), settingsFile: _file);
        settings.Load();

        Assert.NotNull(settings.GetSettings());
        Assert.False(settings.GetSettings().UseSoftwareRendering);
        Assert.Equal(withTrailingComma, File.ReadAllText(_file + ".bad"));
    }

    [AvaloniaFact]
    public void NullFile_FallsBackToDefaults()
    {
        File.WriteAllText(_file, "null");

        var settings = new JsonSettingsService(new Border(), settingsFile: _file);
        settings.Load();

        Assert.NotNull(settings.GetSettings());
        Assert.True(File.Exists(_file + ".bad"));
    }

    [AvaloniaFact]
    public void Save_ReplacesFile_AndLeavesNoTempFile()
    {
        File.WriteAllText(_file, """{ "PaneSize": 300 }""");

        var settings = new JsonSettingsService(new Border(), settingsFile: _file);
        settings.Load();
        settings.GetSettings().PaneSize = 420;
        settings.Save();

        Assert.False(File.Exists(_file + ".tmp"));

        var reloaded = new JsonSettingsService(new Border(), settingsFile: _file);
        reloaded.Load();
        Assert.Equal(420, reloaded.GetSettings().PaneSize);
    }
}
