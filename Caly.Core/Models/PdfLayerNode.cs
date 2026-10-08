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

using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Caly.Core.Models;

/// <summary>
/// An entry of a document's layer tree: a layer (optional content group) with a checkbox, or a text
/// label grouping layers.
/// </summary>
public sealed partial class PdfLayerNode : ObservableObject
{
    private readonly List<PdfLayerNode> _children = new();

    // Set while ApplyState runs, so a state that comes from the document is not reported as a user toggle.
    private bool _isApplyingState;

    public PdfLayerNode(string name, int? groupIndex, PdfLayerNode? parent)
    {
        Name = name;
        GroupIndex = groupIndex;
        Parent = parent;
        IsParentOn = true;
    }

    public string Name { get; }

    /// <summary>
    /// The layer's index in the document's group list; <see langword="null"/> for a label.
    /// </summary>
    public int? GroupIndex { get; }

    public bool IsGroup => GroupIndex.HasValue;

    public PdfLayerNode? Parent { get; }

    public IReadOnlyList<PdfLayerNode> Children => _children;

    /// <summary>
    /// Whether the layer is on. Always <see langword="false"/> for a label.
    /// </summary>
    [ObservableProperty] public partial bool IsOn { get; set; }

    /// <summary>
    /// Whether every enclosing layer is on. When not, this layer's content cannot show whatever its own
    /// state (ISO 32000-2 §8.11.2.1), so the tree dims it.
    /// </summary>
    [ObservableProperty] public partial bool IsParentOn { get; set; }

    /// <summary>
    /// Raised when the user changes <see cref="IsOn"/> (not when the document's state is applied).
    /// </summary>
    public event Action<PdfLayerNode, bool>? Toggled;

    partial void OnIsOnChanged(bool value)
    {
        if (!_isApplyingState)
        {
            Toggled?.Invoke(this, value);
        }
    }

    internal void AddChild(PdfLayerNode child) => _children.Add(child);

    internal void ApplyState(bool isOn, bool isParentOn)
    {
        _isApplyingState = true;
        try
        {
            IsOn = isOn;
            IsParentOn = isParentOn;
        }
        finally
        {
            _isApplyingState = false;
        }
    }
}
