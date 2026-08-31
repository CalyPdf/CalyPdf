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

using System.Collections.Generic;
using System.Linq;
using Caly.Core.Services.Interfaces;

namespace Caly.Printing.Unix;

/// <summary>
/// Pure mapping from <see cref="PrintSettings"/> + <see cref="PrinterCapabilities"/>
/// to primitive IPP attribute values. Kept platform-agnostic and SharpIPP-agnostic
/// so that the Unix service can adapt the primitives to its concrete enum types
/// while the mapping logic stays unit-testable.
/// </summary>
public static class IppAttributeMapping
{
    /// <summary>
    /// Mirrors RFC 8011 print-scaling values, expressed as an enum so callers do not
    /// hand-write strings.
    /// </summary>
    public enum IppPrintScaling : byte
    {
        None = 0,
        Fit = 1,
        AutoFit = 2
    }

    /// <summary>
    /// Returns the IPP <c>orientation-requested</c> integer (3=portrait, 4=landscape)
    /// or <c>null</c> when the printer should be left to default
    /// (Auto — printer-side default; Landscape on a non-supporting printer).
    /// </summary>
    public static int? MapOrientation(PrintSettings settings, PrinterCapabilities caps)
    {
        return settings.Orientation switch
        {
            PrintOrientation.Portrait => 3,
            PrintOrientation.Landscape => caps.SupportsLandscape ? 4 : null,
            PrintOrientation.Auto => null,
            _ => null,
        };
    }

    /// <summary>
    /// Returns the IPP <c>print-color-mode</c> string ("monochrome" / "color") or
    /// <c>null</c> when no directive should be sent (printer can't honor mono → use
    /// app-side grayscale; or user wants color and we'd rather inherit printer default).
    /// </summary>
    public static string? MapColorMode(PrintSettings settings, PrinterCapabilities caps)
    {
        if (settings.ColorMode == PrintColorMode.Monochrome)
        {
            return caps.SupportsMonochromeDirective ? "monochrome" : null;
        }
        return null;
    }

    /// <summary>
    /// The N-up values the print dialog offers. Anything the printer supports outside this
    /// set is irrelevant to Caly, and 1-up is always available.
    /// </summary>
    public static readonly IReadOnlyList<int> OfferedNumberUp = [1, 2, 4];

    /// <summary>
    /// Reduces the printer's <c>number-up-supported</c> spans to the values in
    /// <see cref="OfferedNumberUp"/>.
    /// <para>
    /// IPP reports <c>number-up-supported</c> as either a set of integers or a
    /// <c>rangeOfInteger</c>, so each entry is an inclusive <paramref name="supported"/> span —
    /// a plain integer is simply a span of one. A <c>null</c> or empty sequence means the
    /// printer did not report the attribute, in which case we keep the conventional set rather
    /// than assume the printer can only do 1-up. 1 is always included: RFC 8011 requires it and
    /// the dialog would otherwise have no valid selection.
    /// </para>
    /// </summary>
    public static IReadOnlyList<int> MapNumberUpSupported(IEnumerable<(int Lower, int Upper)>? supported)
    {
        if (supported is null)
        {
            return OfferedNumberUp;
        }

        var spans = supported as IReadOnlyCollection<(int Lower, int Upper)> ?? supported.ToArray();
        if (spans.Count == 0)
        {
            return OfferedNumberUp;
        }

        var result = new List<int>(OfferedNumberUp.Count);
        foreach (int candidate in OfferedNumberUp)
        {
            if (candidate == 1 || spans.Any(s => candidate >= s.Lower && candidate <= s.Upper))
            {
                result.Add(candidate);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the IPP <c>number-up</c> value, falling back to 1 if the printer does
    /// not support the requested value.
    /// </summary>
    public static int MapNumberUp(PrintSettings settings, PrinterCapabilities caps)
    {
        return caps.SupportedNumberUp.Contains(settings.PagesPerSheet)
            ? settings.PagesPerSheet
            : 1;
    }

    /// <summary>
    /// Returns the IPP <c>print-scaling</c> value to send for a given fit mode.
    /// CustomScale is rendered app-side, so we send <c>None</c>.
    /// </summary>
    public static IppPrintScaling MapFitMode(PrintSettings settings)
    {
        return settings.FitMode switch
        {
            PrintFitMode.FitToPage => IppPrintScaling.Fit,
            PrintFitMode.ActualSize => IppPrintScaling.None,
            PrintFitMode.ShrinkToFit => IppPrintScaling.AutoFit,
            PrintFitMode.CustomScale => IppPrintScaling.None,
            _ => IppPrintScaling.Fit,
        };
    }

    /// <summary>
    /// Returns true when the bitmap should be converted to grayscale before transport
    /// because the printer cannot be told to print in monochrome.
    /// </summary>
    public static bool NeedsAppSideGrayscale(PrintSettings settings, PrinterCapabilities caps)
    {
        return settings.ColorMode == PrintColorMode.Monochrome
               && !caps.SupportsMonochromeDirective;
    }
}
