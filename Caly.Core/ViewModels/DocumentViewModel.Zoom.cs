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
using Avalonia;
using Caly.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Caly.Core.ViewModels;

public partial class DocumentViewModel
{
    private static readonly double[] ZoomLevelsDiscrete =
    [
        0.08, 0.125, 0.25, 0.33, 0.5, 0.67, 0.75, 1,
        1.25, 1.5, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64
    ];

    /*
     * See PDF Reference 1.7 - C.2 Architectural limits
     * The magnification factor of a view should be constrained to be between approximately 8 percent and 6400 percent.
     */
    public double MinZoomLevel => 0.08;
    public double MaxZoomLevel => 64;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ZoomInCommand))]
    [NotifyCanExecuteChangedFor(nameof(ZoomOutCommand))]
    private double _zoomLevel = 1;

    /// <summary>
    /// Scroll offset to restore when this document's tab becomes active again. Y is
    /// relative to the top of <see cref="SelectedPageNumber"/>. Stored in unscaled
    /// document coordinates (independent of <see cref="ZoomLevel"/>).
    /// </summary>
    [ObservableProperty] private Vector _scrollOffset;

    /// <summary>
    /// How the zoom is chosen. The view applies the fit modes and keeps them applied as the viewer
    /// changes size; a zoom picked by hand switches back to <see cref="ZoomMode.Manual"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFitPageMode), nameof(IsFitWidthMode), nameof(IsFitContentMode),
        nameof(IsFitContentWidthMode))]
    public partial ZoomMode ZoomMode { get; set; }

    public bool IsFitPageMode => ZoomMode == ZoomMode.FitPage;

    public bool IsFitWidthMode => ZoomMode == ZoomMode.FitWidth;

    public bool IsFitContentMode => ZoomMode == ZoomMode.FitContent;

    public bool IsFitContentWidthMode => ZoomMode == ZoomMode.FitContentWidth;

    [RelayCommand]
    private void SetZoomMode(ZoomMode mode)
    {
        if (mode != ZoomMode.Manual && mode == ZoomMode)
        {
            // The view fits on every change into a fit mode, so passing through Manual re-applies
            // the active one: picking it again snaps the page back into view after scrolling.
            ZoomMode = ZoomMode.Manual;
        }

        ZoomMode = mode;
    }

    [RelayCommand]
    private void ActualSize()
    {
        ZoomMode = ZoomMode.Manual;
        ZoomLevel = 1;
    }

    [RelayCommand(CanExecute = nameof(CanZoomIn))]
    private void ZoomIn()
    {
        ZoomMode = ZoomMode.Manual;

        var index = Array.BinarySearch(ZoomLevelsDiscrete, ZoomLevel);
        if (index < -1)
        {
            ZoomLevel = Math.Min(MaxZoomLevel, ZoomLevelsDiscrete[~index]);
        }
        else
        {
            if (index >= ZoomLevelsDiscrete.Length - 1)
            {
                return;
            }

            ZoomLevel = Math.Min(MaxZoomLevel, ZoomLevelsDiscrete[index + 1]);
        }
    }

    private bool CanZoomIn()
    {
        return ZoomLevel < MaxZoomLevel;
    }

    [RelayCommand(CanExecute = nameof(CanZoomOut))]
    private void ZoomOut()
    {
        ZoomMode = ZoomMode.Manual;

        var index = Array.BinarySearch(ZoomLevelsDiscrete, ZoomLevel);
        if (index < -1)
        {
            ZoomLevel = Math.Max(MinZoomLevel, ZoomLevelsDiscrete[~index - 1]);
        }
        else
        {
            if (index == 0)
            {
                return;
            }

            ZoomLevel = Math.Max(MinZoomLevel, ZoomLevelsDiscrete[index - 1]);
        }
    }

    private bool CanZoomOut()
    {
        return ZoomLevel > MinZoomLevel;
    }

    [RelayCommand]
    private void RotateAllPagesClockwise()
    {
        foreach (PageViewModel page in Pages)
        {
            page.RotateClockwise();
        }
    }

    [RelayCommand]
    private void RotateAllPagesCounterclockwise()
    {
        foreach (PageViewModel page in Pages)
        {
            page.RotateCounterclockwise();
        }
    }
}