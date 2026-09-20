using System.Globalization;
using System.Text;
using Caly.Pdf;
using Caly.Pdf.Models;
using Caly.Pdf.PageFactories;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Tokens;

namespace Caly.Tests;

/// <summary>
/// Replacement text (<c>/ActualText</c>, PDF specification 14.9.4) applies to the marked-content
/// sequence that carries it. Sequences nest, and a content stream can only end the ones it opened
/// itself, so beginning or ending an inner sequence — or returning from a form XObject — must not
/// disturb the replacement text of whatever encloses it.
/// <para>
/// These mirror PdfPig's own tests for <c>ContentStreamProcessor</c>. Caly's
/// <see cref="Caly.Pdf.TextLayer.TextLayerStreamProcessor"/> is a text-only reimplementation of it,
/// so the two have to agree.
/// </para>
/// </summary>
public class TextLayerActualTextTests
{
    [Fact]
    public void ReplacementAppliesToItsOwnSequenceOnly()
    {
        Assert.Equal("XYB", Extract("/Span <</ActualText (XY)>> BDC\n"
                                    + "(A) Tj\n"
                                    + "EMC\n"
                                    + "(B) Tj\n"));
    }

    [Fact]
    public void EndingAnInnerSequenceDoesNotEndTheEnclosingReplacement()
    {
        Assert.Equal("XY", Extract("/Span <</ActualText (XY)>> BDC\n"
                                   + "/Inner BMC\n"
                                   + "EMC\n"
                                   + "(A) Tj\n"
                                   + "EMC\n"));
    }

    [Fact]
    public void AnInnerSequenceWithoutReplacementInheritsTheEnclosingOne()
    {
        Assert.Equal("XY", Extract("/Span <</ActualText (XY)>> BDC\n"
                                   + "/Inner BMC\n"
                                   + "(A) Tj\n"
                                   + "EMC\n"
                                   + "EMC\n"));
    }

    /// <summary>
    /// The enclosing replacement already stands for the whole of its content, the inner sequence
    /// included, so an inner one would say the same region twice.
    /// </summary>
    [Fact]
    public void AnInnerReplacementIsSubsumedByTheEnclosingOne()
    {
        Assert.Equal("AA", Extract("/A <</ActualText (AA)>> BDC\n"
                                   + "/B <</ActualText (BB)>> BDC\n"
                                   + "(x) Tj\n"
                                   + "EMC\n"
                                   + "(y) Tj\n"
                                   + "EMC\n"));
    }

    /// <summary>
    /// A sequence that follows one carrying replacement text is not enclosed by it, so it brings
    /// its own.
    /// </summary>
    [Fact]
    public void ASiblingSequenceBringsItsOwnReplacement()
    {
        Assert.Equal("AABB", Extract("/A <</ActualText (AA)>> BDC\n"
                                     + "(x) Tj\n"
                                     + "EMC\n"
                                     + "/B <</ActualText (BB)>> BDC\n"
                                     + "(y) Tj\n"
                                     + "EMC\n"));
    }

    /// <summary>
    /// The replacement text stands for the whole sequence, so it is emitted once however many
    /// glyphs the sequence holds, including those in sequences nested inside it.
    /// </summary>
    [Fact]
    public void ReplacementIsEmittedOnceAcrossNestedSequences()
    {
        Assert.Equal("XY", Extract("/Span <</ActualText (XY)>> BDC\n"
                                   + "(A) Tj\n"
                                   + "/Inner BMC\n"
                                   + "(B) Tj\n"
                                   + "EMC\n"
                                   + "(C) Tj\n"
                                   + "EMC\n"));
    }

    /// <summary>
    /// A literal string operand of a text showing operator is a sequence of character codes, not
    /// text. A UTF-16 byte order mark at the head of one is two character codes like any other, so
    /// it must not be taken for an encoding marker and stripped.
    /// </summary>
    [Fact]
    public void AByteOrderMarkInAShownStringIsCharacterCodes()
    {
        Assert.Equal("þÿA", Extract("(þÿA) Tj\n"));
    }

    /// <summary>
    /// The replacement text of an /ActualText entry, on the other hand, is a text string, so a byte
    /// order mark there does say how the rest of it is encoded.
    /// </summary>
    [Fact]
    public void AByteOrderMarkInReplacementTextIsAnEncodingMarker()
    {
        Assert.Equal("XY", Extract("/Span <</ActualText (þÿ\0X\0Y)>> BDC\n"
                                   + "(A) Tj\n"
                                   + "EMC\n"));
    }

    /// <summary>
    /// Properties may be given by name instead of inline, in which case they live in the
    /// /Properties entry of the resource dictionary in scope.
    /// </summary>
    [Fact]
    public void ANamedPropertyDictionaryProvidesReplacementText()
    {
        Assert.Equal("XYB", ExtractWithNamedProperties(
            markedContent: "/Span /MC0 BDC\n(A) Tj\nEMC\n(B) Tj\n",
            properties: "<< /MC0 << /ActualText (XY) >> >>"));
    }

