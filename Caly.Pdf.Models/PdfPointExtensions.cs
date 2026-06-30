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

using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Geometry;

namespace Caly.Pdf
{
    public static class PdfPointExtensions
    {
        // https://stackoverflow.com/questions/54009832/scala-orthogonal-projection-of-a-point-onto-a-line
        /* Projects point `p` on line going through two points `line1` and `line2`. */
        public static PdfPoint? ProjectPointOnLine(in PdfPoint p, in PdfPoint line1, in PdfPoint line2, out double s)
        {
            PdfPoint v = p.Subtract(line1);
            PdfPoint d = line2.Subtract(line1);

            double den = d.X * d.X + d.Y * d.Y;

            if (Math.Abs(den) <= double.Epsilon)
            {
                s = 0;
                return null;
            }
            s = v.DotProduct(d) / den;

            return line1.Add(new PdfPoint(d.X * s, d.Y * s));
        }

        public static double ProjectPointOnLineM(in PdfPoint p, in PdfPoint line1, in PdfPoint line2)
        {
            PdfPoint v = p.Subtract(line1);
            PdfPoint d = line2.Subtract(line1);

            double den = d.X * d.X + d.Y * d.Y;

            if (Math.Abs(den) <= double.Epsilon)
            {
                return 0;
            }

            return v.DotProduct(d) / den;
        }
    }
}
