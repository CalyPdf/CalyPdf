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

using Caly.Pdf.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Annotations;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Geometry;
using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.Graphics.Core;
using UglyToad.PdfPig.Graphics.Operations;
using UglyToad.PdfPig.Parser;
using UglyToad.PdfPig.PdfFonts;
using UglyToad.PdfPig.Tokenization.Scanner;
using UglyToad.PdfPig.Tokens;
using UglyToad.PdfPig.Util;

namespace Caly.Pdf.TextLayer
{
    public sealed partial class TextLayerStreamProcessor : BaseStreamProcessor<PageTextLayerContent>
    {
        /// <summary>
        /// Stores each letter as it is encountered in the content stream.
        /// </summary>
        private readonly List<PdfLetter> _letters = new();

        private readonly CancellationToken _token;

        private readonly double _pageWidth;
        private readonly double _pageHeight;
        private readonly double _ppiScale;

        private readonly AnnotationProvider _annotationProvider;

        public TextLayerStreamProcessor(int pageNumber,
            IResourceStore resourceStore,
            IPdfTokenScanner pdfScanner,
            IPageContentParser pageContentParser,
            ILookupFilterProvider filterProvider,
            CropBox cropBox,
            UserSpaceUnit userSpaceUnit,
            PageRotationDegrees rotation,
            TransformationMatrix initialMatrix,
            double pageWidth,
            double pageHeight,
            double ppiScale,
            AnnotationProvider annotationProvider,
            ParsingOptions parsingOptions,
            CancellationToken token)
            : base(pageNumber, resourceStore, pdfScanner, pageContentParser, filterProvider, cropBox, userSpaceUnit,
                rotation, initialMatrix, null, parsingOptions)
        {
            _token = token;

            _pageWidth = pageWidth;
            _pageHeight = pageHeight;
            _ppiScale = ppiScale;

            _annotationProvider = annotationProvider;
            _annotations = new Lazy<Annotation[]>(() => _annotationProvider.GetAnnotations().ToArray());

            var gs = GraphicsStack.Pop();
            System.Diagnostics.Debug.Assert(GraphicsStack.Count == 0);

            GraphicsStack.Push(new CurrentGraphicsState()
            {
                CurrentTransformationMatrix = gs.CurrentTransformationMatrix,
                CurrentClippingPath = gs.CurrentClippingPath,
                ColorSpaceContext = NoOpColorSpaceContext.Instance
            });
        }

        public override PageTextLayerContent Process(int pageNumberCurrent,
            IReadOnlyList<IGraphicsStateOperation> operations)
        {
            PageNumber = pageNumberCurrent;
            CloneAllStates();

            ProcessContentStream(operations);

            DrawAnnotations();

            return new PageTextLayerContent()
            {
                Letters = _letters,
                Annotations = _pdfAnnotations
            };
        }

        protected override void ProcessOperations(IReadOnlyList<IGraphicsStateOperation> operations)
        {
            if (!_token.CanBeCanceled)
            {
                base.ProcessOperations(operations);
                return;
            }

            for (var i = 0; i < operations.Count; ++i)
            {
                if (i % 100 == 0)
                {
                    _token.ThrowIfCancellationRequested();
                }

                operations[i].Run(this);
            }
        }

        private static PdfRectangle InverseYAxis(PdfRectangle rectangle, double height)
        {
            var topLeft = new PdfPoint(rectangle.TopLeft.X, height - rectangle.TopLeft.Y);
            var topRight = new PdfPoint(rectangle.TopRight.X, height - rectangle.TopRight.Y);
            var bottomLeft = new PdfPoint(rectangle.BottomLeft.X, height - rectangle.BottomLeft.Y);
            var bottomRight = new PdfPoint(rectangle.BottomRight.X, height - rectangle.BottomRight.Y);
            return new PdfRectangle(topLeft, topRight, bottomLeft, bottomRight);
        }

