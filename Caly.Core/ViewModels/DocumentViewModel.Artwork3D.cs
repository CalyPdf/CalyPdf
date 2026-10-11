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

using Avalonia.Controls.Notifications;
using Caly.Core.Models;
using Caly.Core.Services;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using UglyToad.PdfPig.Artwork;

namespace Caly.Core.ViewModels;

public partial class DocumentViewModel
{
    /// <summary>
    /// The 3D artwork being viewed in place, if any. One at most per document: a 3D view holds GPU
    /// memory for as long as it is on screen.
    /// </summary>
    public Artwork3DViewModel? ActiveArtwork3D { get; private set; }

    /// <summary>
    /// Shows a piece of 3D artwork in place of its poster, closing the one shown before. Says why, and
    /// does nothing else, when it cannot.
    /// </summary>
    [RelayCommand]
    private void ActivateArtwork3D(Artwork3DActivationRequest? request)
    {
        Debug.ThrowNotOnUiThread();

        if (request is null || request.PageNumber <= 0 || request.PageNumber > Pages.Count)
        {
            return;
        }

        if (ActiveArtwork3D is { } current && current.PageNumber == request.PageNumber &&
            current.ArtworkIndex == request.ArtworkIndex)
        {
            return;
        }

        if (request.Artwork.Format != Artwork3DFormat.U3D && request.Artwork.Format != Artwork3DFormat.Prc)
        {
            NotifyArtwork3D("This 3D model is in a format that is not supported.");
            return;
        }

        // A model that was read and cannot be shown: the reason again, without reading it again.
        if (Pages[request.PageNumber - 1].Artwork3DRefusals is { } refusals && refusals.TryGetValue(request.ArtworkIndex, out string? refusal))
        {
            NotifyArtwork3D(refusal);
            return;
        }

        if (Artwork3DAvailability.IsUnavailable)
        {
            NotifyArtwork3D(Artwork3DAvailability.UnavailableMessage);
            return;
        }

        if (ActiveArtwork3D is { } previous)
        {
            CloseArtwork3D(previous, null);
        }

        var artwork = new Artwork3DViewModel(request, _pdfService, CloseArtwork3D);
        ActiveArtwork3D = artwork;
        Pages[request.PageNumber - 1].ActiveArtwork3D = artwork;
        artwork.Load();
    }

    /// <summary>
    /// Returns a 3D artwork to its poster and frees what its view held.
    /// </summary>
    /// <param name="message">Why, when the view did not end by the user's choice.</param>
    private void CloseArtwork3D(Artwork3DViewModel artwork, string? message)
    {
        Debug.ThrowNotOnUiThread();

        if (ReferenceEquals(ActiveArtwork3D, artwork))
        {
            ActiveArtwork3D = null;
        }

        // The page lets go first: that takes the 3D control off the page, so that it no longer draws
        // the scene about to be disposed.
        int pageIndex = artwork.PageNumber - 1;
        if ((uint)pageIndex < (uint)Pages.Count && ReferenceEquals(Pages[pageIndex].ActiveArtwork3D, artwork))
        {
            Pages[pageIndex].ActiveArtwork3D = null;
        }

        if ((uint)pageIndex < (uint)Pages.Count && artwork.ModelRefusal is { } refusal)
        {
            Pages[pageIndex].RefuseArtwork3D(artwork.ArtworkIndex, refusal);
        }

        artwork.Dispose();

        if (!string.IsNullOrEmpty(message))
        {
            NotifyArtwork3D(message);
        }
    }

    private void NotifyArtwork3D(string message)
    {
        App.Messenger.Send(new ShowNotificationMessage(NotificationType.Warning, "3D model", message, NotificationTarget));
    }
}
