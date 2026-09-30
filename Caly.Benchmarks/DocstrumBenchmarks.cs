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

using BenchmarkDotNet.Attributes;
using Caly.Pdf.Layout;
using Caly.Pdf.Models;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace Caly.Benchmarks
{
    [MemoryDiagnoser]
    public class DocstrumBenchmarks
    {
        [ParamsSource(nameof(Pages))]
        public string Page { get; set; } = null!;

        public static IEnumerable<string> Pages => BenchmarkDocuments.Pages;

        private Word[] _words = null!;
        private PdfWord[] _calyWords = null!;

        [GlobalSetup]
        public void Setup()
        {
            var (letters, calyLetters) = BenchmarkDocuments.GetLetters(Page);
            _words = NearestNeighbourWordExtractor.Instance.GetWords(letters).ToArray();
            _calyWords = CalyNNWordExtractor.Instance.GetWords(calyLetters, CancellationToken.None).ToArray();
        }

        [Benchmark(Baseline = true)]
        public IReadOnlyList<TextBlock> PdfPig()
        {
            return DocstrumBoundingBoxes.Instance.GetBlocks(_words);
        }

        [Benchmark]
        public IReadOnlyList<PdfTextBlock> Caly()
        {
            return CalyDocstrum.Instance.GetBlocks(_calyWords, CancellationToken.None);
        }


        //[Benchmark]
        //public IReadOnlyList<PdfTextBlock> Caly2()
        //{
        //    return CalyDocstrum2.Instance.GetBlocks(_calyWords); //, CancellationToken.None);
        //}
    }
}