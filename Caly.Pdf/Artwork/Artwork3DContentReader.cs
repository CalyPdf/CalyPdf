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

using Caly.Pdf.Models;
using Scene3D;
using UglyToad.PdfPig.Artwork;

namespace Caly.Pdf.Artwork;

/// <summary>
/// Reads the payload and the views of 3D artwork out of the document.
/// <para>
/// Everything here goes through the document's token scanner, which is not thread-safe: call it only
/// while holding the document, never from the UI thread.
/// </para>
/// </summary>
public static class Artwork3DContentReader
{
    /// <summary>Most views read from one artwork; the same limit as the names the text layer carries.</summary>
    public const int MaxViews = PdfArtwork3D.MaxViewNames;

    /// <exception cref="Artwork3DContentException">The artwork's data stream could not be decoded.</exception>
    public static Artwork3DContent Read(PdfArtwork3D artwork)
    {
        ArgumentNullException.ThrowIfNull(artwork);
        Artwork3D source = artwork.Artwork;

        ReadOnlyMemory<byte> payload;
        try
        {
            payload = source.GetData();
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or InvalidDataException or NotSupportedException or ArgumentException)
        {
            throw new Artwork3DContentException(e.Message, e);
        }

        // The 3D view box where the document gives one, else the annotation's rectangle in PDF units.
        double scale = artwork.PpiScale > 0 ? artwork.PpiScale : 1;
        double width = source.ViewBox?.Width ?? artwork.BoundingBox.Width / scale;
        double height = source.ViewBox?.Height ?? artwork.BoundingBox.Height / scale;

        IReadOnlyList<Artwork3DView> authored = source.Views;
        Artwork3DView? defaultView = source.DefaultView;
        int count = Math.Min(authored.Count, MaxViews);
        var views = new List<SceneView>(count + 1);
        var names = new List<string>(count + 1);
        int defaultIndex = -1;
        for (int i = 0; i < count; i++)
        {
            Artwork3DView view = authored[i];
            if (ReferenceEquals(view, defaultView))
            {
                defaultIndex = i;
            }

            views.Add(Artwork3DViewConverter.Convert(view, width, height));
            names.Add(PdfArtwork3D.GetName(view));
        }

        if (defaultIndex < 0 && defaultView is not null)
        {
            // A default view that is not among the listed ones (the artwork's own, given by the stream).
            defaultIndex = views.Count;
            views.Add(Artwork3DViewConverter.Convert(defaultView, width, height));
            names.Add(PdfArtwork3D.GetName(defaultView));
        }
        else if (defaultIndex < 0 && views.Count > 0)
        {
            defaultIndex = 0;
        }

        return new Artwork3DContent
        {
            Payload = payload,
            Format = source.Format,
            Views = views,
            ViewNames = names,
            DefaultViewIndex = defaultIndex
        };
    }
}
