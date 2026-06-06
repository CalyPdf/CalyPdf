using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.Graphics.Operations;
using UglyToad.PdfPig.Graphics.Operations.Compatibility;
using UglyToad.PdfPig.Graphics.Operations.MarkedContent;
using UglyToad.PdfPig.Graphics.Operations.SpecialGraphicsState;
using UglyToad.PdfPig.Graphics.Operations.TextObjects;
using UglyToad.PdfPig.Graphics.Operations.TextPositioning;
using UglyToad.PdfPig.Graphics.Operations.TextShowing;
using UglyToad.PdfPig.Graphics.Operations.TextState;
using UglyToad.PdfPig.Tokens;

namespace Caly.Pdf.PageFactories
{
    public sealed class TextOnlyGraphicsStateOperationFactory : IGraphicsStateOperationFactory
    {
        public static readonly TextOnlyGraphicsStateOperationFactory Instance = new TextOnlyGraphicsStateOperationFactory();

        private TextOnlyGraphicsStateOperationFactory()
        {
            // Private ctor
        }

        private static double[] TokensToDoubleArray(IReadOnlyList<IToken> tokens, bool exceptLast = false)
        {
            using var result = new ArrayPoolBufferWriter<double>(16);

            for (var i = 0; i < tokens.Count - (exceptLast ? 1 : 0); i++)
            {
                var operand = tokens[i];

                if (operand is ArrayToken arr)
                {
                    for (var j = 0; j < arr.Length; j++)
                    {
                        var innerOperand = arr[j];

                        if (!(innerOperand is NumericToken innerNumeric))
                        {
                            return result.WrittenSpan.ToArray();
                        }

                        result.Write(innerNumeric.Data);
                    }
                }

                if (!(operand is NumericToken numeric))
                {
                    return result.WrittenSpan.ToArray();
                }

                result.Write(numeric.Data);
            }

            return result.WrittenSpan.ToArray();
        }

        private static int OperandToInt(IToken token)
        {
            if (!(token is NumericToken numeric))
            {
                throw new InvalidOperationException($"Invalid operand token encountered when expecting numeric: {token}.");
            }

            return numeric.Int;
        }

        private static double OperandToDouble(IToken token)
        {
            if (!(token is NumericToken numeric))
            {
                throw new InvalidOperationException($"Invalid operand token encountered when expecting numeric: {token}.");
            }

            return numeric.Data;
        }

