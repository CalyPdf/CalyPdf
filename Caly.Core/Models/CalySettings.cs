// Copyright (c) BobLd
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System.Text.Json.Serialization;

namespace Caly.Core.Models;

public sealed class CalySettings
{
    public static readonly CalySettings Default = new CalySettings()
    {
        Width = 1000,
        Height = 500,
        PaneSize = 350,
        Debug = null
    };

    // TODO - Add version for compatibility checks

    public int Width { get; set; }

    public int Height { get; set; }

    public bool IsMaximised { get; set; }

    public int PaneSize { get; set; }

    public bool ShowPdfLogs { get; set; }

    public bool UseSoftwareRendering { get; set; }

    /// <summary>
    /// Which theme variant the interface uses. Defaults to <see cref="CalyTheme.Dark"/>,
    /// which is what Caly has always rendered, so an existing install does not change
    /// appearance on upgrade.
    /// </summary>
    public CalyTheme Theme { get; set; }

    /// <summary>
    /// Renders tiles as <c>Rgb565</c> (2 bytes/pixel, no alpha channel)
    /// instead of <c>Bgra8888</c> (4 bytes/pixel).
    /// </summary>
    public bool UseCompactTileFormat { get; set; } // TODO - Might be a good default for mobile platform

    public CalySettingsDebug? Debug { get; set; }


    public sealed class CalySettingsDebug
    {
        public bool Render { get; set; }
        public bool Layout { get; set; }
        public bool Fps { get; set; }
        public bool DirtyRects { get; set; }

        /// <summary>
        /// Dumps render-path timings to the logs folder at exit. Absent means off.
        /// </summary>
        public bool LogRenderTimings { get; set; }
    }
    
    public enum CalySettingsProperty
    {
        PaneSize = 0
    }
}

/// <summary>
/// Written to the settings file by name rather than by number, so the file stays
/// readable and hand-editable like the rest of it.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CalyTheme>))]
public enum CalyTheme
{
    Dark = 0,
    Light = 1,

    /// <summary>
    /// Follows the operating system's light/dark setting, and tracks it while Caly runs.
    /// </summary>
    System = 2
}
