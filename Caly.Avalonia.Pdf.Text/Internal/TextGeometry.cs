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

using Avalonia;
using Avalonia.Media;
using UglyToad.PdfPig.Core;

namespace Caly.Avalonia.Pdf.Text;

internal static class TextGeometry
{
    public static StreamGeometry GetGeometry(PdfRectangle rect, bool isFilled = false)
    {
        var sg = new StreamGeometry();
        using (var ctx = sg.Open())
        {
            ctx.BeginFigure(new Point(rect.BottomLeft.X, rect.BottomLeft.Y), isFilled);
            ctx.LineTo(new Point(rect.TopLeft.X, rect.TopLeft.Y));
            ctx.LineTo(new Point(rect.TopRight.X, rect.TopRight.Y));
            ctx.LineTo(new Point(rect.BottomRight.X, rect.BottomRight.Y));
            ctx.EndFigure(true);
        }

        return sg;
    }

    public static bool IsEmpty(this Rect rect) => rect.Height <= float.Epsilon || rect.Width <= float.Epsilon;
}
