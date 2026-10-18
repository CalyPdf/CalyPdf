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
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Caly.Core.Services;
using Caly.Pdf.Artwork;
using Caly.Pdf.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UglyToad.PdfPig.Artwork;
using UglyToad.PdfPig.Core;

namespace Caly.Core.Controls;

/// <summary>
/// What hovering 3D artwork says about viewing it.
/// </summary>
internal enum Artwork3DHint : byte
{
    /// <summary>A click shows the model.</summary>
    ClickToView,

    /// <summary>There is no reader for the artwork's format.</summary>
    FormatNotSupported,

    /// <summary>3D views cannot be drawn in this run of the application.</summary>
    GpuUnavailable,

    /// <summary>The model was read and cannot be shown; the reason comes with the hint.</summary>
    Refused
}

// 3D artwork: the "3D" badges and the flyout shown when hovering an artwork.
public sealed partial class PageInteractiveLayerControl
{
    /// <summary>
    /// Size of the "3D" badge drawn on 3D artwork, and its inset from the artwork's top-left corner, in
    /// screen pixels at 100% display scaling. The badge keeps this size at every page zoom.
    /// </summary>
    internal const double Artwork3DBadgeWidth = 30;
    internal const double Artwork3DBadgeHeight = 18;
    internal const double Artwork3DBadgeMargin = 4;

    private const double Artwork3DBadgeCornerRadius = 3;
    private const double Artwork3DBadgeFontSize = 11;
    private const int Artwork3DMaxViewNames = 10;

    private static readonly ImmutableSolidColorBrush Artwork3DBadgeBrush = new(Color.FromArgb(0xCC, 0x1F, 0x29, 0x37));
    // Per control, built when a badge is first drawn: it holds on to a typeface, and those do not
    // outlive the application's font manager.
    private FormattedText? _artwork3DBadgeText;

    private PdfArtwork3D? _shownArtwork3D;

    /// <summary>
    /// Defines the <see cref="ActiveArtwork3DIndex"/> property.
    /// </summary>
    public static readonly StyledProperty<int> ActiveArtwork3DIndexProperty =
        AvaloniaProperty.Register<PageInteractiveLayerControl, int>(nameof(ActiveArtwork3DIndex), -1);

    /// <summary>
    /// Defines the <see cref="Artwork3DRefusals"/> property.
    /// </summary>
    public static readonly StyledProperty<IReadOnlyDictionary<int, string>?> Artwork3DRefusalsProperty =
        AvaloniaProperty.Register<PageInteractiveLayerControl, IReadOnlyDictionary<int, string>?>(nameof(Artwork3DRefusals));

    /// <summary>
    /// Position, in the text layer's 3D artworks, of the one being viewed in place; -1 when none is.
    /// Its view has a toolbar where the badge would be.
    /// </summary>
    public int ActiveArtwork3DIndex
    {
        get => GetValue(ActiveArtwork3DIndexProperty);
        set => SetValue(ActiveArtwork3DIndexProperty, value);
    }

    /// <summary>
    /// Why 3D artworks of the page that were read cannot be shown, by their position in the text
    /// layer's 3D artworks; null when none was refused. Hovering one says its reason.
    /// </summary>
    public IReadOnlyDictionary<int, string>? Artwork3DRefusals
    {
        get => GetValue(Artwork3DRefusalsProperty);
        set => SetValue(Artwork3DRefusalsProperty, value);
    }

    /// <summary>
    /// The rectangle of the "3D" badge for artwork occupying <paramref name="box"/>, in layer coordinates:
    /// at the artwork's top-left corner, with a constant on-screen size whatever the page zoom.
    /// </summary>
    internal static Rect GetArtwork3DBadgeRect(PdfRectangle box, double zoom)
    {
        if (!(zoom > 0) || double.IsInfinity(zoom))
        {
            zoom = 1;
        }

        // The layer's y axis grows downwards: the smallest y is the top edge, whatever the corner order.
        double left = Math.Min(Math.Min(box.TopLeft.X, box.TopRight.X), Math.Min(box.BottomLeft.X, box.BottomRight.X));
        double top = Math.Min(Math.Min(box.TopLeft.Y, box.TopRight.Y), Math.Min(box.BottomLeft.Y, box.BottomRight.Y));

        return new Rect(
            left + Artwork3DBadgeMargin / zoom,
            top + Artwork3DBadgeMargin / zoom,
            Artwork3DBadgeWidth / zoom,
            Artwork3DBadgeHeight / zoom);
    }

    /// <summary>
    /// Whether a click on artwork of this format will show it, and if not, why.
    /// </summary>
    /// <param name="refusal">Why this artwork could not be shown when it was read, if it was.</param>
    internal static Artwork3DHint GetArtwork3DHint(Artwork3DFormat format, string? refusal = null)
    {
        // Whether a given model can be shown is only known once it is read: the click finds out.
        if (!Artwork3DSceneReader.CanRead(format))
        {
            return Artwork3DHint.FormatNotSupported;
        }

        if (!string.IsNullOrEmpty(refusal))
        {
            return Artwork3DHint.Refused;
        }

        return Artwork3DAvailability.IsUnavailable ? Artwork3DHint.GpuUnavailable : Artwork3DHint.ClickToView;
    }

