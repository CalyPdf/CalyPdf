using System.Text.Json;
using Caly.Core.Models;
using Caly.Core.Services;

namespace Caly.Tests;

/// <summary>
/// <see cref="CalySettings.DefaultZoomMode"/> is the zoom a document opens with. It is written to
/// the settings file by name, so the file stays hand-editable, and a value Caly does not know is
/// dropped when the file is read.
/// </summary>
public class DefaultZoomModeSettingTests
{
    private static CalySettings Read(string json)
        => JsonSerializer.Deserialize(json, SourceGenerationContext.Default.CalySettings)!;

    [Fact]
    public void DefaultZoomMode_IsWrittenByName()
    {
        string json = JsonSerializer.Serialize(new CalySettings { DefaultZoomMode = ZoomMode.FitContentWidth },
            SourceGenerationContext.Default.CalySettings);

        Assert.Contains("\"DefaultZoomMode\":\"FitContentWidth\"", json);
        Assert.Equal(ZoomMode.FitContentWidth, Read(json).DefaultZoomMode);
    }

    [Fact]
    public void DefaultZoomMode_Absent_IsManual()
    {
        Assert.Equal(ZoomMode.Manual, Read("{}").DefaultZoomMode);
    }

    [Fact]
    public void DefaultZoomMode_Undefined_IsResetWhenValidated()
    {
        var settings = Read("{\"DefaultZoomMode\":42}");

        JsonSettingsService.ValidateSetting(settings);

        Assert.Equal(ZoomMode.Manual, settings.DefaultZoomMode);
    }

    [Fact]
    public void DefaultSettings_OpenAtActualSize()
    {
        Assert.Equal(ZoomMode.Manual, CalySettings.Default.DefaultZoomMode);
    }
}
