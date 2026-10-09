using Caly.Core.Models;
using Caly.Core.Utilities;
using UglyToad.PdfPig;

namespace Caly.Tests;

public class PdfLayersTests
{
    private static string Gwg151 => Path.Combine(AppContext.BaseDirectory, "Documents", "GWG151_OptionalContent-RBGroup_X4.pdf");

    [Fact]
    public void BuildTree_MapsTheOrderTreeWithTheCurrentStates()
    {
        using var document = PdfDocument.Open(Gwg151);

        var roots = PdfLayers.BuildTree(document.OptionalContent!);

        Assert.Equal(new[] { "Default", "GWG View 1", "GWG View 2" }, roots.Select(n => n.Name));
        Assert.All(roots, n => Assert.True(n.IsGroup));
        Assert.Equal(new[] { true, false, false }, roots.Select(n => n.IsOn));
        Assert.All(roots, n => Assert.True(n.IsParentOn));
    }

    [Fact]
    public void GetStates_IsIndexedLikeTheGroups()
    {
        using var document = PdfDocument.Open(Gwg151);
        var state = document.OptionalContent!;

        var states = PdfLayers.GetStates(state);

        Assert.Equal(state.Groups.Count, states.Count);
        for (int i = 0; i < states.Count; i++)
        {
            Assert.Equal(state.IsOn(state.Groups[i]), states[i]);
        }
    }

    [Fact]
    public void ApplyStates_UpdatesEveryNodeOfAGroupAndDimsChildrenOfOffGroups()
    {
        // Group 0 is listed twice; group 1 is a sublayer of group 0; a label holds group 2.
        var first = new PdfLayerNode("A", 0, null);
        var child = new PdfLayerNode("B", 1, first);
        first.AddChild(child);
        var again = new PdfLayerNode("A", 0, null);
        var label = new PdfLayerNode("Label", null, null);
        var labelled = new PdfLayerNode("C", 2, label);
        label.AddChild(labelled);

        PdfLayers.ApplyStates([first, again, label], [false, true, true]);

        Assert.False(first.IsOn);
        Assert.False(again.IsOn);
        Assert.True(child.IsOn);
        Assert.False(child.IsParentOn);
        Assert.True(labelled.IsParentOn);
        Assert.False(label.IsOn);
    }

    [Fact]
    public void ApplyStates_DoesNotRaiseToggled_ButAUserChangeDoes()
    {
        var node = new PdfLayerNode("A", 0, null);
        var raised = new List<bool>();
        node.Toggled += (_, on) => raised.Add(on);

        PdfLayers.ApplyStates([node], [true]);
        Assert.Empty(raised);

        node.IsOn = false;
        Assert.Equal(new[] { false }, raised);
    }
}
