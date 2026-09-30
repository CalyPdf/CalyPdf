using Caly.Pdf.Layout;
using Caly.Pdf.Models;
using Caly.Pdf.PageFactories;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Caly.Benchmarks
{
    /// <summary>
    /// The pages the layout analysis benchmarks run on, as "file:page" so that BenchmarkDotNet shows them as a parameter.
    /// </summary>
    internal static class BenchmarkDocuments
    {
        /// <summary>
        /// A dense page (~26k letters) and a page from a very large document.
        /// </summary>
        public static readonly string[] Pages = ["fseprd1102849.pdf:1", "MOZILLA-7952-0.pdf:2"];

        /// <summary>
        /// The page's letters, as seen by PdfPig and by Caly's text layer.
        /// </summary>
        public static (Letter[] Letters, PdfLetter[] CalyLetters) GetLetters(string page)
        {
            int separator = page.LastIndexOf(':');
            string path = page[..separator];
            int pageNumber = int.Parse(page[(separator + 1)..]);

            using var doc = PdfDocument.Open(path);
            doc.AddPageFactory<PageTextLayerContent, TextLayerFactory>();

            var letters = doc.GetPage(pageNumber).Letters.ToArray();
            var layer = doc.GetPage<PageTextLayerContent>(pageNumber);

            // As PdfTextLayerHelper.GetTextLayer does before extracting the words
            var calyLetters = CalyDuplicateOverlappingTextProcessor.GetInPlace(layer.Letters, CancellationToken.None).ToArray();

            return (letters, calyLetters);
        }
    }
}
