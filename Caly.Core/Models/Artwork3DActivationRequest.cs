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

using Caly.Avalonia.Scene3D;
using Caly.Pdf.Models;

namespace Caly.Core.Models;

/// <summary>
/// A request to view a piece of 3D artwork in place: what a click on it sends to the document.
/// </summary>
/// <param name="PageNumber">The page the artwork is on.</param>
/// <param name="ArtworkIndex">Its position in the page's <see cref="PdfTextLayer.Artworks3D"/>.</param>
/// <param name="Artwork">The artwork, as the page's text layer holds it at the time of the click.</param>
public sealed record Artwork3DActivationRequest(int PageNumber, int ArtworkIndex, PdfArtwork3D Artwork);

/// <summary>
/// What the 3D control says about itself, passed from the view to the view model of the artwork.
/// </summary>
public readonly record struct Artwork3DControlStatus(Scene3DStatus Status, string? Message);
