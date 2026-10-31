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

/// <summary>
/// How a document's zoom is chosen. A fit mode stays on, re-fitting as the viewer changes size,
/// until the user zooms by hand.
/// <para>
/// Written to the settings file by name (<see cref="CalySettings.DefaultZoomMode"/>).
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ZoomMode>))]
public enum ZoomMode : byte
{
    /// <summary>
    /// The zoom is set by hand. Nothing re-fits.
    /// </summary>
    Manual = 0,

    /// <summary>
    /// The whole current page is visible.
    /// </summary>
    FitPage = 1,

    /// <summary>
    /// The current page's width fills the viewer.
    /// </summary>
    FitWidth = 2,

    /// <summary>
    /// The whole content box of the current page (the page without its margins) is visible.
    /// </summary>
    FitContent = 3,

    /// <summary>
    /// The width of the current page's content box fills the viewer.
    /// </summary>
    FitContentWidth = 4
}
