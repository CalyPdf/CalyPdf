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

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Caly.Avalonia.Pdf.Document;

namespace Caly.Avalonia.Pdf.Document.Tests;

public class ControlTests
{
    [AvaloniaFact]
    public void PdfPageView_defaults_are_sane()
    {
        var p = new PdfPageView();
        Assert.Equal(1.0, p.ZoomLevel);
        Assert.Equal(PdfRenderMode.Tiled, p.RenderMode);
        Assert.Null(p.Picture);
        Assert.Null(p.PdfTextLayer);
        Assert.Null(p.VisibleArea);
    }

    [AvaloniaFact]
    public void PdfDocumentView_defaults_are_sane()
    {
        var v = new PdfDocumentView();
        Assert.Equal(1.0, v.ZoomLevel);
        Assert.NotNull(v.PanCursor);
        Assert.NotNull(v.IbeamCursor);
        Assert.NotNull(v.HandCursor);
    }

    [AvaloniaFact]
    public void GoToPage_does_not_throw_when_empty()
    {
        var v = new PdfDocumentView();
        v.GoToPage(1);
        Assert.True(true);
    }
}
