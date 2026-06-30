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

using Avalonia.Headless.XUnit;
using Caly.Avalonia.Pdf.Rendering;
using SkiaSharp;

namespace Caly.Avalonia.Pdf.Rendering.Tests;

public class SkiaPdfPageControlTests
{
    [AvaloniaFact]
    public void Defaults_are_sane()
    {
        var c = new SkiaPdfPageControl();
        Assert.Null(c.Picture);
        Assert.Null(c.VisibleArea);
        Assert.False(c.IsPageVisible);
        Assert.False(c.ShowDiagnosticsOverlay);
    }

    [AvaloniaFact]
    public void Picture_property_round_trips()
    {
        using var recorder = new SKPictureRecorder();
        recorder.BeginRecording(SKRect.Create(10, 10)).Clear(SKColors.White);
        var pic = PdfRef.Create(recorder.EndRecording());

        var c = new SkiaPdfPageControl { Picture = pic };
        Assert.Same(pic, c.Picture);
    }
}
