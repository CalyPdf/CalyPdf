using Caly.Pdf;
using Caly.Pdf.Models;
using Caly.Pdf.PageFactories;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Tokens;

namespace Caly.Tests;

public class TextLayerOptionalContentTests
{
    private static string Gwg151 => Path.Combine(AppContext.BaseDirectory, "Documents", "GWG151_OptionalContent-RBGroup_X4.pdf");

    private static PdfDocument Open()
    {
        var document = PdfDocument.Open(Gwg151);
        document.Advanced.ReplaceIndirectObject(CalyPdfHelper.FakePpiReference, new NumericToken(1));
        document.AddPageFactory<PageTextLayerContent, TextLayerFactory>();
        return document;
    }

    private static string Text(PageTextLayerContent content) => string.Concat(content.Letters.Select(l => l.Value));

    [Fact]
    public void TextLayer_ContainsEveryLayerAndFiltersByState()
    {
        using var document = Open();
        var raw = document.GetPageTextLayerContent(1, CancellationToken.None);
        var state = document.OptionalContent!;

        string all = Text(raw.ForState(null));
        string defaultView = Text(raw.ForState(state));
        string viewOne = Text(raw.ForState(state.WithGroupState(state.Groups.Single(g => g.Name == "GWG View 1"), true)));

        Assert.EndsWith("Default ViewGWG View 1GWG View 2", all);
        Assert.EndsWith("Default View", defaultView);
        Assert.EndsWith("GWG View 1", viewOne);
        Assert.Contains(raw.Letters, l => l.OptionalContent is { IsAlways: false });
    }

    [Fact]
    public void TextLayer_FiltersAnnotationsByState()
    {
        // The document stays open: the condition and the state belong to it.
        using var document = Open();
        var raw = document.GetPageTextLayerContent(1, CancellationToken.None);
        var state = document.OptionalContent!;

        // The last letters ("GWG View 2") are on a layer hidden in the default state.
        var hiddenCondition = raw.Letters[^1].OptionalContent!;
        Assert.False(hiddenCondition.IsVisible(state));

        var visible = new PdfAnnotation { PpiScale = 1, BoundingBox = default };
        var hidden = new PdfAnnotation { PpiScale = 1, BoundingBox = default, OptionalContent = hiddenCondition };
        var content = new PageTextLayerContent { Letters = [], Annotations = [visible, hidden] };

        Assert.Equal(new[] { visible }, content.ForState(state).Annotations);
        Assert.Equal(2, content.ForState(null).Annotations.Count);
    }
}
