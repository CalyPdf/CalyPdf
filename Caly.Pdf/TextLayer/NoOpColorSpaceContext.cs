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

using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.Tokens;

namespace Caly.Pdf.TextLayer
{
    internal sealed class NoOpColorSpaceContext : IColorSpaceContext
    {
        public static readonly NoOpColorSpaceContext Instance = new();

        public ColorSpaceDetails CurrentStrokingColorSpace => DeviceGrayColorSpaceDetails.Instance;

        public ColorSpaceDetails CurrentNonStrokingColorSpace => DeviceGrayColorSpaceDetails.Instance;

        public void SetStrokingColorspace(NameToken colorspace, DictionaryToken? dictionary = null)
        {
            // No op
        }

        public void SetNonStrokingColorspace(NameToken colorspace, DictionaryToken? dictionary = null)
        {
            // No op
        }

        public void SetStrokingColor(double[] operands, NameToken? patternName = null)
        {
            // No op
        }

        public void SetStrokingColor(IReadOnlyList<double> operands, NameToken? patternName = null)
        {
            // No op
        }

        public void SetStrokingColorGray(double gray)
        {
            // No op
        }

        public void SetStrokingColorRgb(double r, double g, double b)
        {
            // No op
        }

        public void SetStrokingColorCmyk(double c, double m, double y, double k)
        {
            // No op
        }

        public void SetNonStrokingColor(double[] operands, NameToken? patternName = null)
        {
            // No op
        }

        public void SetNonStrokingColor(IReadOnlyList<double> operands, NameToken? patternName = null)
        {
            // No op
        }
        
        public void SetNonStrokingColorGray(double gray)
        {
            // No op
        }

        public void SetNonStrokingColorRgb(double r, double g, double b)
        {
            // No op
        }

        public void SetNonStrokingColorCmyk(double c, double m, double y, double k)
        {
            // No op
        }

        public IColorSpaceContext DeepClone()
        {
            return Instance;
        }
    }
}
