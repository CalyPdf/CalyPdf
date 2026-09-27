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

using Avalonia;

namespace Caly.Core.Models;

/// <summary>
/// Where the reader is looking: the point of the document under the viewport centre.
/// </summary>
/// <param name="PageNumber">The page under the viewport centre, or the closest one when the centre
/// falls between pages. Starts at 1.</param>
/// <param name="Position">The viewport centre on the unrotated page, clamped to the page: top-left = 0,
/// y increasing downward, unscaled (independent of the zoom level). Same space as
/// <c>PageViewModel.VisibleArea</c>.</param>
public readonly record struct PageReadingPoint(int PageNumber, Point Position);