        public IGraphicsStateOperation? Create(OperatorToken op, IReadOnlyList<IToken> operands)
        {
            switch (op.Data)
            {
                case Type3SetGlyphWidth.Symbol:
                    var t3SetWidthArgs = GetExpectedDoubles(Type3SetGlyphWidth.Symbol, operands, 2);
                    return new Type3SetGlyphWidth(t3SetWidthArgs[0], t3SetWidthArgs[1]);
                case Type3SetGlyphWidthAndBoundingBox.Symbol:
                    var t3SetWidthAndBbArgs = GetExpectedDoubles(Type3SetGlyphWidthAndBoundingBox.Symbol, operands, 6);
                    return new Type3SetGlyphWidthAndBoundingBox(
                        t3SetWidthAndBbArgs[0],
                        t3SetWidthAndBbArgs[1],
                        t3SetWidthAndBbArgs[2],
                        t3SetWidthAndBbArgs[3],
                        t3SetWidthAndBbArgs[4],
                        t3SetWidthAndBbArgs[5]);
                case BeginCompatibilitySection.Symbol:
                    return BeginCompatibilitySection.Value;
                case EndCompatibilitySection.Symbol:
                    return EndCompatibilitySection.Value;
                case ModifyCurrentTransformationMatrix.Symbol:
                    return new ModifyCurrentTransformationMatrix(TokensToDoubleArray(operands));
                case Pop.Symbol:
                    return Pop.Value;
                case Push.Symbol:
                    return Push.Value;
                case SetGraphicsStateParametersFromDictionary.Symbol:
                    return new SetGraphicsStateParametersFromDictionary((NameToken)operands[0]);
                case BeginText.Symbol:
                    return BeginText.Value;
                case EndText.Symbol:
                    return EndText.Value;
                case SetCharacterSpacing.Symbol:
                    return new SetCharacterSpacing(OperandToDouble(operands[0]));
                case SetFontAndSize.Symbol:
                    return new SetFontAndSize((NameToken)operands[0], OperandToDouble(operands[1]));
                case SetHorizontalScaling.Symbol:
                    return new SetHorizontalScaling(OperandToDouble(operands[0]));
                case SetTextLeading.Symbol:
                    return new SetTextLeading(OperandToDouble(operands[0]));
                case SetTextRenderingMode.Symbol:
                    return new SetTextRenderingMode(OperandToInt(operands[0]));
                case SetTextRise.Symbol:
                    return new SetTextRise(OperandToDouble(operands[0]));
                case SetWordSpacing.Symbol:
                    return new SetWordSpacing(OperandToDouble(operands[0]));
                case InvokeNamedXObject.Symbol:
                    return new InvokeNamedXObject((NameToken)operands[0]);
                case MoveToNextLine.Symbol:
                    return MoveToNextLine.Value;
                case MoveToNextLineShowText.Symbol:
                {
                    if (operands.Count != 1)
                    {
                        throw new InvalidOperationException($"Attempted to create a move to next line and show text operation with {operands.Count} operands.");
                    }

                    var operand = operands[0];

                    if (operand is StringToken snl)
                    {
                        return new MoveToNextLineShowText(snl.Data);
                    }

                    if (operand is HexToken hnl)
                    {
                        return new MoveToNextLineShowText(hnl.Memory);
                    }

                    throw new InvalidOperationException($"Tried to create a move to next line and show text operation with operand type: {operands[0]?.GetType().Name ?? "null"}");
                }
                case MoveToNextLineWithOffset.Symbol:
                    return new MoveToNextLineWithOffset(OperandToDouble(operands[0]), OperandToDouble(operands[1]));
                case MoveToNextLineWithOffsetSetLeading.Symbol:
                    return new MoveToNextLineWithOffsetSetLeading(OperandToDouble(operands[0]), OperandToDouble(operands[1]));
                case MoveToNextLineShowTextWithSpacing.Symbol:
                {
                    var wordSpacing = (NumericToken)operands[0];
                    var charSpacing = (NumericToken)operands[1];
                    var text = operands[2];

                    if (text is StringToken stringToken)
                    {
                        return new MoveToNextLineShowTextWithSpacing(wordSpacing.Double, charSpacing.Double,
                            stringToken.Data);
                    }

                    if (text is HexToken hexToken)
                    {
                        return new MoveToNextLineShowTextWithSpacing(wordSpacing.Double, charSpacing.Double,
                            hexToken.Memory);
                    }

                    throw new InvalidOperationException($"Tried to create a MoveToNextLineShowTextWithSpacing operation with operand type: {operands[2]?.GetType().Name ?? "null"}");
                }
                case SetTextMatrix.Symbol:
                    return new SetTextMatrix(TokensToDoubleArray(operands));
                case ShowText.Symbol:
                    {
                        if (operands.Count != 1)
                        {
                            throw new InvalidOperationException($"Attempted to create a show text operation with {operands.Count} operands.");
                        }

                        var operand = operands[0];

                        if (operand is StringToken s)
                        {
                            return new ShowText(s.Data);
                        }

                        if (operand is HexToken h)
                        {
                            return new ShowText(h.Bytes.ToArray());
                        }

                        throw new InvalidOperationException($"Tried to create a show text operation with operand type: {operand?.GetType().Name ?? "null"}");
                    }
                case ShowTextsWithPositioning.Symbol:
                    if (operands.Count == 0)
                    {
                        throw new InvalidOperationException("Cannot have 0 parameters for a TJ operator.");
                    }

                    if (operands.Count == 1 && operands[0] is ArrayToken arrayToken)
                    {
                        return new ShowTextsWithPositioning(arrayToken.Data);
                    }

                    return new ShowTextsWithPositioning(operands);
                case BeginMarkedContent.Symbol:
                    return new BeginMarkedContent((NameToken)operands[0]);
                case BeginMarkedContentWithProperties.Symbol:
                {
                    var bdcName = (NameToken)operands[0];
                    var operand = operands[1];
                    
                    if (operand is DictionaryToken contentSequenceDictionary)
                    {
                        return new BeginMarkedContentWithProperties(bdcName, contentSequenceDictionary);
                    }

                    if (operand is NameToken contentSequenceName)
                    {
                        return new BeginMarkedContentWithProperties(bdcName, contentSequenceName);
                    }

                    var errorMessageBdc = string.Join(", ", operands.Select(x => x.ToString()));
                    throw new PdfDocumentFormatException($"Attempted to set a marked-content sequence with invalid parameters: [{errorMessageBdc}]");
                }
                case DesignateMarkedContentPoint.Symbol:
                    return new DesignateMarkedContentPoint((NameToken)operands[0]);
                case DesignateMarkedContentPointWithProperties.Symbol:
                {
                    var dpName = (NameToken)operands[0];
                    var operand = operands[1];
                    
                    if (operand is DictionaryToken contentPointDictionary)
                    {
                        return new DesignateMarkedContentPointWithProperties(dpName, contentPointDictionary);
                    }

                    if (operand is NameToken contentPointName)
                    {
                        return new DesignateMarkedContentPointWithProperties(dpName, contentPointName);
                    }

                    var errorMessageDp = string.Join(", ", operands.Select(x => x.ToString()));
                    throw new PdfDocumentFormatException($"Attempted to set a marked-content point with invalid parameters: [{errorMessageDp}]");
                }
                case EndMarkedContent.Symbol:
                    return EndMarkedContent.Value;
            }

            return NoOpGraphicsStateOperation.Instance;
        }

        private static double[] GetExpectedDoubles(string operatorSymbol, IReadOnlyList<IToken> operands, int resultCount)
        {
            var results = new double[resultCount];

            if (operands.Count < resultCount)
            {
                throw new InvalidOperationException(
                    $"Invalid operands for {operatorSymbol}, needed {resultCount} numbers, got: {PrintOperands(operands)}");
            }

            for (var i = 0; i < resultCount; i++)
            {
                var op = operands[i];

                if (op is not NumericToken nt)
                {
                    throw new InvalidOperationException(
                        $"Invalid operands for {operatorSymbol}, needed {resultCount} numbers, got: {PrintOperands(operands)}");
                }

                results[i] = nt.Data;
            }

            return results;
        }

        private static string PrintOperands(IEnumerable<IToken> operands)
        {
            return "[" + string.Join(", ", operands.Select(x => x.ToString())) + "]";
        }
    }
}
