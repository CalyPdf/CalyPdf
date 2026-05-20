// Copyright (c) 2025 BobLd
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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Caly.Core.Services.Interfaces;

/// <summary>
/// Carries per-page information for a print job.
/// </summary>
public readonly record struct PrintPageInfo(int PageNumber, int Rotation = 0);

/// <summary>
/// Represents a printer.
/// </summary>
public sealed record PrinterInfo(string Name, Uri? IppUri)
{
    public override string ToString() => Name;
}

public enum PrintOrientation : byte
{
    Auto = 0,
    Portrait = 1,
    Landscape = 2,
}

public enum PrintFitMode : byte
{
    FitToPage = 0,
    ActualSize = 1,
    ShrinkToFit = 2,
    CustomScale = 3
}

public enum PrintColorMode : byte
{
    Color = 0,
    Monochrome = 1
}

/// <summary>
/// Per-job print settings selected in the dialog.
/// </summary>
public sealed record PrintSettings(
    PrintOrientation Orientation = PrintOrientation.Auto,
    PrintFitMode FitMode = PrintFitMode.FitToPage,
    int CustomScalePercent = 100,
    int PagesPerSheet = 1,
    PrintColorMode ColorMode = PrintColorMode.Color);

/// <summary>
/// Subset of printer capabilities relevant to the print dialog.
/// </summary>
public sealed record PrinterCapabilities(
    bool SupportsLandscape,
    bool IsColorDevice,
    bool SupportsMonochromeDirective,
    IReadOnlyList<int> SupportedNumberUp);

public interface IPrintService
{
    /// <summary>
    /// Get all available printers.
    /// </summary>
    Task<IReadOnlyList<PrinterInfo>> GetAvailablePrintersAsync(CancellationToken token);

    /// <summary>
    /// Get the printer capability subset used to drive the print dialog
    /// (orientation, color, supported N-up values).
    /// </summary>
    Task<PrinterCapabilities> GetPrinterCapabilitiesAsync(
        PrinterInfo printer,
        CancellationToken token);

    /// <summary>
    /// Print the document.
    /// </summary>
    /// <param name="printer">The printer to which the document will be sent. Cannot be null.</param>
    /// <param name="documentService">The PDF document service used to access and render the document content. Cannot be null.</param>
    /// <param name="pages">A read-only list of page information specifying which pages to print and their print settings. Cannot be null or empty.</param>
    /// <param name="settings">Print settings.</param>
    /// <param name="progress">Optional progress sink; receives the count of pages processed so far after each page completes.</param>
    /// <param name="token">A cancellation token that can be used to cancel the print operation.</param>
    /// <returns>A task that represents the asynchronous print operation.</returns>
    Task PrintDocumentAsync(
        PrinterInfo printer,
        IPdfDocumentService documentService,
        IReadOnlyList<PrintPageInfo> pages,
        PrintSettings settings,
        IProgress<int>? progress,
        CancellationToken token);
}
