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
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.VisualTree;
using Caly.Core.Controls.Artwork3D;
using Caly.Core.ViewModels;

namespace Caly.Core.Controls;

[TemplatePart("PART_Artwork3DHost", typeof(Panel))]
public sealed partial class PageItem
{
    /// <summary>
    /// Defines the <see cref="ZoomLevel"/> property.
    /// </summary>
    public static readonly StyledProperty<double> ZoomLevelProperty =
        AvaloniaProperty.Register<PageItem, double>(nameof(ZoomLevel), 1.0);

    /// <summary>
    /// Defines the <see cref="ActiveArtwork3D"/> property.
    /// </summary>
    public static readonly StyledProperty<Artwork3DViewModel?> ActiveArtwork3DProperty =
        AvaloniaProperty.Register<PageItem, Artwork3DViewModel?>(nameof(ActiveArtwork3D));

    /// <summary>
    /// The zoom the page is shown at. Only what is laid out against the screen rather than the page
    /// needs it (the 3D view).
    /// </summary>
    public double ZoomLevel
    {
        get => GetValue(ZoomLevelProperty);
        set => SetValue(ZoomLevelProperty, value);
    }

    /// <summary>
    /// The 3D artwork of this page that is being viewed in place, as the data context of its view;
    /// <c>null</c> when there is none, which is the case of nearly every page.
    /// </summary>
    public Artwork3DViewModel? ActiveArtwork3D
    {
        get => GetValue(ActiveArtwork3DProperty);
        set => SetValue(ActiveArtwork3DProperty, value);
    }

    /// <summary>
    /// Host for the 3D view, and the view currently inside it. Like the skeleton, the view is built on
    /// demand and dropped when this container leaves the visual tree: it holds an OpenGL surface.
    /// </summary>
    private Panel? _artwork3DHost;
    private Artwork3DView? _artwork3DView;

    private void ApplyArtwork3DTemplate(INameScope nameScope)
    {
        DropArtwork3DView();
        _artwork3DHost = nameScope.Find<Panel>("PART_Artwork3DHost");
        SyncArtwork3DView();
    }

    /// <summary>Gives the keyboard focus, and so the wheel, to the model of this page's 3D view, if it has one.</summary>
    internal void FocusArtwork3DView() => _artwork3DView?.FocusModel();

    private void DropArtwork3DView()
    {
        if (_artwork3DView is null)
        {
            return;
        }

        _artwork3DHost?.Children.Remove(_artwork3DView);

        // Clearing the bindings unsubscribes the view from this container, which outlives it.
        _artwork3DView.ClearValue(Artwork3DView.VisibleAreaProperty);
        _artwork3DView.ClearValue(Artwork3DView.ZoomLevelProperty);
        _artwork3DView.DataContext = null;
        _artwork3DView = null;
    }

    /// <summary>
    /// Shows the 3D view while this page has an artwork being viewed and is on screen, and takes it
    /// down otherwise.
    /// </summary>
    private void SyncArtwork3DView()
    {
        Artwork3DViewModel? artwork = ActiveArtwork3D;
        if (_artwork3DHost is null || artwork is null || !this.IsAttachedToVisualTree())
        {
            DropArtwork3DView();
            return;
        }

        if (_artwork3DView is not null && ReferenceEquals(_artwork3DView.DataContext, artwork))
        {
            return;
        }

        DropArtwork3DView();
        _artwork3DView = new Artwork3DView { DataContext = artwork };
        _artwork3DView.Bind(Artwork3DView.VisibleAreaProperty, this.GetObservable(VisibleAreaProperty));
        _artwork3DView.Bind(Artwork3DView.ZoomLevelProperty, this.GetObservable(ZoomLevelProperty));
        _artwork3DHost.Children.Add(_artwork3DView);
    }
}