        public override void RenderGlyph(IFont font,
            CurrentGraphicsState currentState,
            double fontSize,
            double pointSize,
            int code,
            string unicode,
            long currentOffset,
            in TransformationMatrix renderingMatrix,
            in TransformationMatrix textMatrix,
            in TransformationMatrix transformationMatrix,
            CharacterBoundingBox characterBoundingBox)
        {
            if (IsOptionalContentHidden)
            {
                // Hidden optional content (layer) is not drawn, so its text cannot be selected or found.
                return;
            }

            unicode = ApplyActualText(unicode);

            if (currentOffset > 0 && _letters.Count > 0 && Diacritics.IsInCombiningDiacriticRange(unicode))
            {
                // GHOSTSCRIPT-698363-0.pdf
                var attachTo = _letters[^1];

                if (attachTo.TextSequence == TextSequence
                    && Diacritics.TryCombineDiacriticWithPreviousLetter(unicode, attachTo.Value, out var newLetter))
                {
                    // TODO: union of bounding boxes.
                    _letters[^1] = new PdfLetter(newLetter, attachTo.BoundingBox, attachTo.PointSize, attachTo.TextSequence);
                    return;
                }
            }

            // If we did not create a letter for a combined diacritic, create one here.
            /* 9.2.2 Basics of showing text
             * A font defines the glyphs at one standard size. This standard is arranged so that the nominal height of tightly
             * spaced lines of text is 1 unit. In the default user coordinate system, this means the standard glyph size is 1
             * unit in user space, or 1 ⁄ 72 inch. Starting with PDF 1.6, the size of this unit may be specified as greater than
             * 1 ⁄ 72 inch by means of the UserUnit entry of the page dictionary.
             */

            PdfRectangle boundingBox;

            if (font is IType3Font)
            {
                // NB: Type 3 fonts are a mess
                double width = characterBoundingBox.GlyphBounds.BottomRight.X -
                               characterBoundingBox.GlyphBounds.BottomLeft.X;
                var baselineBounds = InverseYAxis(PerformantRectangleTransformer
                        .Transform(renderingMatrix,
                            textMatrix,
                            transformationMatrix,
                            new PdfRectangle(0, 0, width, UserSpaceUnit.PointMultiples)),
                    _pageHeight);

                boundingBox = InverseYAxis(PerformantRectangleTransformer
                        .Transform(renderingMatrix,
                            textMatrix,
                            transformationMatrix,
                            characterBoundingBox.GlyphBounds),
                    _pageHeight)
                    .UnMirror(); // We make sure the bbox is not mirrored, happens a lot for type 3 fonts

                boundingBox = new PdfRectangle(boundingBox.TopLeft, boundingBox.TopRight,
                    baselineBounds.BottomLeft, baselineBounds.BottomRight);
            }
            else
            {
                var bbox = new PdfRectangle(0, 0, characterBoundingBox.Width,
                    Math.Max(font.GetAscent(), UserSpaceUnit.PointMultiples));

                boundingBox = InverseYAxis(PerformantRectangleTransformer
                        .Transform(renderingMatrix,
                            textMatrix,
                            transformationMatrix,
                            bbox),
                    _pageHeight);
            }

            var letter = new PdfLetter(unicode,
                boundingBox,
                (float)pointSize,
                TextSequence);

            _letters.Add(letter);
        }

        #region BaseStreamProcessor overrides
        public override void BeginInlineImage()
        {
            // No op
        }

        public override void SetInlineImageProperties(IReadOnlyDictionary<NameToken, IToken> properties)
        {
            // No op
        }

        public override void EndInlineImage(Memory<byte> bytes)
        {
            // No op
        }

        public override void SetNamedGraphicsState(NameToken stateName)
        {
            var state = ResourceStore.GetExtendedGraphicsStateDictionary(stateName);

            if (state is null)
            {
                return;
            }

            // Only do text related things

            if (state.TryGet(NameToken.Font, PdfScanner, out ArrayToken? fontArray) && fontArray.Length == 2
                && fontArray.Data[0] is IndirectReferenceToken fontReference &&
                fontArray.Data[1] is NumericToken sizeToken)
            {
                var currentGraphicsState = GetCurrentState();

                currentGraphicsState.FontState.FromExtendedGraphicsState = true;
                currentGraphicsState.FontState.FontSize = sizeToken.Data;
                ActiveExtendedGraphicsStateFont = ResourceStore.GetFontDirectly(fontReference);
            }
        }

        /// <inheritdoc/>
        public override void SetFlatnessTolerance(double tolerance)
        {
            // No op
        }

        /// <inheritdoc/>
        public override void SetLineCap(LineCapStyle cap)
        {
            // No op
        }

        /// <inheritdoc/>
        public override void SetLineDashPattern(LineDashPattern pattern)
        {
            // No op
        }

        /// <inheritdoc/>
        public override void SetLineJoin(LineJoinStyle join)
        {
            // No op
        }

        /// <inheritdoc/>
        public override void SetLineWidth(double width)
        {
            // No op
        }

        /// <inheritdoc/>
        public override void SetMiterLimit(double limit)
        {
            // No op
        }

        #endregion
        protected override void RenderXObjectImage(XObjectContentRecord xObjectContentRecord)
        {
            // No op
        }

        public override void BeginSubpath()
        {
            // No op
        }

        public override PdfPoint? CloseSubpath()
        {
            // No op
            return null;
        }

        public override void StrokePath(bool close)
        {
            // No op
        }

        public override void FillPath(FillingRule fillingRule, bool close)
        {
            // No op
        }

        public override void FillStrokePath(FillingRule fillingRule, bool close)
        {
            // No op
        }

        public override void MoveTo(double x, double y)
        {
            // No op
        }

        public override void BezierCurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
        {
            // No op
        }

        public override void LineTo(double x, double y)
        {
            // No op
        }

        public override void Rectangle(double x, double y, double width, double height)
        {
            // No op
        }

        public override void EndPath()
        {
            // No op
        }

        public override void ClosePath()
        {
            // No op
        }

        public override void ModifyClippingIntersect(FillingRule clippingRule)
        {
            // No op
        }

        protected override void ClipToRectangle(PdfRectangle rectangle, FillingRule clippingRule)
        {
            // No op
        }

        public override void PaintShading(NameToken shadingName)
        {
            // No op
        }

        protected override void RenderInlineImage(InlineImage inlineImage)
        {
            // No op
        }

        public override void BezierCurveTo(double x2, double y2, double x3, double y3)
        {
            // No op
        }
    }
}
