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
    /// Converts a zero-based index into the one-based number shown to the user.
    /// <para>
    /// A negative index (nothing selected) has no one-based equivalent and converts to
    /// <see cref="AvaloniaProperty.UnsetValue"/>, so the binding falls back instead of
    /// displaying '0'.
    /// </para>
    /// </summary>
    public sealed class ZeroToOneBasedIndexConverter : IValueConverter
    {
        /// <summary>
        /// Shared instance, to be used as <c>{x:Static converters:ZeroToOneBasedIndexConverter.Instance}</c>.
        /// </summary>
        public static readonly ZeroToOneBasedIndexConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int index && index >= 0)
            {
                return index + 1;
            }

            return AvaloniaProperty.UnsetValue;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int oneBasedIndex && oneBasedIndex > 0)
            {
                return oneBasedIndex - 1;
            }

            return AvaloniaProperty.UnsetValue;
        }
    }
}
