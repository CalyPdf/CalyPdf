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
using Avalonia;
using Avalonia.Data.Converters;

namespace Caly.Core.Converters
{
    /// <summary>
    /// Converts a <see cref="Controls.PageItemsControl.ZoomLevel"/> value into a bottom
    /// <see cref="Thickness"/> that keeps the on-screen gap between pages constant across zoom levels.
    /// </summary>
    public sealed class ZoomToPageSpacingConverter : IValueConverter
    {
        /// <summary>
        /// Shared instance, to be used as <c>{x:Static converters:ZoomToPageSpacingConverter.Instance}</c>.
        /// </summary>
        public static readonly ZoomToPageSpacingConverter Instance = new();

        /// <summary>
        /// Desired on-screen gap, in device-independent pixels, used when no <c>ConverterParameter</c> is supplied.
        /// </summary>
        private const double DefaultGap = 5.0;

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            double gap = DefaultGap;
            if (parameter is string s && double.TryParse(s, NumberStyles.Float, culture, out double parsedGap))
            {
                gap = parsedGap;
            }

            if (value is double zoom && zoom > 0)
            {
                return new Thickness(0, 0, 0, gap / zoom);
            }

            return new Thickness(0, 0, 0, gap);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
