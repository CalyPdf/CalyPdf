using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.Graphics.Operations;
using UglyToad.PdfPig.Graphics.Operations.InlineImages;
using UglyToad.PdfPig.Logging;
using UglyToad.PdfPig.Parser;
using UglyToad.PdfPig.Tokenization.Scanner;
using UglyToad.PdfPig.Tokens;

namespace Caly.Pdf.PageFactories
{
    internal sealed class TextOnlyPageContentParser : IPageContentParser
    {
        private readonly IGraphicsStateOperationFactory operationFactory;
        private readonly bool useLenientParsing;
        private readonly StackDepthGuard stackDepthGuard;

        public TextOnlyPageContentParser(IGraphicsStateOperationFactory operationFactory, StackDepthGuard stackDepthGuard, bool useLenientParsing = false)
        {
            this.operationFactory = operationFactory;
            this.stackDepthGuard = stackDepthGuard;
            this.useLenientParsing = useLenientParsing;
        }

        public IReadOnlyList<IGraphicsStateOperation> Parse(int pageNumber, IInputBytes inputBytes, ILog log)
        {
            var scanner = new CoreTokenScanner(inputBytes, stackDepthGuard, useLenientParsing: useLenientParsing);

            var precedingTokens = new List<IToken>();
            var graphicsStateOperations = new List<IGraphicsStateOperation>();

            while (scanner.MoveNext())
            {
                var token = scanner.CurrentToken;

                if (token is InlineImageDataToken)
                {
                    precedingTokens.Clear();
                }
                else if (token is OperatorToken op)
                {
                    // Handle an end image where the stream of image data contained EI but was not actually a real end image operator.
                    if (op.Data == EndInlineImage.Symbol)
                    {
                        // No op
                    }
                    else
                    {
                        IGraphicsStateOperation? operation;
                        try
                        {
                            operation = operationFactory.Create(op, precedingTokens);
                        }
                        catch (Exception ex)
                        {
                            // End images can cause weird state if the "EI" appears inside the inline data stream.
                            log.Error($"Failed reading operation at offset {inputBytes.CurrentOffset} for page {pageNumber}, data: '{op.Data}'", ex);
                            if (useLenientParsing)
                            {
                                operation = null;
                            }
                            else
                            {
                                throw;
                            }
                        }

                        if (operation != null)
                        {
                            if (!operation.Equals(NoOpGraphicsStateOperation.Instance))
                            {
                                graphicsStateOperations.Add(operation);
                            }
                        }
                        else if (graphicsStateOperations.Count > 0)
                        {
                            if (op.Data == "inf")
                            {
                                // Value representing infinity in broken file from #467.
                                // Treat as zero.
                                precedingTokens.Add(NumericToken.Zero);
                                continue;
                            }

                            log.Warn($"Operator which was not understood encountered. Values was {op.Data}. Ignoring.");
                        }
                    }

                    precedingTokens.Clear();
                }
                else if (token is CommentToken)
                {
                    // No op
                }
                else
                {
                    precedingTokens.Add(token);
                }
            }

            return graphicsStateOperations;
        }
    }
}
