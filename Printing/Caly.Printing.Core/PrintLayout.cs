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

using System;
using System.Collections.Generic;
using Avalonia;
using Caly.Core.Services.Interfaces;

namespace Caly.Printing.Core;

/// <summary>
/// Pure layout math used by both Windows and Unix print services.
/// </summary>
public static class PrintLayout
{
    /// <summary>
    /// Returns true if the page should be rotated so it prints in landscape rather
    /// than portrait when <c>PrintOrientation.Auto</c> is selected. A page is "wide"
    /// if its width strictly exceeds its height.
    /// </summary>
    public static bool ShouldRotateForAutoOrientation(float pdfWidth, float pdfHeight)
    {
        return pdfWidth > pdfHeight;
    }

    /// <summary>
    /// Computes the cells (regions of the printable area) for an N-up layout.
    /// Reading order: left-to-right, top-to-bottom.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="pagesPerSheet"/> is not 1, 2, or 4.
    /// </exception>
    public static IReadOnlyList<PixelRect> ComputeCells(int printerW, int printerH, int pagesPerSheet)
    {
        switch (pagesPerSheet)
        {
            case 1:
            {
                return [new PixelRect(0, 0, printerW, printerH)];
            }

            case 2:
            {
                // Split along the paper's longer edge.
                if (printerH >= printerW)
                {
                    int halfH = printerH / 2;
                    return
                    [
                        new PixelRect(0, 0, printerW, halfH),
                        new PixelRect(0, halfH, printerW, printerH - halfH)
                    ];
                }

                int halfW = printerW / 2;
                return
                [
                    new PixelRect(0, 0, halfW, printerH),
                    new PixelRect(halfW, 0, printerW - halfW, printerH)
                ];
            }

            case 4:
            {
                int halfW = printerW / 2;
                int halfH = printerH / 2;
                return
                [
                    new PixelRect(0, 0, halfW, halfH),
                    new PixelRect(halfW, 0, printerW - halfW, halfH),
                    new PixelRect(0, halfH, halfW, printerH - halfH),
                    new PixelRect(halfW, halfH, printerW - halfW, printerH - halfH)
                ];
            }

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(pagesPerSheet),
                    pagesPerSheet,
                    "Only 1, 2, or 4 pages per sheet are supported.");
        }
    }

    /// <summary>
    /// Returns the printer-pixel rectangle to draw a single PDF page into, centered in
    /// <paramref name="cell"/>, sized according to <paramref name="fitMode"/>.
    /// </summary>
    public static PixelRect ComputeDestRect(
        PixelRect cell,
        int bitmapW,
        int bitmapH,
        float pdfPointsW,
        float pdfPointsH,
        int dpiX,
        int dpiY,
        PrintFitMode fitMode,
        int customScalePercent)
    {
        int targetW;
        int targetH;

        switch (fitMode)
        {
            case PrintFitMode.FitToPage:
                (targetW, targetH) = FitPreservingAspect(cell.Width, cell.Height, bitmapW, bitmapH);
                break;

            case PrintFitMode.ActualSize:
                targetW = (int)(pdfPointsW * dpiX / 72f);
                targetH = (int)(pdfPointsH * dpiY / 72f);
                break;

            case PrintFitMode.ShrinkToFit:
            {
                int actualW = (int)(pdfPointsW * dpiX / 72f);
                int actualH = (int)(pdfPointsH * dpiY / 72f);
                if (actualW <= cell.Width && actualH <= cell.Height)
                {
                    targetW = actualW;
                    targetH = actualH;
                }
                else
                {
                    (targetW, targetH) = FitPreservingAspect(cell.Width, cell.Height, actualW, actualH);
                }
                break;
            }

            case PrintFitMode.CustomScale:
                int scale = Math.Clamp(customScalePercent, 10, 400);
                targetW = (int)(pdfPointsW * dpiX / 72f * scale / 100f);
                targetH = (int)(pdfPointsH * dpiY / 72f * scale / 100f);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(fitMode), fitMode, null);
        }

        int x = cell.X + (cell.Width - targetW) / 2;
        int y = cell.Y + (cell.Height - targetH) / 2;
        return new PixelRect(x, y, targetW, targetH);
    }

    private static (int W, int H) FitPreservingAspect(int boxW, int boxH, int srcW, int srcH)
    {
        if (srcW <= 0 || srcH <= 0)
        {
            return (boxW, boxH);
        }

        float boxAspect = (float)boxW / boxH;
        float srcAspect = (float)srcW / srcH;
        if (srcAspect >= boxAspect)
        {
            return (boxW, (int)(boxW / srcAspect));
        }
        return ((int)(boxH * srcAspect), boxH);
    }
}
