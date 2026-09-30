using BenchmarkDotNet.Attributes;
using Caly.Pdf.Layout;
using Caly.Pdf.Models;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace Caly.Benchmarks
{
    /// <summary>
    /// The text layout pass, from the page's letters to the text blocks: nearest neighbour words, then Docstrum.
    /// </summary>
    [MemoryDiagnoser]
    public class TextLayoutBenchmarks
    {
        [ParamsSource(nameof(Pages))]
        public string Page { get; set; } = null!;

        public static IEnumerable<string> Pages => BenchmarkDocuments.Pages;

        private Letter[] _letters = null!;
        private PdfLetter[] _calyLetters = null!;

        [GlobalSetup]
        public void Setup()
        {
            (_letters, _calyLetters) = BenchmarkDocuments.GetLetters(Page);
        }

        [Benchmark(Baseline = true)]
        public IReadOnlyList<TextBlock> PdfPig()
        {
            var words = NearestNeighbourWordExtractor.Instance.GetWords(_letters);
            return DocstrumBoundingBoxes.Instance.GetBlocks(words);
        }

        [Benchmark]
        public IReadOnlyList<PdfTextBlock> Caly()
        {
            var words = CalyNNWordExtractor.Instance.GetWords(_calyLetters, CancellationToken.None);
            return CalyDocstrum.Instance.GetBlocks(words, CancellationToken.None);
        }
    }
}
