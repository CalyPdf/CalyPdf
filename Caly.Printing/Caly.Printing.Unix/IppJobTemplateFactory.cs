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

using Caly.Core.Services.Interfaces;
using SharpIpp.Protocol.Models;
using IppPrintColorMode = SharpIpp.Protocol.Models.PrintColorMode;

namespace Caly.Printing.Unix;

/// <summary>
/// Adapts the SharpIPP-agnostic primitives produced by <see cref="IppAttributeMapping"/>
/// to the concrete SharpIppNext <see cref="JobTemplateAttributes"/> sent with Create-Job.
/// <para>
/// Split out of <see cref="UnixPrintService"/> so the SharpIppNext-typed conversions —
/// in particular the smart-enum casts, which throw on <c>null</c> — stay unit-testable
/// without a live IPP endpoint.
/// </para>
/// </summary>
public static class IppJobTemplateFactory
{
    /// <summary>
    /// Builds the job template for a Create-Job request.
    /// </summary>
    public static JobTemplateAttributes Build(PrintSettings settings, PrinterCapabilities caps)
    {
        // Caly.Core.Services.Interfaces.PrintColorMode -> "monochrome" / null.
        string? colorMode = IppAttributeMapping.MapColorMode(settings, caps);

        return new JobTemplateAttributes
        {
            // Orientation is a plain enum, so the int? -> Orientation? cast is lifted and
            // maps null to null.
            OrientationRequested = (Orientation?)IppAttributeMapping.MapOrientation(settings, caps),

            // PrintColorMode is a struct "smart enum" whose implicit string operator throws
            // ArgumentNullException on null (SharpIppNext 4.2.4). The string -> PrintColorMode?
            // conversion is NOT lifted — the source is a reference type — so the operator would
            // be invoked with null. Guard before casting.
            PrintColorMode = colorMode is null ? null : (IppPrintColorMode?)colorMode,

            NumberUp = IppAttributeMapping.MapNumberUp(settings, caps),
            PrintScaling = MapPrintScaling(IppAttributeMapping.MapFitMode(settings)),
        };
    }

    /// <summary>
    /// Maps the neutral scaling enum onto SharpIppNext's <see cref="PrintScaling"/> smart enum.
    /// </summary>
    public static PrintScaling MapPrintScaling(IppAttributeMapping.IppPrintScaling scaling)
    {
        return scaling switch
        {
            IppAttributeMapping.IppPrintScaling.Fit => PrintScaling.Fit,
            IppAttributeMapping.IppPrintScaling.None => PrintScaling.None,
            IppAttributeMapping.IppPrintScaling.AutoFit => PrintScaling.AutoFit,
            _ => PrintScaling.Fit,
        };
    }
}
