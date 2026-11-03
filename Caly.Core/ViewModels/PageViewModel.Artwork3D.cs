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

using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;

namespace Caly.Core.ViewModels;

// The page's 3D artwork: the one being viewed in place, and those that cannot be shown.
public sealed partial class PageViewModel
{
    /// <summary>
    /// The 3D artwork of this page that is being viewed in place, if any. Set by the document.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveArtwork3DIndex))]
    private Artwork3DViewModel? _activeArtwork3D;

    /// <summary>
    /// Position of <see cref="ActiveArtwork3D"/> in the text layer's artworks, or -1 when none is active.
    /// </summary>
    public int ActiveArtwork3DIndex => ActiveArtwork3D?.ArtworkIndex ?? -1;

    /// <summary>
    /// Why 3D artworks of this page cannot be shown, for those that were read and refused: the reason
    /// by the artwork's position in the text layer's artworks. Null until there is one. What reading a
    /// model finds is true of it for as long as the document is open, so it is found once.
    /// </summary>
    [ObservableProperty] private IReadOnlyDictionary<int, string>? _artwork3DRefusals;

    /// <summary>Remembers why an artwork of this page cannot be shown.</summary>
    internal void RefuseArtwork3D(int artworkIndex, string reason)
    {
        // A new dictionary each time: the page's layer control is told by the property changing.
        var refusals = Artwork3DRefusals is null ? new Dictionary<int, string>() : new Dictionary<int, string>(Artwork3DRefusals);
        refusals[artworkIndex] = reason;
        Artwork3DRefusals = refusals;
    }
}
