using System;
using System.Reflection;

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
}