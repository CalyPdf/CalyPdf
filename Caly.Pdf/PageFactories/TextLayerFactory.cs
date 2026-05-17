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
using Caly.Pdf.TextLayer;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Annotations;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Geometry;
using UglyToad.PdfPig.Graphics.Operations;
using UglyToad.PdfPig.Outline.Destinations;
using UglyToad.PdfPig.Parser;
using UglyToad.PdfPig.Tokenization.Scanner;
using UglyToad.PdfPig.Tokens;

namespace Caly.Pdf.PageFactories
{
    public sealed class TextLayerFactory : BasePageFactory<PageTextLayerContent>
    {
        private readonly double _ppiScale = 1;
        private readonly TransformationMatrix _scale;

        private static readonly AsyncLocal<CancellationToken> _currentToken = new();

        internal static CancellationToken CurrentToken
        {
            get => _currentToken.Value;
            set => _currentToken.Value = value;
        }

        public TextLayerFactory(IPdfTokenScanner pdfScanner,
            IResourceStore resourceStore,
            ILookupFilterProvider filterProvider,
            IPageContentParser _,
            ParsingOptions parsingOptions)
            : base(pdfScanner,
                resourceStore,
                filterProvider,
                new TextOnlyPageContentParser(TextOnlyGraphicsStateOperationFactory.Instance,
                    new StackDepthGuard(parsingOptions.MaxStackDepth),
                    parsingOptions.UseLenientParsing),
                parsingOptions)
        {
            // We store the PPI scale as an indirect object so that it can be accessed in the TextLayerFactory.
            // This is very hacky but PdfPig does not provide a better way to pass such information
            // to the PageFactory for the moment.
            // TODO - to remove.
            if (pdfScanner.Get(CalyPdfHelper.FakePpiReference)?.Data is NumericToken ppi)
            {
                _ppiScale = ppi.Double;
            }
            _scale = TransformationMatrix.GetScaleMatrix(_ppiScale, _ppiScale);
        }

        protected override PageTextLayerContent ProcessPage(int pageNumber, DictionaryToken dictionary,
            NamedDestinations namedDestinations, MediaBox mediaBox,
            CropBox cropBox, UserSpaceUnit userSpaceUnit, PageRotationDegrees rotation,
            TransformationMatrix initialMatrix,
            IReadOnlyList<IGraphicsStateOperation> operations)
        {
            // Special case where cropbox is outside mediabox: use cropbox instead of intersection
            var effectiveCropBox = mediaBox.Bounds.Intersect(cropBox.Bounds) ?? cropBox.Bounds;

            // Scale to desired PPI
            effectiveCropBox = _scale.Transform(effectiveCropBox);
            initialMatrix = initialMatrix.Multiply(in _scale);

            var annotationProvider = new AnnotationProvider(PdfScanner,
                dictionary,
                initialMatrix,
                namedDestinations,
                ParsingOptions.Logger);

            var context = new TextLayerStreamProcessor(pageNumber, ResourceStore, PdfScanner, PageContentParser,
                FilterProvider, cropBox, userSpaceUnit, rotation, initialMatrix,
                effectiveCropBox.Width, effectiveCropBox.Height, _ppiScale,
                ParsingOptions, annotationProvider, CurrentToken);

            return context.Process(pageNumber, operations);
        }
    }
}
