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

using Avalonia;
using Avalonia.Input;
using Avalonia.VisualTree;
using Caly.Core.Models;
using Caly.Core.Utilities;
using Caly.Pdf.Models;

namespace Caly.Core.Controls;

// Hovering and clicking 3D artwork.
internal sealed partial class TextSelectionInputHandler
{
    /// <summary>
    /// How far, in page units, the pointer may stray between press and release and still have
    /// clicked a 3D artwork rather than dragged across it.
    /// </summary>
    private const double Artwork3DClickTolerance = 3.0;

    /// <summary>
    /// Asks the owner to show the 3D artwork under a click, if there is one.
    /// </summary>
    /// <returns><c>true</c> if the click was on 3D artwork, whether or not anything can show it.</returns>
    private bool TryActivateArtwork3D(PageInteractiveLayerControl control, Point? pressed, Point released)
    {
        if (control.PdfTextLayer is not { } textLayer || !control.PageNumber.HasValue)
        {
            return false;
        }

        PdfArtwork3D? artwork = textLayer.FindArtwork3DOver(released.X, released.Y, out int index);
        if (artwork is null)
        {
            return false;
        }

        // A press that wandered off before the release was a drag, even where there was no text to select.
        if (!pressed.HasValue || pressed.Value.Euclidean(released) > Artwork3DClickTolerance)
        {
            return true;
        }

        if (_owner.ActivateArtwork3D is { } command)
        {
            control.HideAnnotation();
            var request = new Artwork3DActivationRequest(control.PageNumber.Value, index, artwork);
            if (command.CanExecute(request))
            {
                command.Execute(request);

                // The click was on the model: the wheel goes to it, as after a click in an open view. Only here,
                // not wherever a view is built: a page scrolled back into view must leave the wheel to the page.
                control.FindAncestorOfType<PageItem>()?.FocusArtwork3DView();
            }
        }

        return true;
    }

    /// <summary>
    /// Shows the flyout, cursor and status of the 3D artwork under the pointer, if there is one.
    /// </summary>
    /// <returns><c>true</c> if the pointer is over 3D artwork.</returns>
    private bool TryShowArtwork3DOver(PageInteractiveLayerControl control, PointerPointProperties properties, Point loc)
    {
        PdfArtwork3D? artwork = control.PdfTextLayer!.FindArtwork3DOver(loc.X, loc.Y, out int artworkIndex);
        if (artwork is null)
        {
            return false;
        }

        if (!properties.IsRightButtonPressed)
        {
            control.ShowArtwork3D(artwork, artworkIndex);
        }

        // A hand where a click will open the model.
        if (control.GetArtwork3DHint(artwork, artworkIndex) == Artwork3DHint.ClickToView)
        {
            control.SetHandCursor();
        }
        else
        {
            control.SetDefaultCursor();
        }

        _owner.SetCurrentValue(PageItemsControl.InteractiveActionOverProperty, $"3D model ({artwork.FormatDisplayName})");
        return true;
    }
}
