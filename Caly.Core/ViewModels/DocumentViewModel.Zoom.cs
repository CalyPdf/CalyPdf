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
    public static double MinZoomLevel => 0.08;
    public static double MaxZoomLevel => 64;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ZoomInCommand))]
    [NotifyCanExecuteChangedFor(nameof(ZoomOutCommand))]
    public partial double ZoomLevel { get; set; } = 1;

    /// <summary>
    /// Scroll offset to restore when this document's tab becomes active again. Y is
    /// relative to the top of <see cref="SelectedPageNumber"/>. Stored in unscaled
    /// document coordinates (independent of <see cref="ZoomLevel"/>).
    /// </summary>
    [ObservableProperty]
    public partial Vector ScrollOffset { get; set; }

    /// <summary>
    /// Width of the page viewport in display pixels, pushed up from the view. Independent
    /// of <see cref="ZoomLevel"/> (the zoom scale is applied to the content inside the
    /// viewport). Used by <see cref="FitToPage"/> to compute the zoom that fits a page width.
    /// </summary>
    [ObservableProperty]
    public partial double ViewportWidth { get; set; }

    [RelayCommand(CanExecute = nameof(CanZoomIn))]
    private void ZoomIn()
    {
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

    /// <summary>
    /// Adjusts <see cref="ZoomLevel"/> so the active (selected) page's width fills the
    /// viewport width.
    /// </summary>
    [RelayCommand]
    private void FitToPage()
    {
        // TODO - This is actually "Fit to Width". We also need:
        // "Fit to Page": fit to width or height so that full page is visible on screen
        // "Fit to Content": Fit to relevant part of the page that actually contains sthing (us SKPicture CullRect).
        // These different "fit" options should be made available through a drop-down button, that can be clicked,
        // but only display one choice at a time (each should have its own shortcut though)
        // NB:
        // - When fitting to width, the page x location should stay the same (i.e. if top of the page is visible,
        //      it should stay visible)
        // - When fitting to content, the (x,y) top corner zoomed in should be the top corner of the content (i.e.
        //      page location should be adjusted after the zoom). The ScrollOffset property is one-way, only
        //      update from view.

        int index = SelectedPageIndex;
        if (index < 0 || index >= Pages.Count)
        {
            return;
        }

        // 100.5% of the width to avoid showing horizontal scrollbar
        double pageWidth = Pages[index].DisplayWidth * 1.005;
        double viewportWidth = ViewportWidth;
        if (pageWidth <= 0 || viewportWidth <= 0)
        {
            return;
        }

        ZoomLevel = Math.Clamp(viewportWidth / pageWidth, MinZoomLevel, MaxZoomLevel);
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