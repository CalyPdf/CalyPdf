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

using UglyToad.PdfPig.Content;

namespace Caly.Pdf.Models;

public sealed record PageTextLayerContent
{
    public required IReadOnlyList<PdfLetter> Letters { get; init; }

    public required IReadOnlyList<PdfAnnotation> Annotations { get; init; }

    /// <summary>
    /// The content visible in <paramref name="state"/> (everything when null). Each distinct condition is
    /// evaluated once.
    /// </summary>
    public PageTextLayerContent ForState(OptionalContentState? state)
    {
        if (state is null)
        {
            return this;
        }

        var visible = new Dictionary<OptionalContentCondition, bool>();
        bool IsVisible(OptionalContentCondition? condition)
        {
            if (condition is null || condition.IsAlways)
            {
                return true;
            }

            if (!visible.TryGetValue(condition, out bool v))
            {
                v = condition.IsVisible(state);
                visible[condition] = v;
            }

            return v;
        }

        return new PageTextLayerContent
        {
            Letters = Letters.Where(l => IsVisible(l.OptionalContent)).ToArray(),
            Annotations = Annotations.Where(a => IsVisible(a.OptionalContent)).ToArray()
        };
    }
}