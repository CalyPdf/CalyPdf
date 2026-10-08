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

using System.Collections.Generic;
using Caly.Core.Models;
using UglyToad.PdfPig.Content;

namespace Caly.Core.Utilities;

/// <summary>
/// Maps a PdfPig optional content state to Caly's layer tree.
/// </summary>
internal static class PdfLayers
{
    public static IReadOnlyList<PdfLayerNode> BuildTree(OptionalContentState state)
    {
        var groupIndices = new Dictionary<OptionalContentGroup, int>();
        for (int i = 0; i < state.Groups.Count; i++)
        {
            groupIndices[state.Groups[i]] = i;
        }

        var roots = new List<PdfLayerNode>(state.Order.Count);
        foreach (var node in state.Order)
        {
            roots.Add(Map(node, null, groupIndices));
        }

        ApplyStates(roots, GetStates(state));
        return roots;
    }

    private static PdfLayerNode Map(OptionalContentOrderNode node, PdfLayerNode? parent,
        Dictionary<OptionalContentGroup, int> groupIndices)
    {
        var mapped = node.Group is { } group
            ? new PdfLayerNode(group.Name, groupIndices[group], parent)
            : new PdfLayerNode(node.Label ?? string.Empty, null, parent);

        foreach (var child in node.Children)
        {
            mapped.AddChild(Map(child, mapped, groupIndices));
        }

        return mapped;
    }

    /// <summary>
    /// Every group's ON/OFF state, indexed like <see cref="OptionalContentState.Groups"/>.
    /// </summary>
    public static IReadOnlyList<bool> GetStates(OptionalContentState state)
    {
        var states = new bool[state.Groups.Count];
        for (int i = 0; i < states.Length; i++)
        {
            states[i] = state.IsOn(state.Groups[i]);
        }

        return states;
    }

    /// <summary>
    /// Sets every node's <see cref="PdfLayerNode.IsOn"/> and <see cref="PdfLayerNode.IsParentOn"/> from
    /// <paramref name="states"/>, without raising <see cref="PdfLayerNode.Toggled"/>.
    /// </summary>
    public static void ApplyStates(IReadOnlyList<PdfLayerNode> roots, IReadOnlyList<bool> states)
    {
        foreach (var root in roots)
        {
            Apply(root, states, true);
        }
    }

    private static void Apply(PdfLayerNode node, IReadOnlyList<bool> states, bool isParentOn)
    {
        bool isOn = node.GroupIndex is int index && index < states.Count && states[index];
        node.ApplyState(isOn, isParentOn);

        // A label does not hide anything: its entries keep the label's own parent state.
        bool childrenParentOn = isParentOn && (!node.IsGroup || isOn);
        foreach (var child in node.Children)
        {
            Apply(child, states, childrenParentOn);
        }
    }

    public static IEnumerable<PdfLayerNode> Flatten(IReadOnlyList<PdfLayerNode> roots)
    {
        var stack = new Stack<PdfLayerNode>();
        for (int i = roots.Count - 1; i >= 0; i--)
        {
            stack.Push(roots[i]);
        }

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            for (int i = node.Children.Count - 1; i >= 0; i--)
            {
                stack.Push(node.Children[i]);
            }
        }
    }
}
