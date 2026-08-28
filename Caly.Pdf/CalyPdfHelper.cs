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

using UglyToad.PdfPig;
using UglyToad.PdfPig.Tokens;

namespace Caly.Pdf
{
    public static class CalyPdfHelper
    {
        private static readonly NameToken CollectionNameToken = NameToken.Create("Collection");
        
        private const int NUMBER_OFFSET = sizeof(ushort) * 8;
        private static readonly long MAX_OBJECT_NUMBER = (long)(Math.Pow(2, sizeof(long) * 8 - NUMBER_OFFSET) - 1) / 2;

        public static readonly UglyToad.PdfPig.Core.IndirectReference FakePpiReference = new(-MAX_OBJECT_NUMBER, ushort.MaxValue);

        public static bool IsPunctuation(this ReadOnlySpan<char> text)
        {
            for (int i = 0; i < text.Length; ++i)
            {
                if (!char.IsPunctuation(text[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public static bool IsSeparator(this ReadOnlySpan<char> text)
        {
            for (int i = 0; i < text.Length; ++i)
            {
                if (!char.IsSeparator(text[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Is the pdf document a portfolio.
        /// </summary>
        /// <param name="document"></param>
        /// <returns></returns>
        public static bool IsPortfolio(this PdfDocument document)
        {
            return document.Structure.Catalog
                .CatalogDictionary
                .ContainsKey(CollectionNameToken);
        }
    }
}
