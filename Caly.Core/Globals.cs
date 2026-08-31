using System;
using System.Reflection;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace Caly.Core;

public static class Globals
{
    public const string AppName = "Caly Pdf Reader";

    public static readonly string CalyVersion;

    static Globals()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

        string? version = assembly.GetName().Version?.ToString().Trim();
        CalyVersion = !string.IsNullOrEmpty(version) ? version : @"n/a";
    }

    public static bool IsMobilePlatform()
    {
        return OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();
    }

    /// <summary>
    /// Whether the app is running with a desktop windowing lifetime, and can therefore open
    /// additional <see cref="Avalonia.Controls.Window"/>s.
    /// </summary>
    public static bool IsDesktopLifetime()
    {
        return Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime;
    }
}