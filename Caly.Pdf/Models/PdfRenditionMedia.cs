// Copyright (c) 2025 BobLd
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

using UglyToad.PdfPig.Actions;

namespace Caly.Pdf.Models;

/// <summary>
/// The embedded media of a Rendition action (PDF 2.0, 12.6.4.13 "Rendition actions" and 13.2 "Multimedia"),
/// extracted from a media rendition's media clip. Only self-contained embedded audio is currently supported.
/// </summary>
public sealed class PdfRenditionMedia
{
    /// <summary>
    /// The decoded media bytes (e.g. the raw contents of the embedded <c>.mp3</c> file).
    /// </summary>
    public required byte[] Data { get; init; }

    /// <summary>
    /// The media clip content type (the media clip data <c>/CT</c> entry), e.g. <c>audio/mpeg</c>,
    /// or <see langword="null"/> when absent.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// The embedded file name (the file specification <c>/UF</c> or <c>/F</c> entry), e.g.
    /// <c>lick1p.mp3</c>, or <see langword="null"/> when absent. Useful to derive a file extension.
    /// </summary>
    public string? FileName { get; init; }

    /// <summary>
    /// The operation the rendition action requests (the rendition action <c>/OP</c> entry).
    /// </summary>
    public required RenditionOperation Operation { get; init; }
}