    /// <summary>
    /// The named entry may be an indirect reference rather than the dictionary itself.
    /// </summary>
    [Fact]
    public void ANamedPropertyDictionaryResolvesThroughAnIndirectReference()
    {
        Assert.Equal("XYB", ExtractWithNamedProperties(
            markedContent: "/Span /MC0 BDC\n(A) Tj\nEMC\n(B) Tj\n",
            properties: "<< /MC0 5 0 R >>",
            referencedObject: "<< /ActualText (XY) >>"));
    }

    /// <summary>
    /// A name the resource dictionary does not have leaves the sequence with no properties at all,
    /// so its glyphs are extracted as themselves.
    /// </summary>
    [Fact]
    public void AnUnknownNameLeavesTheSequenceWithoutReplacement()
    {
        Assert.Equal("AB", ExtractWithNamedProperties(
            markedContent: "/Span /Missing BDC\n(A) Tj\nEMC\n(B) Tj\n",
            properties: "<< /MC0 << /ActualText (XY) >> >>"));
    }

    /// <summary>
    /// A form XObject brings its own resource dictionary, so a name inside it resolves against the
    /// form's /Properties and not the page's.
    /// </summary>
    [Fact]
    public void AFormResolvesANameAgainstItsOwnResources()
    {
        Assert.Equal("XYB", ExtractWithForm(
            pageContent: "q\n/Fm1 Do\nQ\nBT\n/F1 12 Tf\n10 20 Td\n(B) Tj\nET\n",
            formContent: "BT\n/F1 12 Tf\n10 50 Td\n/Span /MC0 BDC\n(A) Tj\nEMC\nET\n",
            formProperties: "<< /MC0 << /ActualText (XY) >> >>"));
    }

    /// <summary>
    /// The form leaves its sequence open, so without a boundary its replacement text would stay in
    /// effect afterwards and the rest of the page would be extracted as the empty value the
    /// sequence gives every glyph after its first.
    /// </summary>
    [Fact]
    public void AnUnclosedSequenceInAFormDoesNotCoverTheRestOfThePage()
    {
        Assert.Equal("XYB", ExtractWithForm(
            pageContent: "q\n/Fm1 Do\nQ\nBT\n/F1 12 Tf\n10 20 Td\n(B) Tj\nET\n",
            formContent: "BT\n/F1 12 Tf\n10 50 Td\n/Span <</ActualText (XY)>> BDC\n(A) Tj\nET\n"));
    }

    /// <summary>
    /// The other direction: a form invoked inside a sequence is part of that sequence's content,
    /// so the enclosing replacement still stands for what the form draws and is not repeated once
    /// the form returns.
    /// </summary>
    [Fact]
    public void AnEnclosingReplacementCoversAFormAndIsEmittedOnce()
    {
        Assert.Equal("XYB", ExtractWithForm(
            pageContent: "/Span <</ActualText (XY)>> BDC\nq\n/Fm1 Do\nQ\nEMC\n"
                         + "BT\n/F1 12 Tf\n10 20 Td\n(B) Tj\nET\n",
            formContent: "BT\n/F1 12 Tf\n10 50 Td\n(A) Tj\nET\n"));
    }

    /// <summary>
    /// The form ends a sequence it never began. That cannot be the page's sequence ending, since
    /// the page's own end is still to come, so the form's stray end is ignored and the page's
    /// replacement text goes on covering what follows.
    /// </summary>
    [Fact]
    public void AStrayEndInAFormDoesNotEndTheEnclosingSequence()
    {
        Assert.Equal("XY", ExtractWithForm(
            pageContent: "/Span <</ActualText (XY)>> BDC\nq\n/Fm1 Do\nQ\n"
                         + "BT\n/F1 12 Tf\n10 20 Td\n(B) Tj\nET\nEMC\n",
            formContent: "BT\n/F1 12 Tf\n10 50 Td\n(A) Tj\nET\nEMC\n"));
    }

    /// <summary>
    /// Within the stream that opened it the sequence is still open, so it covers everything that
    /// follows in that stream.
    /// </summary>
    [Fact]
    public void AnUnclosedSequenceCoversTheRestOfItsOwnStream()
    {
        Assert.Equal("XY", ExtractWithForm(
            pageContent: "BT\n/F1 12 Tf\n10 50 Td\n/Span <</ActualText (XY)>> BDC\n(A) Tj\n(B) Tj\nET\n",
            formContent: "BT\n/F1 12 Tf\n10 20 Td\n(C) Tj\nET\n"));
    }

    private static string Extract(string markedContent)
    {
        return ExtractPage(PdfBuilder.SinglePage(
            CharacterCodes("BT\n/F1 12 Tf\n10 50 Td\n" + markedContent + "ET\n")));
    }

    private static string ExtractWithNamedProperties(string markedContent, string properties,
        string? referencedObject = null)
    {
        return ExtractPage(PdfBuilder.SinglePageWithProperties(
            CharacterCodes("BT\n/F1 12 Tf\n10 50 Td\n" + markedContent + "ET\n"),
            properties,
            referencedObject));
    }

