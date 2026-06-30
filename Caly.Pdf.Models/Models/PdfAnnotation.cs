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

using UglyToad.PdfPig.Actions;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics.Colors;

namespace Caly.Pdf.Models;

public sealed class PdfAnnotation
{
    public required double PpiScale { get; init; }

    /// <summary>
    /// The rectangle completely containing the block.
    /// </summary>
    public required PdfRectangle BoundingBox { get; init; }

    public PdfAction? Action { get; init; }

    public bool IsInteractive { get; init; }

    public string? Content { get; init; }

    public string? Date { get; init; }

    /// <summary>
    /// Represents a colour used for the following purposes:
    /// <list type="bullet">
    /// <item>The background of the annotation’s icon when closed.</item>
    /// <item>The title bar of the annotation’s popup window.</item>
    /// <item>The border of a link annotation.</item>
    /// </list>
    /// </summary>
    public IColor? Colour { get; init; }
}