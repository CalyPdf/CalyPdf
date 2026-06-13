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

using Caly.Pdf;
using Caly.Pdf.Models;
using Caly.Pdf.PageFactories;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Actions;
using UglyToad.PdfPig.Tokens;

namespace Caly.Tests;

public class RenditionMediaExtractionTests
{
    private static readonly string PdfPath =
        Path.Combine(AppContext.BaseDirectory, "Documents", "GuitarLovers-LicksRiffs-5v0-en-demo.pdf");

    private static List<PdfRenditionMedia> GetRenditionMedia()
    {
        using var document = PdfDocument.Open(PdfPath, new ParsingOptions { SkipMissingFonts = true });

        // The text layer factory reads a PPI scale stored as an indirect object (see PdfPigDocumentService).
        document.Advanced.ReplaceIndirectObject(CalyPdfHelper.FakePpiReference, new NumericToken(1.0));
        document.AddPageFactory<PageTextLayerContent, TextLayerFactory>();

        var media = new List<PdfRenditionMedia>();
        for (int page = 1; page <= document.NumberOfPages; page++)
        {
            PageTextLayerContent content = document.GetPageTextLayerContent(page, CancellationToken.None);
            media.AddRange(content.Annotations
                .Where(a => a.Rendition is not null)
                .Select(a => a.Rendition!));
        }

        return media;
    }

    [Fact]
    public void Extracts_embedded_mp3_from_every_rendition_annotation()
    {
        List<PdfRenditionMedia> media = GetRenditionMedia();

        // The demo embeds an MP3 "play" rendition on each of its many guitar-lick screen annotations.
        Assert.Equal(68, media.Count);

        foreach (PdfRenditionMedia rendition in media)
        {
            Assert.Equal(RenditionOperation.PlayAndAssociate, rendition.Operation);
            Assert.Equal("audio/mpeg", rendition.ContentType);
            Assert.NotNull(rendition.FileName);
            Assert.EndsWith(".mp3", rendition.FileName, StringComparison.OrdinalIgnoreCase);

            Assert.NotNull(rendition.Data);
            Assert.NotEmpty(rendition.Data);
            Assert.True(IsLikelyMp3(rendition.Data),
                $"Decoded bytes for '{rendition.FileName}' do not look like an MP3 file.");
        }
    }

    [Fact]
    public void Extracts_multiple_distinct_audio_clips()
    {
        List<PdfRenditionMedia> media = GetRenditionMedia();

        string[] distinctNames = media
            .Select(m => m.FileName!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // The demo references several distinct songs (e.g. angie.mp3, back_in_black.mp3, ...).
        Assert.True(distinctNames.Length >= 10,
            $"Expected several distinct clips, found {distinctNames.Length}.");
        Assert.All(distinctNames, name => Assert.EndsWith(".mp3", name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLikelyMp3(byte[] data)
    {
        if (data.Length < 3)
        {
            return false;
        }

        // An ID3v2 tag at the start.
        if (data[0] == (byte)'I' && data[1] == (byte)'D' && data[2] == (byte)'3')
        {
            return true;
        }

        // Or an MPEG audio frame sync (11 bits set: 0xFF followed by 0b111xxxxx) near the start.
        int limit = Math.Min(data.Length - 1, 256);
        for (int i = 0; i < limit; i++)
        {
            if (data[i] == 0xFF && (data[i + 1] & 0xE0) == 0xE0)
            {
                return true;
            }
        }

        return false;
    }
}