    private static string ExtractWithForm(string pageContent, string formContent,
        string? formProperties = null)
    {
        return ExtractPage(PdfBuilder.SinglePageWithForm(
            CharacterCodes(pageContent), CharacterCodes(formContent), formProperties));
    }

    private static string ExtractPage(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf, new ParsingOptions { UseActualText = true });

        // TextLayerFactory reads the PPI scale back out of this fake indirect object, the way
        // PdfPigDocumentService puts it there.
        document.Advanced.ReplaceIndirectObject(CalyPdfHelper.FakePpiReference, new NumericToken(1));
        document.AddPageFactory<PageTextLayerContent, TextLayerFactory>();

        var content = document.GetPageTextLayerContent(1, CancellationToken.None);

        return string.Concat(content.Letters.Select(l => l.Value));
    }

    /// <summary>
    /// A content stream holds character codes, one byte each for the simple font used here, so the
    /// bytes of a code above 0x7F have to survive being written as a char in the test source.
    /// </summary>
    private static byte[] CharacterCodes(string content) => Encoding.Latin1.GetBytes(content);

    /// <summary>
    /// Writes the smallest PDF that can hold a content stream: a catalog, a page tree of one page,
    /// and the objects below, then a cross-reference table over them and a trailer.
    /// </summary>
    private static class PdfBuilder
    {
        private const string Font = "/Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >>";

        public static byte[] SinglePage(byte[] contentStream)
        {
            return Assemble([
                Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
                Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
                Ascii($"<< /Type /Page /Parent 2 0 R /Resources << {Font} >> "
                      + "/MediaBox [0 0 100 100] /Contents 4 0 R >>"),
                Stream("<< /Length {0} >>", contentStream)
            ]);
        }

        /// <summary>
        /// As <see cref="SinglePage"/>, with a /Properties entry in the page's resources. A
        /// <paramref name="referencedObject"/> is written as object 5, for properties that name it
        /// by indirect reference.
        /// </summary>
        public static byte[] SinglePageWithProperties(byte[] contentStream, string properties,
            string? referencedObject)
        {
            var objects = new List<byte[]>
            {
                Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
                Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
                Ascii($"<< /Type /Page /Parent 2 0 R /Resources << {Font} /Properties {properties} >> "
                      + "/MediaBox [0 0 100 100] /Contents 4 0 R >>"),
                Stream("<< /Length {0} >>", contentStream)
            };

            if (referencedObject is not null)
            {
                objects.Add(Ascii(referencedObject));
            }

            return Assemble(objects);
        }

        public static byte[] SinglePageWithForm(byte[] pageContent, byte[] formContent,
            string? formProperties)
        {
            var formResources = formProperties is null
                ? $"<< {Font} >>"
                : $"<< {Font} /Properties {formProperties} >>";

            return Assemble([
                Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
                Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
                Ascii($"<< /Type /Page /Parent 2 0 R /Resources << {Font} /XObject << /Fm1 5 0 R >> >> "
                      + "/MediaBox [0 0 100 100] /Contents 4 0 R >>"),
                Stream("<< /Length {0} >>", pageContent),
                Stream("<< /Type /XObject /Subtype /Form /BBox [0 0 100 100] "
                       + $"/Resources {formResources} /Length {{0}} >>", formContent)
            ]);
        }

        private static byte[] Assemble(IReadOnlyList<byte[]> objects)
        {
            using var ms = new MemoryStream();

            void Write(byte[] b) => ms.Write(b, 0, b.Length);

            Write(Ascii("%PDF-1.7\n"));

            var offsets = new long[objects.Count + 1];

            for (var i = 0; i < objects.Count; i++)
            {
                offsets[i + 1] = ms.Position;

                Write(Ascii($"{Number(i + 1)} 0 obj\n"));
                Write(objects[i]);
                Write(Ascii("\nendobj\n"));
            }

            var startXref = ms.Position;

            Write(Ascii($"xref\n0 {Number(objects.Count + 1)}\n"));
            Write(Ascii("0000000000 65535 f \n"));

            for (var i = 1; i <= objects.Count; i++)
            {
                Write(Ascii($"{offsets[i].ToString("D10", CultureInfo.InvariantCulture)} 00000 n \n"));
            }

            Write(Ascii($"trailer\n<< /Size {Number(objects.Count + 1)} /Root 1 0 R >>\nstartxref\n"));
            Write(Ascii(Number(startXref)));
            Write(Ascii("\n%%EOF\n"));

            return ms.ToArray();
        }

        private static byte[] Stream(string dictionaryFormat, byte[] data)
        {
            using var ms = new MemoryStream();

            var header = Ascii(string.Format(CultureInfo.InvariantCulture, dictionaryFormat, data.Length)
                               + "\nstream\n");

            ms.Write(header, 0, header.Length);
            ms.Write(data, 0, data.Length);

            var footer = Ascii("\nendstream");
            ms.Write(footer, 0, footer.Length);

            return ms.ToArray();
        }

        private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

        private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);
    }
}
