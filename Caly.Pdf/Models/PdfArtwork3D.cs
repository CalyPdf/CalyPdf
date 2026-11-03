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

using UglyToad.PdfPig.Artwork;
using UglyToad.PdfPig.Core;

namespace Caly.Pdf.Models;

/// <summary>
/// 3D artwork (U3D or PRC) placed on a page by a 3D or rich media annotation.
/// <para>
/// Carries what is needed to show that the artwork is there, as plain values captured while the page was
/// processed. Nothing is decoded: the 3D data stays in the document until <see cref="Artwork3D.GetData"/> is called.
/// </para>
/// </summary>
public sealed class PdfArtwork3D
{
    internal const int MaxViewNames = 1_000;

    public required double PpiScale { get; init; }

    /// <summary>
    /// The rectangle of the annotation, in the same space as <see cref="PdfAnnotation.BoundingBox"/>.
    /// </summary>
    public required PdfRectangle BoundingBox { get; init; }

    /// <summary>
    /// The PDF-level description of the artwork: format, data, views and activation settings.
    /// <para>
    /// Its views and data are read from the document on demand, through the document's token scanner,
    /// which is not thread-safe: touch <see cref="Artwork3D.Views"/>, <see cref="Artwork3D.DefaultView"/>,
    /// <see cref="Artwork3D.GetData"/> and <see cref="Artwork3D.GetScript"/> only while holding the
    /// document, never from the UI thread. What the UI needs is on this class as plain values.
    /// </para>
    /// </summary>
    public required Artwork3D Artwork { get; init; }

    /// <summary>
    /// Whether the annotation has a normal appearance, the poster drawn with the page. When
    /// <see langword="false"/> the page shows nothing at this rectangle.
    /// </summary>
    public required bool HasNormalAppearance { get; init; }

    /// <summary>
    /// The names of the authored views, in presentation order.
    /// </summary>
    public required IReadOnlyList<string> ViewNames { get; init; }

    /// <summary>
    /// The name of the view shown initially, or <see langword="null"/> when the view defined inside the
    /// 3D data is used.
    /// </summary>
    public string? DefaultViewName { get; init; }

    public Artwork3DFormat Format => Artwork.Format;

    /// <summary>
    /// The format as shown to the user: "U3D", "PRC", or the name the document gives an unknown format.
    /// </summary>
    public string FormatDisplayName => Format switch
    {
        Artwork3DFormat.U3D => "U3D",
        Artwork3DFormat.Prc => "PRC",
        _ => string.IsNullOrEmpty(Artwork.FormatName) ? "unknown" : Artwork.FormatName
    };

    /// <summary>
    /// Reads the view names out of the document. Call while processing the page.
    /// </summary>
    internal static IReadOnlyList<string> GetViewNames(Artwork3D artwork)
    {
        IReadOnlyList<Artwork3DView> views = artwork.Views;
        if (views.Count == 0)
        {
            return [];
        }

        var names = new string[Math.Min(views.Count, MaxViewNames)];
        for (int i = 0; i < names.Length; ++i)
        {
            names[i] = GetName(views[i]);
        }

        return names;
    }

    /// <summary>
    /// Reads the default view's name out of the document. Call while processing the page.
    /// </summary>
    internal static string? GetDefaultViewName(Artwork3D artwork)
    {
        return artwork.DefaultView is { } view ? GetName(view) : null;
    }

    internal static string GetName(Artwork3DView view)
    {
        if (!string.IsNullOrWhiteSpace(view.ExternalName))
        {
            return view.ExternalName;
        }

        return string.IsNullOrWhiteSpace(view.InternalName) ? "(unnamed)" : view.InternalName;
    }
}
