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
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace Caly.Core.Converters;

/// <summary>
/// Positions the page-loading progress ring within the visible area of the page.
/// Inputs for both converters are the page's visible area (<see cref="Rect"/>?, may be
/// <c>null</c> when the whole page is used) and the page size (<see cref="Size"/>).
/// </summary>
internal static class ProgressRingPlacement
{
    internal static Rect GetArea(IList<object?> values)
    {
        Rect? visibleArea = values.Count > 0 && values[0] is Rect r ? r : null;
        Size size = values.Count > 1 && values[1] is Size s ? s : default;
        return visibleArea ?? new Rect(default, size);
    }

    /// <summary>
    /// Diameter of the loading progress ring, scaled to the area.
    /// </summary>
    internal static double GetSize(Rect area)
    {
        double size = 0.10 * Math.Min(area.Width, area.Height);
        return size < 5 ? 5 : size;
    }
}

public sealed class ProgressRingSizeConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        return ProgressRingPlacement.GetSize(ProgressRingPlacement.GetArea(values));
    }
}

/// <summary>
/// Margin that positions the loading progress ring at the center of the area.
/// </summary>
public sealed class ProgressRingMarginConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        Rect area = ProgressRingPlacement.GetArea(values);
        Point center = area.Center;
        double half = ProgressRingPlacement.GetSize(area) / 2.0;
        return new Thickness(center.X - half, center.Y - half, 0, 0);
    }
}