    /// <summary>
    /// Why an artwork of the page could not be shown when it was read: looked up by its position among
    /// the page's artworks, since the artwork object itself is new whenever the text layer is rebuilt.
    /// </summary>
    internal static string? GetArtwork3DRefusal(IReadOnlyDictionary<int, string>? refusals, int artworkIndex)
    {
        return refusals is not null && refusals.TryGetValue(artworkIndex, out string? refusal) ? refusal : null;
    }

    /// <summary>What hovering the artwork at this position among the page's artworks says about viewing it.</summary>
    internal Artwork3DHint GetArtwork3DHint(PdfArtwork3D artwork, int artworkIndex)
    {
        return GetArtwork3DHint(artwork.Format, GetArtwork3DRefusal(Artwork3DRefusals, artworkIndex));
    }

    /// <summary>
    /// The text shown when hovering 3D artwork: its format, the views its author defined, and what a
    /// click will do.
    /// </summary>
    internal static string GetArtwork3DDescription(string formatDisplayName, IReadOnlyList<string> viewNames, Artwork3DHint hint, string? refusal = null)
    {
        var sb = new StringBuilder();
        sb.Append("3D model (").Append(formatDisplayName).AppendLine(")");

        if (viewNames.Count == 0)
        {
            sb.AppendLine("No authored views");
        }
        else
        {
            sb.Append("Views: ");
            int shown = Math.Min(viewNames.Count, Artwork3DMaxViewNames);
            for (int i = 0; i < shown; ++i)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(viewNames[i]);
            }

            if (viewNames.Count > shown)
            {
                sb.Append(" and ").Append(viewNames.Count - shown).Append(" more");
            }

            sb.AppendLine();
        }

        switch (hint)
        {
            case Artwork3DHint.ClickToView:
                sb.Append("Click to view in 3D.");
                break;
            case Artwork3DHint.GpuUnavailable:
                sb.Append(Artwork3DAvailability.UnavailableMessage);
                break;
            case Artwork3DHint.Refused:
                sb.Append(refusal);
                break;
            default:
                sb.Append(formatDisplayName).Append(" models are not supported.");
                break;
        }

        return sb.ToString();
    }

    internal string GetArtwork3DDescription(PdfArtwork3D artwork, int artworkIndex)
    {
        string? refusal = GetArtwork3DRefusal(Artwork3DRefusals, artworkIndex);
        return GetArtwork3DDescription(artwork.FormatDisplayName, artwork.ViewNames, GetArtwork3DHint(artwork.Format, refusal), refusal);
    }

    /// <param name="artworkIndex">The artwork's position in the text layer's 3D artworks.</param>
    internal void ShowArtwork3D(PdfArtwork3D artwork, int artworkIndex)
    {
        if (FlyoutBase.GetAttachedFlyout(this) is not Flyout attachedFlyout)
        {
            return;
        }

        if (ReferenceEquals(_shownArtwork3D, artwork) && attachedFlyout.IsOpen)
        {
            // Pointer moves over the same artwork: nothing to rebuild.
            return;
        }

        _shownArtwork3D = artwork;

        attachedFlyout.FlyoutPresenterClasses.Remove(AnnotationFlyoutClass);
        attachedFlyout.Content = new TextBlock()
        {
            MaxWidth = 280,
            TextWrapping = TextWrapping.Wrap,
            Text = GetArtwork3DDescription(artwork, artworkIndex)
        };

        attachedFlyout.ShowAt(this);
    }

    private void RenderArtwork3DBadges(DrawingContext context, IReadOnlyList<PdfArtwork3D> artworks, Rect visibleArea)
    {
        // The page zoom is a layout transform above this control: draw the badge at the inverse scale
        // so it keeps its size on screen.
        Matrix transform = GetLayoutTransformMatrix();
        double zoom = Math.Sqrt(transform.M11 * transform.M11 + transform.M12 * transform.M12);
        if (!(zoom > 0) || double.IsInfinity(zoom))
        {
            zoom = 1;
        }

        _artwork3DBadgeText ??= new FormattedText("3D", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold), Artwork3DBadgeFontSize, Brushes.White);

        int active = ActiveArtwork3DIndex;
        for (int i = 0; i < artworks.Count; ++i)
        {
            if (i == active)
            {
                // Being viewed: its view has a toolbar at this corner.
                continue;
            }

            PdfArtwork3D artwork = artworks[i];
            Rect badge = GetArtwork3DBadgeRect(artwork.BoundingBox, zoom);
            if (!badge.Intersects(visibleArea))
            {
                continue;
            }

            context.DrawRectangle(Artwork3DBadgeBrush, null, badge, Artwork3DBadgeCornerRadius / zoom, Artwork3DBadgeCornerRadius / zoom);

            using (context.PushTransform(Matrix.CreateScale(1 / zoom, 1 / zoom) * Matrix.CreateTranslation(badge.X, badge.Y)))
            {
                context.DrawText(_artwork3DBadgeText, new Point(
                    (Artwork3DBadgeWidth - _artwork3DBadgeText.Width) / 2,
                    (Artwork3DBadgeHeight - _artwork3DBadgeText.Height) / 2));
            }
        }
    }
}
