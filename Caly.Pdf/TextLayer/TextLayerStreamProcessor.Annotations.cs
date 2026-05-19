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

using Caly.Pdf.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Annotations;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Geometry;
using UglyToad.PdfPig.Tokens;

namespace Caly.Pdf.TextLayer
{
    public partial class TextLayerStreamProcessor
    {
        /// <summary>
        /// Raw page annotations.
        /// </summary>
        private readonly Lazy<Annotation[]> _annotations;

        /// <summary>
        /// Processed page annotations.
        /// </summary>
        private readonly List<PdfAnnotation> _pdfAnnotations = new();

        private static bool IsInteractive(Annotation annotation)
        {
            return annotation.Type == AnnotationType.Link;
        }

        private static bool IsNoZoom(Annotation annotation)
        {
            return annotation.Flags.HasFlag(AnnotationFlags.NoZoom);
        }

        private static bool IsNoRotate(Annotation annotation)
        {
            return annotation.Flags.HasFlag(AnnotationFlags.NoRotate);
        }

        private void DrawAnnotations()
        {
            foreach (Annotation annotation in _annotations.Value)
            {
                PdfRectangle rect = annotation.Rectangle;

                if (rect.Width > 0 && rect.Height > 0)
                {
                    var matrix = TransformationMatrix.Identity;
                    if (annotation.AnnotationDictionary.TryGet<ArrayToken>(NameToken.Matrix, PdfScanner, out var matrixToken))
                    {
                        matrix = TransformationMatrix.FromArray(matrixToken.Data.OfType<NumericToken>()
                            .Select(x => x.Double).ToArray());
                    }

                    PdfRectangle bbox = rect;

                    // https://github.com/apache/pdfbox/blob/47867f7eee275e9e54a87222b66ab14a8a3a062a/pdfbox/src/main/java/org/apache/pdfbox/contentstream/PDFStreamEngine.java#L310
                    // transformed appearance box  fixme: may be an arbitrary shape
                    PdfRectangle transformedBox = InverseYAxis(matrix.Transform(bbox)
                        .NormaliseCaly(), _pageHeight);

                    if (transformedBox.Width <= double.MinValue || transformedBox.Height <= double.MinValue)
                    {
                        continue;
                    }

                    bool isInteractive = IsInteractive(annotation);
                    bool hasAction = annotation.Action is not null;
                    bool hasContent = !string.IsNullOrEmpty(annotation.Content);

                    if (!isInteractive && !hasAction && !hasContent)
                    {
                        continue;
                    }

                    _pdfAnnotations.Add(new PdfAnnotation()
                    {
                        PpiScale = _ppiScale,
                        BoundingBox = transformedBox,
                        Action = annotation.Action,
                        Content = annotation.Content,
                        Date = annotation.ModifiedDate,
                        IsInteractive = isInteractive && hasAction
                    });
                }
            }
        }
    }
}
