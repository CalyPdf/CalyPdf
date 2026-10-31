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
using System.Globalization;
using Avalonia.Data.Converters;
using Caly.Core.Models;

namespace Caly.Core.Converters;

/// <summary>
/// The name of a <see cref="ZoomMode"/> as the Settings "Default zoom" box shows it.
/// <see cref="ZoomMode.Manual"/> is how a document opens when no fit is chosen: at 100%.
/// </summary>
public sealed class ZoomModeDisplayNameConverter : IValueConverter
{
    /// <summary>
    /// Shared instance, to be used as <c>{x:Static converters:ZoomModeDisplayNameConverter.Instance}</c>.
    /// </summary>
    public static readonly ZoomModeDisplayNameConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            ZoomMode.Manual => "Actual size (100%)",
            ZoomMode.FitPage => "Fit page",
            ZoomMode.FitWidth => "Fit width",
            ZoomMode.FitContent => "Fit content",
            ZoomMode.FitContentWidth => "Fit content width",
            _ => value?.ToString()
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
